using SmartTour.Shared.Models;
using SmartTourApp.Data;
using SmartTour.Services;

namespace SmartTourApp.Services;

/// <summary>
/// AudioListenTracker — Ghi nhận thời gian nghe thực tế cho tất cả nguồn phát.
///
/// Enhancement (Yêu cầu 5):
///   - Khi online: gửi log lên API ngay.
///   - Khi offline: lưu vào OfflinePlayLog (SQLite) → sync sau.
/// </summary>
public class AudioListenTracker
{
    private readonly Database db;
    private readonly ApiService api;
    private readonly OfflineSyncService offlineSync;

    private int? currentPoiId;
    private double currentLat;
    private double currentLng;
    private DateTime? sessionStart;

    // Accumulated seconds across pause/resume cycles in one session
    private double accumulatedSeconds;
    private bool isTracking;

    private static readonly string DeviceId =
        Preferences.Default.Get("heatmap_device_id", "");

    public AudioListenTracker(Database db, ApiService api, OfflineSyncService offlineSync)
    {
        this.db = db;
        this.api = api;
        this.offlineSync = offlineSync;
    }

    /// <summary>
    /// Start or resume tracking for a POI.
    /// Call on Play / Resume.
    /// </summary>
    public void StartSession(int poiId, double lat, double lng)
    {
        // If switching POI mid-session, flush the old one first
        if (isTracking && currentPoiId.HasValue && currentPoiId.Value != poiId)
            FlushSession();

        currentPoiId = poiId;
        currentLat = lat;
        currentLng = lng;
        sessionStart = DateTime.Now;
        isTracking = true;
    }

    /// <summary>
    /// Pause tracking (user paused / skipped). Accumulates elapsed seconds.
    /// </summary>
    public void PauseSession()
    {
        if (!isTracking || sessionStart == null) return;

        accumulatedSeconds += (DateTime.Now - sessionStart.Value).TotalSeconds;
        sessionStart = null;
        isTracking = false;
    }

    /// <summary>
    /// Stop and flush the current session to DB + API.
    /// Call on Stop / audio completed.
    /// </summary>
    public void StopSession()
    {
        if (sessionStart != null)
        {
            accumulatedSeconds += (DateTime.Now - sessionStart.Value).TotalSeconds;
            sessionStart = null;
        }

        isTracking = false;
        FlushSession();
    }

    /// <summary>
    /// Called when skip occurs — pause accumulation, caller will StartSession again on resume.
    /// </summary>
    public void OnSkip()
    {
        PauseSession();
    }

    private void FlushSession()
    {
        if (currentPoiId == null || accumulatedSeconds < 1)
        {
            ResetState();
            return;
        }

        var log = new PlayLog
        {
            PoiId = currentPoiId.Value,
            Time = DateTime.Now,
            Lat = currentLat,
            Lng = currentLng,
            DurationListened = (int)Math.Round(accumulatedSeconds),
            DeviceId = DeviceId,
            UserId = string.Empty
        };

        // Save locally to SQLite
        try { db.AddLog(log); } catch { }

        // ── Yêu cầu 5: Online → API ngay / Offline → OfflinePlayLog ──
        var logCopy = log;
        var poiId = currentPoiId.Value;
        var lat = currentLat;
        var lng = currentLng;
        var duration = (int)Math.Round(accumulatedSeconds);

        _ = Task.Run(async () =>
        {
            bool isOnline = IsOnline();

            if (isOnline)
            {
                // Online: gửi ngay
                try
                {
                    await api.PostPlayLog(logCopy);
                    System.Diagnostics.Debug.WriteLine(
                        $"[Tracker] ✅ Online log sent: POI={poiId}, " +
                        $"duration={duration}s");
                }
                catch (Exception ex)
                {
                    // API fail → fallback sang offline log
                    System.Diagnostics.Debug.WriteLine(
                        $"[Tracker] API fail, saving offline: {ex.Message}");
                    offlineSync.RecordOfflineLog(
                        poiId, lat, lng, duration,
                        DeviceId, string.Empty);
                }
            }
            else
            {
                // Offline: lưu vào SQLite chờ sync
                offlineSync.RecordOfflineLog(
                    poiId, lat, lng, duration,
                    DeviceId, string.Empty);

                System.Diagnostics.Debug.WriteLine(
                    $"[Tracker] 📦 Offline log queued: POI={poiId}, " +
                    $"duration={duration}s");
            }
        });

        ResetState();
    }

    private void ResetState()
    {
        currentPoiId = null;
        accumulatedSeconds = 0;
        sessionStart = null;
        isTracking = false;
    }

    private static bool IsOnline()
    {
        try
        {
            var access = Connectivity.Current.NetworkAccess;
            return access == NetworkAccess.Internet ||
                   access == NetworkAccess.ConstrainedInternet;
        }
        catch { return false; }
    }
}
