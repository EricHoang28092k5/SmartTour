using SmartTour.Services;
using SmartTour.Shared.Models;
using SmartTourApp.Data;
using System.Text.Json;

namespace SmartTourApp.Services
{
    /// <summary>
    /// OfflineSyncService — Quản lý toàn bộ vòng đời đồng bộ dữ liệu offline.
    ///
    /// ╔══════════════════════════════════════════════════════════════════╗
    /// ║  CHỨC NĂNG CHÍNH                                                ║
    /// ║  1. PRE-FETCH: Tải trước toàn bộ script + audio metadata        ║
    /// ║     vào SQLite khi App có mạng (trước khi tour bắt đầu).        ║
    /// ║  2. VERSION CHECK: So sánh UpdatedAt server vs local để cập nhật║
    /// ║  3. BACKGROUND SYNC: Đẩy PlayLog lên server khi có mạng lại.    ║
    /// ║  4. CONNECTIVITY: Monitor mạng, trigger sync tự động.           ║
    /// ╚══════════════════════════════════════════════════════════════════╝
    /// </summary>
    public partial class OfflineSyncService
    {
        // ══════════════════════════════════════════════════════════════
        // DEPENDENCIES
        // ══════════════════════════════════════════════════════════════

        private readonly ApiService _api;
        private readonly OfflineDatabase _offlineDb;
        private readonly Database _db;

        // ══════════════════════════════════════════════════════════════
        // STATE
        // ══════════════════════════════════════════════════════════════

        private bool _isSyncing = false;
        private bool _isPrefetching = false;
        private CancellationTokenSource? _bgSyncCts;

        private const string LastSyncKey = "offline_last_sync_utc";
        private const string SyncVersionKey = "offline_data_version";
        private const int BgSyncIntervalSeconds = 30;

        // ── Track số lượng để báo cáo progress ──
        public event Action<SyncProgress>? OnProgress;
        public event Action<bool>? OnConnectivityChanged;

        private bool _lastConnectivityState = true;

        // ══════════════════════════════════════════════════════════════
        // CONSTRUCTOR
        // ══════════════════════════════════════════════════════════════

        public OfflineSyncService(ApiService api, OfflineDatabase offlineDb, Database db)
        {
            _api = api;
            _offlineDb = offlineDb;
            _db = db;
        }

        // ══════════════════════════════════════════════════════════════
        // PUBLIC: PRE-FETCH (Yêu cầu 1)
        // Gọi khi User nhấn "Bắt đầu" — tải trước toàn bộ script offline
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Tải trước toàn bộ TTS Script và Audio metadata của danh sách POI.
        /// Lưu vào OfflineDatabase (SQLite) để dùng khi offline.
        /// </summary>
        /// <param name="pois">Danh sách POI cần pre-fetch.</param>
        /// <param name="token">Cancellation token — user có thể hủy.</param>
        public async Task PrefetchPoiDataAsync(List<Poi> pois, CancellationToken token = default)
        {
            if (_isPrefetching) return;
            _isPrefetching = true;

            int done = 0;
            int total = pois.Count;

            try
            {
                ReportProgress(SyncPhase.Prefetch, done, total, "Đang kiểm tra kết nối...");

                // Check connectivity trước
                bool isOnline = await CheckConnectivityAsync();
                if (!isOnline)
                {
                    ReportProgress(SyncPhase.Prefetch, done, total, "Không có mạng — dùng dữ liệu offline");
                    return;
                }

                ReportProgress(SyncPhase.Prefetch, done, total, "Đang tải dữ liệu thuyết minh...");

                // Batch fetch để tối ưu network
                var batches = pois
                    .Select((p, i) => new { p, i })
                    .GroupBy(x => x.i / 5)
                    .Select(g => g.Select(x => x.p).ToList())
                    .ToList();

                foreach (var batch in batches)
                {
                    if (token.IsCancellationRequested) break;

                    var tasks = batch.Select(poi => FetchAndStoreSinglePoiAsync(poi, token));
                    var results = await Task.WhenAll(tasks);

                    done += results.Count(r => r);
                    ReportProgress(SyncPhase.Prefetch, done, total,
                        $"Đã tải {done}/{total} địa điểm...");
                }

                // Lưu timestamp sync
                Preferences.Default.Set(LastSyncKey, DateTime.UtcNow.ToString("O"));

                ReportProgress(SyncPhase.Prefetch, done, total,
                    $"✅ Tải xong {done}/{total} địa điểm — Sẵn sàng offline!");

                System.Diagnostics.Debug.WriteLine(
                    $"[OfflineSync] Pre-fetch done: {done}/{total} POIs");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OfflineSync] Pre-fetch error: {ex.Message}");
                ReportProgress(SyncPhase.Prefetch, done, total, $"Lỗi: {ex.Message}");
            }
            finally
            {
                _isPrefetching = false;
            }
        }

