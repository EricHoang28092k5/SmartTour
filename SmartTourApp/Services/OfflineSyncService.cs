using SmartTour.Services;
using SmartTour.Shared.Models;
using SmartTourApp.Data;
using System.Text.Json;

namespace SmartTourApp.Services
{
    /// <summary>
    /// OfflineSyncService — Quản lý toàn bộ vòng đời đồng bộ dữ liệu offline.
    /// YC1: Pre-fetch toàn bộ title + ttsScript + audioUrl + food translations khi lần đầu lấy POIs.
    /// </summary>
    public partial class OfflineSyncService
    {
        private readonly ApiService _api;
        private readonly OfflineDatabase _offlineDb;
        private readonly Database _db;

        private bool _isSyncing = false;
        private bool _isPrefetching = false;
        private CancellationTokenSource? _bgSyncCts;

        private const string LastSyncKey = "offline_last_sync_utc";
        private const string SyncVersionKey = "offline_data_version";
        private const int BgSyncIntervalSeconds = 30;

        public event Action<SyncProgress>? OnProgress;
        public event Action<bool>? OnConnectivityChanged;

        private bool _lastConnectivityState = true;

        public OfflineSyncService(ApiService api, OfflineDatabase offlineDb, Database db)
        {
            _api = api;
            _offlineDb = offlineDb;
            _db = db;
        }

