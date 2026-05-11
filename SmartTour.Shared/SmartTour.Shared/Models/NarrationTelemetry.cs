namespace SmartTour.Shared.Models;

/// <summary>
/// Kết quả một vòng thuyết minh tự động (geofence) — dùng cho simulator / tích hợp.
/// </summary>
public enum NarrationCycleOutcome
{
    Completed,
    SkippedCooldown,
    Error
}

public sealed class NarrationCompletedEventArgs : EventArgs
{
    public int PoiId { get; }
    public double Latitude { get; }
    public double Longitude { get; }
    public NarrationCycleOutcome Outcome { get; }

    public NarrationCompletedEventArgs(int poiId, double latitude, double longitude, NarrationCycleOutcome outcome)
    {
        PoiId = poiId;
        Latitude = latitude;
        Longitude = longitude;
        Outcome = outcome;
    }
}

/// <summary>
/// Bus tĩnh để OverlapLogRunner (và công cụ khác) subscribe cùng luồng telemetry với app.
/// </summary>
public static class NarrationTelemetryBus
{
    public static event EventHandler<NarrationCompletedEventArgs>? NarrationCompleted;

    public static void Publish(NarrationCompletedEventArgs args)
    {
        NarrationCompleted?.Invoke(null, args);
    }
}