        // ══════════════════════════════════════════════════════════════
        // PUBLIC: VERSION CHECK (Yêu cầu 1)
        // Kiểm tra dữ liệu SQLite có cũ hơn server không
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// So sánh version server vs local. Trả về danh sách POI cần cập nhật.
        /// </summary>
        public async Task<List<int>> CheckStalePoiIdsAsync(List<Poi> serverPois)
        {
            var stale = new List<int>();

            try
            {
                foreach (var serverPoi in serverPois)
                {
                    var localVersion = _offlineDb.GetPoiVersion(serverPoi.Id);

                    // Nếu chưa có data LOCAL hoặc server mới hơn → cần cập nhật
                    if (localVersion == null ||
                        serverPoi.UpdatedAt > localVersion.LastSyncedAt)
                    {
                        stale.Add(serverPoi.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OfflineSync] Version check error: {ex.Message}");
            }

            return stale;
        }

        /// <summary>
        /// Cập nhật dữ liệu cho các POI đã lỗi thời (stale).
        /// </summary>
        public async Task RefreshStaleDataAsync(List<int> stalePoiIds, List<Poi> allPois,
            CancellationToken token = default)
        {
            if (stalePoiIds.Count == 0) return;

            bool isOnline = await CheckConnectivityAsync();
            if (!isOnline) return;

            var stalePois = allPois.Where(p => stalePoiIds.Contains(p.Id)).ToList();
            int done = 0;

            foreach (var poi in stalePois)
            {
                if (token.IsCancellationRequested) break;

                bool ok = await FetchAndStoreSinglePoiAsync(poi, token);
                if (ok) done++;
            }

            System.Diagnostics.Debug.WriteLine(
                $"[OfflineSync] Refreshed {done}/{stalePoiIds.Count} stale POIs");
        }

        // ══════════════════════════════════════════════════════════════
        // PUBLIC: BACKGROUND SYNC (Yêu cầu 5)
        // Đẩy PlayLog offline lên server khi có mạng
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Bắt đầu vòng lặp background sync.
        /// Tự động phát hiện khi có mạng và đẩy log lên server.
        /// </summary>
        public void StartBackgroundSync()
        {
            if (_bgSyncCts != null) return;

            _bgSyncCts = new CancellationTokenSource();
            var token = _bgSyncCts.Token;

            _ = Task.Run(async () =>
            {
                System.Diagnostics.Debug.WriteLine("[OfflineSync] Background sync started");

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        bool isOnline = await CheckConnectivityAsync();

                        // Notify nếu trạng thái mạng thay đổi
                        if (isOnline != _lastConnectivityState)
                        {
                            _lastConnectivityState = isOnline;
                            MainThread.BeginInvokeOnMainThread(() =>
                                OnConnectivityChanged?.Invoke(isOnline));

                            if (isOnline)
                            {
                                System.Diagnostics.Debug.WriteLine(
                                    "[OfflineSync] 🌐 Network restored — triggering sync");
                            }
                        }

                        if (isOnline && !_isSyncing)
                        {
                            await SyncPendingLogsAsync(token);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[OfflineSync] BG loop error: {ex.Message}");
                    }

                    await Task.Delay(BgSyncIntervalSeconds * 1000, token)
                        .ContinueWith(_ => { }); // swallow cancel
                }
            }, token);
        }

        /// <summary>
        /// Dừng background sync loop.
        /// </summary>
        public void StopBackgroundSync()
        {
            _bgSyncCts?.Cancel();
            _bgSyncCts = null;
        }

        /// <summary>
        /// Force sync ngay lập tức (gọi khi biết mạng vừa khôi phục).
        /// </summary>
        public async Task ForceSyncNowAsync(CancellationToken token = default)
        {
            bool isOnline = await CheckConnectivityAsync();
            if (!isOnline) return;

            await SyncPendingLogsAsync(token);
        }

        // ══════════════════════════════════════════════════════════════
        // INTERNAL: FETCH & STORE SINGLE POI
        // ══════════════════════════════════════════════════════════════

        private async Task<bool> FetchAndStoreSinglePoiAsync(Poi poi, CancellationToken token)
        {
            try
            {
                // Lấy audio tracks từ API (có AudioUrl + TtsScript cho mọi ngôn ngữ)
                var response = await _api.GetPoiAudios(poi.Id);
                if (response?.Data == null || response.Data.Count == 0)
                    return false;

                // Lưu từng track vào SQLite
                foreach (var track in response.Data)
                {
                    if (token.IsCancellationRequested) break;

                    _offlineDb.UpsertPoiScript(new OfflinePoiScript
                    {
                        PoiId = poi.Id,
                        LanguageCode = track.LanguageCode,
                        LanguageName = track.LanguageName,
                        Title = track.Title,
                        TtsScript = track.TtsScript ?? "",
                        AudioUrl = track.AudioUrl ?? "",
                        LastSyncedAt = DateTime.UtcNow,
                        ServerUpdatedAt = poi.UpdatedAt
                    });
                }

                // Cập nhật version record
                _offlineDb.UpsertPoiVersion(new OfflinePoiVersion
                {
                    PoiId = poi.Id,
                    LastSyncedAt = DateTime.UtcNow,
                    ServerUpdatedAt = poi.UpdatedAt,
                    TrackCount = response.Data.Count
                });

                System.Diagnostics.Debug.WriteLine(
                    $"[OfflineSync] ✅ POI {poi.Id} ({poi.Name}): " +
                    $"{response.Data.Count} tracks cached");

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[OfflineSync] ❌ POI {poi.Id} fetch error: {ex.Message}");
                return false;
            }
        }

        // ══════════════════════════════════════════════════════════════
        // INTERNAL: SYNC PENDING LOGS (Yêu cầu 5)
        // ══════════════════════════════════════════════════════════════

        private async Task SyncPendingLogsAsync(CancellationToken token)
        {
            if (_isSyncing) return;
            _isSyncing = true;

            try
            {
                var pending = _offlineDb.GetPendingPlayLogs();
                if (pending.Count == 0) return;

                System.Diagnostics.Debug.WriteLine(
                    $"[OfflineSync] Syncing {pending.Count} pending logs...");

                ReportProgress(SyncPhase.LogSync, 0, pending.Count,
                    $"Đang đồng bộ {pending.Count} nhật ký...");

                int synced = 0;
                int failed = 0;

                foreach (var log in pending)
                {
                    if (token.IsCancellationRequested) break;

                    try
                    {
                        await _api.PostPlayLog(new PlayLog
                        {
                            PoiId = log.PoiId,
                            Time = log.PlayedAt,
                            Lat = log.Lat,
                            Lng = log.Lng,
                            DurationListened = log.DurationListened,
                            DeviceId = log.DeviceId,
                            UserId = log.UserId
                        });

                        // Xóa log đã sync thành công
                        _offlineDb.DeleteOfflineLog(log.Id);
                        synced++;
                    }
                    catch
                    {
                        // Tăng retry count — sẽ thử lại lần sau
                        _offlineDb.IncrementLogRetry(log.Id);
                        failed++;
                    }
                }

                System.Diagnostics.Debug.WriteLine(
                    $"[OfflineSync] Log sync done: {synced} ok, {failed} failed");

                if (synced > 0)
                    ReportProgress(SyncPhase.LogSync, synced, pending.Count,
                        $"✅ Đã đồng bộ {synced} nhật ký");
            }
            finally
            {
                _isSyncing = false;
            }
        }

        // ══════════════════════════════════════════════════════════════
        // PUBLIC: OFFLINE LOGGING (Yêu cầu 5)
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Ghi log khi offline — lưu vào SQLite để sync sau.
        /// </summary>
        public void RecordOfflineLog(int poiId, double lat, double lng,
            int durationSec, string deviceId = "", string userId = "")
        {
            try
            {
                _offlineDb.InsertOfflineLog(new OfflinePlayLog
                {
                    PoiId = poiId,
                    PlayedAt = DateTime.UtcNow,
                    Lat = lat,
                    Lng = lng,
                    DurationListened = durationSec,
                    DeviceId = deviceId,
                    UserId = userId,
                    RetryCount = 0
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[OfflineSync] RecordOfflineLog error: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════
        // PUBLIC: LOCAL SCRIPT LOOKUP (Yêu cầu 4)
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Tìm TTS Script từ SQLite theo PoiId + LanguageCode.
        /// Không gọi API — hoàn toàn offline.
        /// </summary>
        public OfflinePoiScript? GetLocalScript(int poiId, string languageCode)
        {
            try
            {
                // Tìm chính xác ngôn ngữ
                var exact = _offlineDb.GetPoiScript(poiId, languageCode);
                if (exact != null) return exact;

                // Fallback: tìm ngôn ngữ có prefix tương tự (vi → vi-VN)
                var prefix = languageCode.Split('-')[0];
                var byPrefix = _offlineDb.GetPoiScriptByPrefix(poiId, prefix);
                if (byPrefix != null) return byPrefix;

                // Fallback cuối: bất kỳ ngôn ngữ nào có sẵn cho POI này
                return _offlineDb.GetAnyScriptForPoi(poiId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[OfflineSync] GetLocalScript error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Lấy tất cả script của một POI từ SQLite (tất cả ngôn ngữ).
        /// </summary>
        public List<OfflinePoiScript> GetAllLocalScripts(int poiId)
        {
            try
            {
                return _offlineDb.GetAllScriptsForPoi(poiId);
            }
            catch
            {
                return new List<OfflinePoiScript>();
            }
        }

        /// <summary>
        /// Kiểm tra POI đã được cached offline chưa.
        /// </summary>
        public bool IsPoiCached(int poiId) =>
            _offlineDb.GetPoiVersion(poiId) != null;

        // ══════════════════════════════════════════════════════════════
        // HELPERS
        // ══════════════════════════════════════════════════════════════

        private static async Task<bool> CheckConnectivityAsync()
        {
            try
            {
                var current = Connectivity.Current.NetworkAccess;
                return current == NetworkAccess.Internet ||
                       current == NetworkAccess.ConstrainedInternet;
            }
            catch
            {
                return false;
            }
        }

        private void ReportProgress(SyncPhase phase, int done, int total, string message)
        {
            try
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    OnProgress?.Invoke(new SyncProgress
                    {
                        Phase = phase,
                        Done = done,
                        Total = total,
                        Message = message,
                        Percent = total > 0 ? (double)done / total : 0
                    });
                });
            }
            catch { }
        }

        // ══════════════════════════════════════════════════════════════
        // DISPOSAL
        // ══════════════════════════════════════════════════════════════

        public void Dispose()
        {
            StopBackgroundSync();
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // DTOs / Models
    // ══════════════════════════════════════════════════════════════════

    public enum SyncPhase { Prefetch, VersionCheck, LogSync }

    public class SyncProgress
    {
        public SyncPhase Phase { get; set; }
        public int Done { get; set; }
        public int Total { get; set; }
        public double Percent { get; set; }
        public string Message { get; set; } = "";
    }
}
