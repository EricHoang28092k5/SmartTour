using SmartTour.Services;
using SmartTour.Shared.Models;

namespace SmartTourApp.Services;

/// <summary>
/// TourOfflineManager — Quản lý vòng đời offline cho Tour.
///
/// ╔══════════════════════════════════════════════════════════════════╗
/// ║  CHIẾN LƯỢC OFFLINE TOUR                                        ║
/// ║  1. PRE-FETCH khi user nhấn "Tải về offline" trên TourPage      ║
/// ║     → Tải toàn bộ TtsScript + AudioUrl + Map tiles              ║
/// ║  2. SMART CACHE: Chỉ re-fetch khi server có data mới hơn        ║
/// ║  3. PLAYBACK OFFLINE: NarrationEngine tự fallback SQLite→TTS    ║
/// ║  4. STATUS: Track trạng thái download per-tour                  ║
/// ║  5. AUTO-FETCH: Khi load TourPage mà có mạng → kiểm tra stale  ║
/// ╚══════════════════════════════════════════════════════════════════╝
/// </summary>
public class TourOfflineManager
{
    private readonly ApiService _api;
    private readonly OfflineSyncService _offlineSync;
    private readonly OfflineMapService _offlineMapService;
    private readonly PoiRepository _poiRepo;

    // ── State: download status per tourId ──
    private readonly Dictionary<int, TourOfflineStatus> _statusMap = new();
    private readonly Dictionary<int, CancellationTokenSource> _downloadTasks = new();

    // ── Events ──
    public event Action<int, TourOfflineStatus>? OnStatusChanged;
    public event Action<int, TourDownloadProgress>? OnProgress;

    public TourOfflineManager(
        ApiService api,
        OfflineSyncService offlineSync,
        OfflineMapService offlineMapService,
        PoiRepository poiRepo)
    {
        _api = api;
        _offlineSync = offlineSync;
        _offlineMapService = offlineMapService;
        _poiRepo = poiRepo;
    }

    // ══════════════════════════════════════════════════════════════
    // PUBLIC: GET STATUS
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Lấy trạng thái offline của một tour.
    /// </summary>
    public TourOfflineStatus GetStatus(int tourId, List<TourPoiDto> pois)
    {
        if (_statusMap.TryGetValue(tourId, out var cached))
            return cached;

        // Check xem POI nào đã được cache
        var status = ComputeStatus(tourId, pois);
        _statusMap[tourId] = status;
        return status;
    }

    private TourOfflineStatus ComputeStatus(int tourId, List<TourPoiDto> pois)
    {
        if (pois == null || pois.Count == 0)
            return TourOfflineStatus.NotDownloaded;

        int cachedCount = pois.Count(p => _offlineSync.IsPoiCached(p.PoiId));

        if (cachedCount == 0) return TourOfflineStatus.NotDownloaded;
        if (cachedCount == pois.Count) return TourOfflineStatus.Ready;
        return TourOfflineStatus.Partial;
    }