        // ══════════════════════════════════════════════════════════════
        // PUBLIC: PRE-FETCH (YC1) — Tải trước toàn bộ script + food
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// YC1: Tải trước toàn bộ TTS Script, Audio metadata và Food translations.
        /// Gọi sau khi lấy POIs lần đầu từ API.
        /// </summary>
        public async Task PrefetchPoiDataAsync(List<Poi> pois, CancellationToken token = default)
        {
            if (_isPrefetching) return;
            _isPrefetching = true;

            int done = 0;
            int total = pois.Count;

            try
            {
                ReportProgress(SyncPhase.Prefetch, done, total, "Đang kiểm tra kết nối...");

                bool isOnline = await CheckConnectivityAsync();
                if (!isOnline)
                {
                    ReportProgress(SyncPhase.Prefetch, done, total, "Không có mạng — dùng dữ liệu offline");
                    return;
                }

                ReportProgress(SyncPhase.Prefetch, done, total, "Đang tải dữ liệu thuyết minh...");

                // Batch 5 POIs song song
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
        // PUBLIC: VERSION CHECK
        // ══════════════════════════════════════════════════════════════

        public async Task<List<int>> CheckStalePoiIdsAsync(List<Poi> serverPois)
        {
            var stale = new List<int>();
            try
            {
                foreach (var serverPoi in serverPois)
                {
                    var localVersion = _offlineDb.GetPoiVersion(serverPoi.Id);
                    if (localVersion == null || serverPoi.UpdatedAt > localVersion.LastSyncedAt)
                        stale.Add(serverPoi.Id);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OfflineSync] Version check error: {ex.Message}");
            }
            return stale;
        }

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
        // PUBLIC: BACKGROUND SYNC
        // ══════════════════════════════════════════════════════════════

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
                        if (isOnline != _lastConnectivityState)
                        {
                            _lastConnectivityState = isOnline;
                            MainThread.BeginInvokeOnMainThread(() =>
                                OnConnectivityChanged?.Invoke(isOnline));
                            if (isOnline)
                                System.Diagnostics.Debug.WriteLine("[OfflineSync] 🌐 Network restored");
                        }
                        if (isOnline && !_isSyncing)
                            await SyncPendingLogsAsync(token);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[OfflineSync] BG loop error: {ex.Message}");
                    }
                    await Task.Delay(BgSyncIntervalSeconds * 1000, token).ContinueWith(_ => { });
                }
            }, token);
        }

        public void StopBackgroundSync()
        {
            _bgSyncCts?.Cancel();
            _bgSyncCts = null;
        }

        public async Task ForceSyncNowAsync(CancellationToken token = default)
        {
            bool isOnline = await CheckConnectivityAsync();
            if (!isOnline) return;
            await SyncPendingLogsAsync(token);
        }

        // ══════════════════════════════════════════════════════════════
        // INTERNAL: FETCH & STORE SINGLE POI (YC1 + YC4)
        // ══════════════════════════════════════════════════════════════

        private async Task<bool> FetchAndStoreSinglePoiAsync(Poi poi, CancellationToken token)
        {
            try
            {
                // 1. Lấy audio tracks (title + ttsScript + audioUrl) cho tất cả ngôn ngữ
                var response = await _api.GetPoiAudios(poi.Id);
                if (response?.Data != null && response.Data.Count > 0)
                {
                    foreach (var track in response.Data)
                    {
                        if (token.IsCancellationRequested) break;
                        _offlineDb.UpsertPoiScript(new OfflinePoiScript
                        {
                            PoiId = poi.Id,
                            LanguageCode = track.LanguageCode,
                            LanguageName = track.LanguageName,
                            Title = track.Title ?? "",
                            TtsScript = track.TtsScript ?? "",
                            AudioUrl = track.AudioUrl ?? "",
                            LastSyncedAt = DateTime.UtcNow,
                            ServerUpdatedAt = poi.UpdatedAt
                        });
                    }

                    _offlineDb.UpsertPoiVersion(new OfflinePoiVersion
                    {
                        PoiId = poi.Id,
                        LastSyncedAt = DateTime.UtcNow,
                        ServerUpdatedAt = poi.UpdatedAt,
                        TrackCount = response.Data.Count
                    });

                    System.Diagnostics.Debug.WriteLine(
                        $"[OfflineSync] ✅ POI {poi.Id}: {response.Data.Count} tracks cached");
                }

                // 2. YC4: Lấy food translations cho tất cả ngôn ngữ
                await FetchAndStoreFoodTranslationsAsync(poi.Id, token);

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
        // YC4: FOOD TRANSLATIONS
        // ══════════════════════════════════════════════════════════════

        private async Task FetchAndStoreFoodTranslationsAsync(int poiId, CancellationToken token)
        {
            try
            {
                // Lấy foods cho tất cả ngôn ngữ hỗ trợ
                var langs = new[] { "vi", "en", "ja", "zh", "ko" };
                foreach (var lang in langs)
                {
                    if (token.IsCancellationRequested) break;
                    try
                    {
                        var foods = await _api.GetFoodsByPoiAndLang(poiId, lang);
                        if (foods != null && foods.Count > 0)
                        {
                            _offlineDb.UpsertFoodTranslations(poiId, lang, foods);
                            System.Diagnostics.Debug.WriteLine(
                                $"[OfflineSync] Food [{lang}] POI {poiId}: {foods.Count} items cached");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[OfflineSync] Food [{lang}] POI {poiId} error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[OfflineSync] FetchFoodTranslations error: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════
        // INTERNAL: SYNC PENDING LOGS
        // ══════════════════════════════════════════════════════════════

        private async Task SyncPendingLogsAsync(CancellationToken token)
        {
            if (_isSyncing) return;
            _isSyncing = true;
            try
            {
                var pending = _offlineDb.GetPendingPlayLogs();
                if (pending.Count == 0) return;

                System.Diagnostics.Debug.WriteLine($"[OfflineSync] Syncing {pending.Count} pending logs...");
                ReportProgress(SyncPhase.LogSync, 0, pending.Count, $"Đang đồng bộ {pending.Count} nhật ký...");

                int synced = 0, failed = 0;
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
                        _offlineDb.DeleteOfflineLog(log.Id);
                        synced++;
                    }
                    catch
                    {
                        _offlineDb.IncrementLogRetry(log.Id);
                        failed++;
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[OfflineSync] Log sync done: {synced} ok, {failed} failed");
                if (synced > 0)
                    ReportProgress(SyncPhase.LogSync, synced, pending.Count, $"✅ Đã đồng bộ {synced} nhật ký");
            }
            finally
            {
                _isSyncing = false;
            }
        }

        // ══════════════════════════════════════════════════════════════
        // PUBLIC: OFFLINE LOGGING
        // ══════════════════════════════════════════════════════════════

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
                System.Diagnostics.Debug.WriteLine($"[OfflineSync] RecordOfflineLog error: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════
        // PUBLIC: LOCAL SCRIPT LOOKUP (YC1)
        // ══════════════════════════════════════════════════════════════

        public OfflinePoiScript? GetLocalScript(int poiId, string languageCode)
        {
            try
            {
                var exact = _offlineDb.GetPoiScript(poiId, languageCode);
                if (exact != null) return exact;
                var prefix = languageCode.Split('-')[0];
                var byPrefix = _offlineDb.GetPoiScriptByPrefix(poiId, prefix);
                if (byPrefix != null) return byPrefix;
                return _offlineDb.GetAnyScriptForPoi(poiId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OfflineSync] GetLocalScript error: {ex.Message}");
                return null;
            }
        }

        public List<OfflinePoiScript> GetAllLocalScripts(int poiId)
        {
            try { return _offlineDb.GetAllScriptsForPoi(poiId); }
            catch { return new List<OfflinePoiScript>(); }
        }

        /// <summary>
        /// YC1: Lấy title đã được cache theo ngôn ngữ hiện tại.
        /// </summary>
        public string? GetLocalTitle(int poiId, string languageCode)
        {
            try
            {
                var script = GetLocalScript(poiId, languageCode);
                if (script != null && !string.IsNullOrWhiteSpace(script.Title))
                    return script.Title;
                return null;
            }
            catch { return null; }
        }

        /// <summary>
        /// YC4: Lấy food translations đã cache theo ngôn ngữ.
        /// </summary>
        public List<Food> GetLocalFoods(int poiId, string languageCode)
        {
            try
            {
                var foods = _offlineDb.GetFoodTranslations(poiId, languageCode);
                if (foods.Count > 0) return foods;

                // Fallback: tìm ngôn ngữ khác
                var prefix = languageCode.Split('-')[0];
                foods = _offlineDb.GetFoodTranslationsByPrefix(poiId, prefix);
                if (foods.Count > 0) return foods;

                // Fallback cuối: English
                return _offlineDb.GetFoodTranslations(poiId, "en");
            }
            catch { return new List<Food>(); }
        }

        public bool IsPoiCached(int poiId) => _offlineDb.GetPoiVersion(poiId) != null;

        // ══════════════════════════════════════════════════════════════
        // HELPERS
        // ══════════════════════════════════════════════════════════════

        private static async Task<bool> CheckConnectivityAsync()
        {
            try
            {
                var current = Connectivity.Current.NetworkAccess;
                return current == NetworkAccess.Internet || current == NetworkAccess.ConstrainedInternet;
            }
            catch { return false; }
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

        public void Dispose() => StopBackgroundSync();
    }

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
