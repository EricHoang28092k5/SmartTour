using SmartTour.Shared.Models;
using SmartTourApp.Services;

public class TrackingService
{
    private readonly LocationService location;
    private readonly GeofencingEngine geo;
    private readonly NarrationEngine narration;
    private readonly PoiRepository repo;
    private readonly LocationLogger logger;
    private readonly HeatmapService heatmap;

    private CancellationTokenSource? cts;

    private List<Poi> pois = new();
    private Location? lastLocation;
    private int interval = 3;

    public event Action<Location>? OnLocationChanged;

    public TrackingService(
        LocationService location,
        GeofencingEngine geo,
        NarrationEngine narration,
        PoiRepository repo,
        LocationLogger logger,
        HeatmapService heatmap)
    {
        this.location = location;
        this.geo = geo;
        this.narration = narration;
        this.repo = repo;
        this.logger = logger;
        this.heatmap = heatmap;
    }

    public async Task Start()
    {
        if (cts != null) return;

        cts = new CancellationTokenSource();
        var token = cts.Token;

        pois = await repo.GetPois();

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var loc = await location.GetLocation();

                    if (loc == null)
                    {
                        await Task.Delay(5000, token);
                        continue;
                    }

                    logger.Log(loc);
                    OnLocationChanged?.Invoke(loc);

                    AdjustInterval(loc);

                    // ─── Narration (geofencing trigger) ───
                    var poi = geo.FindBestPoi(loc, pois);
                    if (poi != null)
                        await narration.Play(poi, loc);

                    // ─── 🔥 Heatmap: detect zone_enter edge trigger ───
                    // OnLocationUpdatedAsync dùng state machine nội bộ để chỉ ghi nhận
                    // khi bước vào từ bên ngoài, không ghi lại khi đi vòng vòng trong radius.
                    await heatmap.OnLocationUpdatedAsync(loc, pois);

                    await Task.Delay(interval * 1000, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Tracking error: " + ex.Message);
                }
            }
        }, token);
    }

    public void Stop()
    {
        cts?.Cancel();
        cts = null;
    }

    private void AdjustInterval(Location loc)
    {
        if (lastLocation == null)
        {
            lastLocation = loc;
            return;
        }

        var dist = Location.CalculateDistance(
            lastLocation,
            loc,
            DistanceUnits.Kilometers);

        if (dist < 0.01)
            interval = 15;
        else if (dist < 0.05)
            interval = 10;
        else
            interval = 5;

        lastLocation = loc;
    }
}