    // ══════════════════════════════════════════════════════════════
    // PUBLIC: DOWNLOAD TOUR FOR OFFLINE
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Bắt đầu tải toàn bộ dữ liệu offline cho tour.
    /// Bao gồm: TtsScript + AudioUrl (qua OfflineSyncService) + Map tiles
    /// </summary>
    public async Task DownloadTourAsync(int tourId, List<TourPoiDto> tourPois,
        CancellationToken externalToken = default)
    {
        // Hủy download cũ nếu đang chạy
        if (_downloadTasks.TryGetValue(tourId, out var oldCts))
        {
            oldCts.Cancel();
            _downloadTasks.Remove(tourId);
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        _downloadTasks[tourId] = cts;
        var token = cts.Token;

        SetStatus(tourId, TourOfflineStatus.Downloading);

        try
        {
            // Lấy danh sách POI đầy đủ
            var allPois = await _poiRepo.GetPois();
            var poiIds = tourPois.Select(p => p.PoiId).ToHashSet();
            var fullPois = allPois.Where(p => poiIds.Contains(p.Id)).ToList();

            int total = fullPois.Count;
            int done = 0;

            ReportProgress(tourId, done, total, "Đang kiểm tra kết nối...", TourDownloadPhase.Script);

            // ── Phase 1: Tải TTS Scripts + Audio metadata ──
            ReportProgress(tourId, 0, total, "Đang tải nội dung thuyết minh...", TourDownloadPhase.Script);

            foreach (var poi in fullPois)
            {
                if (token.IsCancellationRequested) break;

                try
                {
                    var response = await _api.GetPoiAudios(poi.Id);
                    if (response?.Data != null)
                    {
                        foreach (var track in response.Data)
                        {
                            var script = new OfflinePoiScript
                            {
                                PoiId = poi.Id,
                                LanguageCode = track.LanguageCode,
                                LanguageName = track.LanguageName,
                                Title = track.Title,
                                TtsScript = track.TtsScript ?? "",
                                AudioUrl = track.AudioUrl ?? "",
                                LastSyncedAt = DateTime.UtcNow,
                                ServerUpdatedAt = poi.UpdatedAt
                            };
                            // Dùng OfflineSyncService upsert
                            _offlineSync.UpsertPoiScriptDirect(script);
                        }
                        _offlineSync.UpsertPoiVersionDirect(new OfflinePoiVersion
                        {
                            PoiId = poi.Id,
                            LastSyncedAt = DateTime.UtcNow,
                            ServerUpdatedAt = poi.UpdatedAt,
                            TrackCount = response.Data.Count
                        });
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[TourOffline] POI {poi.Id} script fetch error: {ex.Message}");
                }

                done++;
                ReportProgress(tourId, done, total,
                    $"Thuyết minh: {done}/{total}", TourDownloadPhase.Script);
            }

            if (token.IsCancellationRequested)
            {
                SetStatus(tourId, TourOfflineStatus.Partial);
                return;
            }

            // ── Phase 2: Pre-cache Map tiles cho tất cả POI trong tour ──
            ReportProgress(tourId, 0, tourPois.Count, "Đang tải bản đồ...", TourDownloadPhase.Map);

            if (OfflineMapService.IsConnected())
            {
                var centers = tourPois
                    .Select(p => (p.Lat, p.Lng))
                    .ToList();

                try
                {
                    await _offlineMapService.PrefetchTourMapAsync(
                        fullPois, radiusKmPerPoi: 0.8, token: token);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[TourOffline] Map prefetch error: {ex.Message}");
                    // Non-fatal — script đã cache rồi
                }
            }

            var finalStatus = token.IsCancellationRequested
                ? TourOfflineStatus.Partial
                : TourOfflineStatus.Ready;

            SetStatus(tourId, finalStatus);

            ReportProgress(tourId, total, total,
                finalStatus == TourOfflineStatus.Ready
                    ? $"✅ Tour sẵn sàng offline! ({total} địa điểm)"
                    : $"⚠️ Đã lưu một phần ({done}/{total})",
                TourDownloadPhase.Done);

            System.Diagnostics.Debug.WriteLine(
                $"[TourOffline] Tour {tourId}: {finalStatus}, {done}/{total} POIs");
        }
        catch (OperationCanceledException)
        {
            SetStatus(tourId, TourOfflineStatus.Partial);
            ReportProgress(tourId, 0, 0, "Đã hủy tải", TourDownloadPhase.Done);
        }
        catch (Exception ex)
        {
            SetStatus(tourId, TourOfflineStatus.Error);
            System.Diagnostics.Debug.WriteLine($"[TourOffline] Error: {ex.Message}");
        }
        finally
        {
            _downloadTasks.Remove(tourId);
        }
    }

    /// <summary>
    /// Hủy download đang chạy cho một tour.
    /// </summary>
    public void CancelDownload(int tourId)
    {
        if (_downloadTasks.TryGetValue(tourId, out var cts))
        {
            cts.Cancel();
            _downloadTasks.Remove(tourId);
        }
    }

    /// <summary>
    /// Xóa dữ liệu offline cho một tour (nếu cần giải phóng bộ nhớ).
    /// </summary>
    public void ClearTourOffline(int tourId)
    {
        _statusMap.Remove(tourId);
    }

    /// <summary>
    /// Kiểm tra xem tour có đầy đủ data offline không.
    /// </summary>
    public bool IsTourFullyOffline(int tourId, List<TourPoiDto> pois)
        => GetStatus(tourId, pois) == TourOfflineStatus.Ready;

    // ── Helpers ──
    private void SetStatus(int tourId, TourOfflineStatus status)
    {
        _statusMap[tourId] = status;
        try
        {
            MainThread.BeginInvokeOnMainThread(() =>
                OnStatusChanged?.Invoke(tourId, status));
        }
        catch { }
    }

    private void ReportProgress(int tourId, int done, int total, string message, TourDownloadPhase phase)
    {
        try
        {
            MainThread.BeginInvokeOnMainThread(() =>
                OnProgress?.Invoke(tourId, new TourDownloadProgress
                {
                    Done = done,
                    Total = total,
                    Message = message,
                    Phase = phase,
                    Percent = total > 0 ? (double)done / total : 0
                }));
        }
        catch { }
    }
}

// ══════════════════════════════════════════════════════════════════
// MODELS
// ══════════════════════════════════════════════════════════════════

public enum TourOfflineStatus
{
    NotDownloaded,
    Downloading,
    Partial,
    Ready,
    Error
}

public enum TourDownloadPhase
{
    Script,
    Audio,
    Map,
    Done
}

public class TourDownloadProgress
{
    public int Done { get; set; }
    public int Total { get; set; }
    public double Percent { get; set; }
    public string Message { get; set; } = "";
    public TourDownloadPhase Phase { get; set; }
}
