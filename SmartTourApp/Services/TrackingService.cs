using SmartTour.Shared.Models;
using SmartTourApp.Services;
using SmartTourApp.Pages; // để dùng SettingsPage.AutoPlayKey

public class TrackingService
{
    private readonly LocationService location;
    private readonly GeofencingEngine geo;
    private readonly NarrationEngine narration;
    private readonly PoiRepository repo;
    private readonly LocationLogger logger;
    private readonly HeatmapService heatmap;
    private readonly RouteTrackingService routeTracking;

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
        HeatmapService heatmap,
        RouteTrackingService routeTracking)
    {
        this.location = location;
        this.geo = geo;
        this.narration = narration;
        this.repo = repo;
        this.logger = logger;
        this.heatmap = heatmap;
        this.routeTracking = routeTracking;
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

                    // ─────────────────────────────────────────────────────────
                    // Yêu cầu 2: Đọc trạng thái auto-play mỗi chu kỳ
                    // → cập nhật ngay lập tức khi người dùng gạt switch trong Settings
                    // Mặc định true nếu chưa được set (người dùng mới)
                    // ─────────────────────────────────────────────────────────
                    bool autoPlayEnabled = Preferences.Default.Get(
                        SettingsPage.AutoPlayKey, true);

                    if (autoPlayEnabled)
                    {
                        // ─── AUTO PLAY: chỉ gọi narration khi auto-play BẬT ───
                        // Narration.Play sẽ ghi Play Log (DurationListened) thông qua AudioListenTracker
                        var poi = geo.FindBestPoi(loc, pois);
                        if (poi != null)
                            await narration.Play(poi, loc);
                    }
                    else
                    {
                        // ─── AUTO PLAY TẮT: KHÔNG gọi narration, KHÔNG ghi Play Log ───
                        // Nhưng vẫn phải cập nhật state machine geofencing
                        // để FindBestPoi hoạt động đúng khi bật lại
                        geo.FindBestPoi(loc, pois); // chỉ update state, bỏ qua kết quả
                    }

                    // ─────────────────────────────────────────────────────────
                    // Yêu cầu 2: Heatmap luôn được ghi nhận
                    // KHÔNG phụ thuộc vào auto-play để đảm bảo thống kê chính xác
                    // ─────────────────────────────────────────────────────────
                    await heatmap.OnLocationUpdatedAsync(loc, pois);

                    // ─────────────────────────────────────────────────────────
                    // Route Tracking: cập nhật dwell timer và kiểm tra timeout
                    // Chạy độc lập, không phụ thuộc auto-play hay heatmap
                    // ─────────────────────────────────────────────────────────
                    await routeTracking.OnLocationUpdatedAsync(loc, pois);

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
