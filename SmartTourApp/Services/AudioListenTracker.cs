using SmartTour.Shared.Models;
using SmartTourApp.Data;
using SmartTour.Services;

namespace SmartTourApp.Services;

/// <summary>
/// AudioListenTracker — Ghi nhận thời gian nghe thực tế.
///
/// ╔══════════════════════════════════════════════════════════════════╗
/// ║  NGUYÊN TẮC CỐT LÕI (Yêu cầu 3)                               ║
/// ║  • Chỉ tích lũy thời gian khi isTracking = TRUE                ║
/// ║  • Pause()  → isTracking = false, snapshot thời gian tích lũy  ║
/// ║  • Seek()   → PauseSession() ngay → snapshot, KHÔNG tính giây  ║
/// ║    quãng seek. StartSession() mới sẽ bắt đầu từ vị trí mới.   ║
/// ║  • Resume() → StartSession() mở phiên mới từ vị trí hiện tại  ║
/// ║  • Stop()   → FlushSession() ghi log nếu ≥ 1 giây thực nghe   ║
/// ║                                                                  ║
/// ║  ONLINE/OFFLINE (Yêu cầu 5)                                    ║
/// ║  • Online  → PostPlayLog ngay lập tức                          ║
/// ║  • Offline → RecordOfflineLog → sync sau                       ║
/// ╚══════════════════════════════════════════════════════════════════╝
/// </summary>
public class AudioListenTracker
{
    // ══════════════════════════════════════════════════════════════════
    // DEPENDENCIES
    // ══════════════════════════════════════════════════════════════════

    private readonly Database db;
    private readonly ApiService api;
    private readonly OfflineSyncService offlineSync;

    // ══════════════════════════════════════════════════════════════════
    // SESSION STATE
    // ══════════════════════════════════════════════════════════════════

    private int? currentPoiId;
    private double currentLat;
    private double currentLng;

    /// <summary>
    /// Thời điểm phiên nghe HIỆN TẠI bắt đầu.
    /// Null khi đang Paused hoặc chưa phát.
    /// </summary>
    private DateTime? sessionStart;

    /// <summary>
    /// Tổng giây đã nghe thực tế (tích lũy qua nhiều lần Pause/Resume).
    /// KHÔNG bao gồm khoảng seek.
    /// </summary>
    private double accumulatedSeconds;

    /// <summary>
    /// TRUE chỉ khi audio đang thực sự phát (không phải pause/seek).
    /// Đây là guard chính để tránh ghi log sai.
    /// </summary>
    private bool isTracking;

    // ══════════════════════════════════════════════════════════════════
    // DEVICE ID
    // ══════════════════════════════════════════════════════════════════

    private static readonly string DeviceId =
        Preferences.Default.Get("heatmap_device_id", "");

    // ══════════════════════════════════════════════════════════════════
    // CONSTRUCTOR
    // ══════════════════════════════════════════════════════════════════

    public AudioListenTracker(Database db, ApiService api, OfflineSyncService offlineSync)
    {
        this.db = db;
        this.api = api;
        this.offlineSync = offlineSync;
    }

    // ══════════════════════════════════════════════════════════════════
    // PUBLIC API
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Bắt đầu hoặc tiếp tục ghi nhận thời gian nghe cho một POI.
    /// Gọi khi: Play / Resume.
    ///
    /// Nếu đang theo dõi POI khác → flush phiên cũ trước.
    /// </summary>
    public void StartSession(int poiId, double lat, double lng)
    {
        // Nếu đang theo dõi POI khác → flush trước
        if (isTracking && currentPoiId.HasValue && currentPoiId.Value != poiId)
        {
            FlushSession();
        }

        // Nếu đổi POI nhưng chưa flush (paused state) → flush
        if (!isTracking && currentPoiId.HasValue && currentPoiId.Value != poiId)
        {
            FlushSession();
        }

        currentPoiId = poiId;
        currentLat = lat;
        currentLng = lng;

        // Bắt đầu tính giờ từ đây — chỉ kể từ thời điểm này
        sessionStart = DateTime.Now;
        isTracking = true;

        System.Diagnostics.Debug.WriteLine(
            $"[Tracker] ▶ StartSession POI={poiId} | accumulated={accumulatedSeconds:F1}s");
    }

