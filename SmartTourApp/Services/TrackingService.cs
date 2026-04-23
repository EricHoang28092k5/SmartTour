using SmartTour.Shared.Models;
using SmartTourApp.Services;
using SmartTourApp.Pages; // để dùng SettingsPage.AutoPlayKey
using SmartTour.Services;

public class TrackingService
{
    private readonly LocationService location;
    private readonly GeofencingEngine geo;
    private readonly NarrationEngine narration;
    private readonly PoiRepository repo;
    private readonly LocationLogger logger;
    private readonly HeatmapService heatmap;
    private readonly ApiService api;
    private readonly RouteTrackingService routeTracking;

    private CancellationTokenSource? cts;

    private List<Poi> pois = new();
    private Location? lastLocation;
    private int interval = 3;
    private DateTime _lastPopularitySyncUtc = DateTime.MinValue;
    private const int PopularitySyncSeconds = 120;

    // ── Track trạng thái auto-play của chu kỳ trước để detect bật lại ──
    private bool _prevAutoPlayEnabled = true;

    public event Action<Location>? OnLocationChanged;

    public TrackingService(
        LocationService location,
        GeofencingEngine geo,
        NarrationEngine narration,
        PoiRepository repo,
        LocationLogger logger,
        HeatmapService heatmap,
        ApiService api,
        RouteTrackingService routeTracking)
    {
        this.location = location;
        this.geo = geo;
        this.narration = narration;
        this.repo = repo;
        this.logger = logger;
        this.heatmap = heatmap;
        this.api = api;
        this.routeTracking = routeTracking;
    }

    public async Task Start()
    {
        if (cts != null) return;

        cts = new CancellationTokenSource();
        var token = cts.Token;

        pois = await repo.GetPois();

        // Đọc trạng thái ban đầu để so sánh chu kỳ đầu tiên
        _prevAutoPlayEnabled = Preferences.Default.Get(SettingsPage.AutoPlayKey, true);

        // Reset adaptive accuracy khi start mới
        location.ResetAdaptiveState();

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

                    OnLocationChanged?.Invoke(loc);

                    // ── Adaptive interval: kết hợp khoảng cách + accuracy hiện tại ──
                    AdjustInterval(loc);

                    // ─────────────────────────────────────────────────────────
                    // Đọc trạng thái auto-play mỗi chu kỳ
                    // → cập nhật ngay lập tức khi người dùng gạt switch trong Settings
                    // ─────────────────────────────────────────────────────────
                    bool autoPlayEnabled = Preferences.Default.Get(
                        SettingsPage.AutoPlayKey, true);

                    // ── Detect auto-play VỪA được bật lại ──
                    if (autoPlayEnabled && !_prevAutoPlayEnabled)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            "🔄 Auto-play re-enabled → Reset GeofencingEngine + LocationService state");
                        geo.Reset();
                        location.ResetAdaptiveState();
                    }

                    _prevAutoPlayEnabled = autoPlayEnabled;

                    if (autoPlayEnabled)
                    {
                        await RefreshPoiPopularityIfNeededAsync();
                        var poi = geo.FindBestPoi(loc, pois);
                        if (poi != null)
                            await narration.Play(poi, loc);
                    }
                    else
                    {
                        // Chỉ update state, bỏ qua kết quả
                        geo.FindBestPoi(loc, pois);
                    }

                    // Tắt ghi nhận lịch sử di chuyển theo cấu hình rút gọn chức năng.

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

    private async Task RefreshPoiPopularityIfNeededAsync()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastPopularitySyncUtc).TotalSeconds < PopularitySyncSeconds)
            return;

        _lastPopularitySyncUtc = now;
        try
        {
            var response = await api.GetHeatmap();
            if (response?.Success != true || response.Data == null || response.Data.Count == 0)
                return;

            var popularity = response.Data
                .GroupBy(x => x.PoiId)
                .ToDictionary(g => g.Key, g => g.Max(x => x.Sum));

            geo.UpdatePoiPopularity(popularity);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Tracking] RefreshPoiPopularity failed: {ex.Message}");
        }
    }

    public void Stop()
    {
        cts?.Cancel();
        cts = null;
    }

    /// <summary>
    /// Adaptive interval dựa trên khoảng cách DI CHUYỂN + accuracy hiện tại.
    ///
    /// Logic 2 tầng:
    ///   1. Khoảng cách → tốc độ di chuyển thô
    ///   2. Accuracy hiện tại (từ LocationService) → fine-tune interval
    ///
    /// Kết quả: đứng yên + Low accuracy = 20s interval (pin tiết kiệm tối đa)
    ///          đi bộ + Medium = 8s, chạy + Best = 3s
    /// </summary>
    private void AdjustInterval(Location loc)
    {
        if (lastLocation == null)
        {
            lastLocation = loc;
            interval = 3;
            return;
        }

        var dist = Location.CalculateDistance(
            lastLocation, loc, DistanceUnits.Kilometers) * 1000.0; // in meters

        lastLocation = loc;

        // Tầng 1: khoảng cách
        int baseInterval;
        if (dist < 3)           // <3m — đứng yên
            baseInterval = 15;
        else if (dist < 15)     // 3-15m — đứng vẫy
            baseInterval = 10;
        else if (dist < 50)     // 15-50m — đi bộ
            baseInterval = 5;
        else                    // >50m — đi nhanh
            baseInterval = 3;

        // Tầng 2: điều chỉnh theo accuracy hiện tại của LocationService
        // Khi đang dùng Low accuracy (đứng yên lâu), tăng interval thêm
        var accuracy = location.CurrentAccuracy;
        interval = accuracy switch
        {
            GeolocationAccuracy.Low => Math.Max(baseInterval, 20),    // tối thiểu 20s
            GeolocationAccuracy.Medium => Math.Max(baseInterval, 8),  // tối thiểu 8s
            _ => baseInterval                                          // Best: theo dist
        };

        System.Diagnostics.Debug.WriteLine(
            $"[Tracking] dist={dist:F0}m, accuracy={accuracy}, interval={interval}s");
    }
}
