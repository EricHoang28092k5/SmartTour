using SmartTour.Shared.Models;
using SmartTourApp.Data;
using SmartTour.Services;

namespace SmartTourApp.Services;

/// <summary>
/// Tracks real-time audio listening duration for all playback sources.
/// Sends stats to backend API after each session ends.
/// </summary>
public class AudioListenTracker
{
    private readonly Database db;
    private readonly ApiService api;

    private int? currentPoiId;
    private double currentLat;
    private double currentLng;
    private DateTime? sessionStart;

    // Accumulated seconds across pause/resume cycles in one session
    private double accumulatedSeconds;
    private bool isTracking;

    public AudioListenTracker(Database db, ApiService api)
    {
        this.db = db;
        this.api = api;
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
            DeviceId = string.Empty,
            UserId = string.Empty
        };

        // Save locally
        try { db.AddLog(log); } catch { }

        // Fire-and-forget to API
        var logCopy = log;
        _ = Task.Run(async () =>
        {
            try { await api.PostPlayLog(logCopy); } catch { }
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
}