    /// <summary>
    /// Tạm dừng ghi nhận (Pause hoặc trước Seek).
    /// Snapshot thời gian đã nghe → dừng đồng hồ, KHÔNG flush.
    /// </summary>
    public void PauseSession()
    {
        if (!isTracking || sessionStart == null)
        {
            // Đã pause rồi hoặc chưa bắt đầu — không làm gì
            return;
        }

        var elapsed = (DateTime.Now - sessionStart.Value).TotalSeconds;
        accumulatedSeconds += elapsed;
        sessionStart = null;
        isTracking = false;

        System.Diagnostics.Debug.WriteLine(
            $"[Tracker] ⏸ PauseSession | elapsed={elapsed:F1}s | total={accumulatedSeconds:F1}s");
    }

    /// <summary>
    /// Dừng hoàn toàn và ghi log nếu đã nghe ≥ 1 giây.
    /// Gọi khi: Stop / audio kết thúc tự nhiên.
    /// </summary>
    public void StopSession()
    {
        if (sessionStart != null)
        {
            var elapsed = (DateTime.Now - sessionStart.Value).TotalSeconds;
            accumulatedSeconds += elapsed;
            sessionStart = null;
        }

        isTracking = false;

        System.Diagnostics.Debug.WriteLine(
            $"[Tracker] ⏹ StopSession | total={accumulatedSeconds:F1}s");

        FlushSession();
    }

    /// <summary>
    /// Gọi khi người dùng kéo Seek — chốt log đoạn đã nghe,
    /// KHÔNG tính quãng thời gian bị skip.
    ///
    /// Flow: OnSeekStarted → OnSkip() → PauseSession()
    ///       OnSeekCompleted → audio.Seek() → StartSession() (vị trí mới)
    /// </summary>
    public void OnSkip()
    {
        // Đóng băng log đoạn hiện tại — quãng seek không được tính
        PauseSession();

        System.Diagnostics.Debug.WriteLine(
            $"[Tracker] ⏭ OnSkip | accumulated={accumulatedSeconds:F1}s (frozen)");
    }

    // ══════════════════════════════════════════════════════════════════
    // INTERNAL: FLUSH
    // ══════════════════════════════════════════════════════════════════

    private void FlushSession()
    {
        // Guard: chỉ ghi khi đã nghe thực sự ít nhất 1 giây
        if (currentPoiId == null || accumulatedSeconds < 1.0)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Tracker] ⚡ Flush skipped: " +
                $"poiId={currentPoiId}, seconds={accumulatedSeconds:F1}");
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

        System.Diagnostics.Debug.WriteLine(
            $"[Tracker] 💾 Flush: POI={log.PoiId}, duration={log.DurationListened}s");

        // Lưu local SQLite (luôn luôn, backup)
        try { db.AddLog(log); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Tracker] SQLite error: {ex.Message}");
        }

        // Fire-and-forget: gửi API online hoặc queue offline
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
                try
                {
                    await api.PostPlayLog(logCopy);
                    System.Diagnostics.Debug.WriteLine(
                        $"[Tracker] ✅ Online log sent: POI={poiId}, duration={duration}s");
                }
                catch (Exception ex)
                {
                    // API fail → fallback sang offline queue
                    System.Diagnostics.Debug.WriteLine(
                        $"[Tracker] API fail → offline queue: {ex.Message}");
                    offlineSync.RecordOfflineLog(poiId, lat, lng, duration, DeviceId, string.Empty);
                }
            }
            else
            {
                // Offline → queue để sync sau
                offlineSync.RecordOfflineLog(poiId, lat, lng, duration, DeviceId, string.Empty);
                System.Diagnostics.Debug.WriteLine(
                    $"[Tracker] 📦 Offline queued: POI={poiId}, duration={duration}s");
            }
        });

        ResetState();
    }

    // ══════════════════════════════════════════════════════════════════
    // RESET
    // ══════════════════════════════════════════════════════════════════

    private void ResetState()
    {
        currentPoiId = null;
        accumulatedSeconds = 0;
        sessionStart = null;
        isTracking = false;
    }

    // ══════════════════════════════════════════════════════════════════
    // HELPERS
    // ══════════════════════════════════════════════════════════════════

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
