using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Tiling;
using Mapsui.Tiling.Layers;
using Microsoft.Maui.Devices.Sensors;
using SmartTour.Services;
using SmartTour.Shared.Models;
using SmartTourApp.Data;
using SmartTourApp.Services;
using SmartTourApp.Services.Offline;
using SmartTourApp.ViewModels;
using System.Linq;
using static SmartTour.Services.ApiService;

namespace SmartTourApp.Pages;

[QueryProperty(nameof(TargetPoiId), "targetPoi")]
public partial class MapPage : ContentPage
{
    private bool followUser = true;
    private readonly TrackingService tracking;
    private readonly PoiRepository repo;
    private readonly GeofencingEngine geo;
    private readonly ApiService api;
    private readonly OfflineMapService offlineMapService;
    private readonly MapViewModel vm = new();
    private List<Poi> pois = new();
    private bool mapInitialized = false;
    private bool trackingStarted = false;
    private bool firstZoom = true;
    private bool heatmapLoaded = false;
    public string? TargetPoiId { get; set; }
    private bool openedFromRoute = false;
    private Location? currentLocation;
    private Poi? currentNearest;
    private Poi? selectedPoi = null;
    private bool cardManuallyClosed = false;
    private bool _isActionButtonTapped = false;

    // 🔥 Offline download state
    private CancellationTokenSource? _downloadCts;
    private double _progressBarMaxWidth = 0;

    public MapPage(
        TrackingService tracking,
        PoiRepository repo,
        GeofencingEngine geo,
        ApiService api,
        OfflineMapService offlineMapService)
    {
        InitializeComponent();

        TourMap.Map!.Navigator.ViewportChanged += (s, e) =>
        {
            if (!openedFromRoute) followUser = false;
        };

        this.tracking = tracking;
        this.repo = repo;
        this.geo = geo;
        this.api = api;
        this.offlineMapService = offlineMapService;

        // ── Wire offline events ──
        offlineMapService.OnConnectivityChanged += OnConnectivityChanged;
        offlineMapService.OnDownloadProgress += OnDownloadProgress;

        TourMap.Loaded += async (_, _) =>
        {
            if (!mapInitialized)
            {
                await InitMap();
                mapInitialized = true;
            }
        };

        // Track progress bar container width
        DownloadProgressFill.SizeChanged += (s, e) =>
        {
            if (DownloadProgressFill.Parent is View p && p.Width > 0)
                _progressBarMaxWidth = p.Width;
        };
    }

    // ══════════════════════════════════════════════════════════════════
    // LIFECYCLE
    // ══════════════════════════════════════════════════════════════════

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        tracking.OnLocationChanged -= UpdateLocation;
        tracking.OnLocationChanged += UpdateLocation;

        if (!trackingStarted)
        {
            await tracking.Start();
            trackingStarted = true;
        }

        if (!heatmapLoaded && mapInitialized)
        {
            heatmapLoaded = true;
            _ = Task.Run(LoadPoiHeatmapAsync);
        }

        var loc = await Geolocation.GetLastKnownLocationAsync();
        if (loc == null)
            loc = await Geolocation.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(5)));

        if (TourMap?.Map?.Navigator == null || loc == null) return;

        if (!string.IsNullOrEmpty(TargetPoiId))
        {
            if (pois.Count == 0) { await Task.Delay(300); return; }
            var poi = pois.FirstOrDefault(p => p.Id.ToString() == TargetPoiId);
            if (poi != null)
            {
                openedFromRoute = true;
                followUser = false;
                cardManuallyClosed = false;
                var mercator = SphericalMercator.FromLonLat(poi.Lng, poi.Lat);
                var pos = new MPoint(mercator.x, mercator.y);
                TourMap.Map.Navigator.CenterOnAndZoomTo(pos, 0.5, 600,
                    Mapsui.Animations.Easing.CubicOut);
                SelectPoi(poi);
            }
            TargetPoiId = null;
        }
        else
        {
            if (selectedPoi != null && !cardManuallyClosed)
                ShowCardForPoi(selectedPoi);
            else if (!cardManuallyClosed && currentNearest != null)
                ShowCardForPoi(currentNearest);
            else if (cardManuallyClosed)
                PoiCard.IsVisible = false;

            followUser = true;
            openedFromRoute = false;
            var mercator = SphericalMercator.FromLonLat(loc.Longitude, loc.Latitude);
            var pos = new MPoint(mercator.x, mercator.y);
            TourMap.Map.Navigator.CenterOnAndZoomTo(pos, 0.5, 600,
                Mapsui.Animations.Easing.CubicOut);
            vm.UpdateUser(TourMap.Map, loc, false);
        }

        UpdateConnectivityUI(OfflineMapService.IsConnected());
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        tracking.OnLocationChanged -= UpdateLocation;
    }

    // ══════════════════════════════════════════════════════════════════
    // MAP INIT
    // ══════════════════════════════════════════════════════════════════

    private async Task InitMap()
    {
        try
        {
            var db = Application.Current?.Handler?.MauiContext?.Services
                .GetService<Database>();
            if (db != null)
            {
                var logs = db.GetLocations();
                vm.LoadHeatMap(TourMap.Map, logs);
            }

            if (TourMap.Map == null)
            {
                var map = new Mapsui.Map();
                map.BackColor = Mapsui.Styles.Color.White;
                Mapsui.Logging.Logger.LogDelegate = null;
                TourMap.Map = map;
                TourMap.Map.Widgets.Clear();
            }

            TourMap.Map.Info += OnMapInfo;

            if (!TourMap.Map.Layers.OfType<TileLayer>().Any())
            {
                var offlineTileLayer = OfflineOpenStreetMap.CreateOfflineTileLayer(
                    offlineMapService.TileCache);
                TourMap.Map.Layers.Add(offlineTileLayer);
            }

            pois = await repo.GetPois();
            if (!string.IsNullOrEmpty(TargetPoiId)) HandleRouteAfterLoad();

            vm.LoadPois(TourMap.Map, pois);
            if (!TourMap.Map.Layers.Contains(vm.PoiLayer))
                TourMap.Map.Layers.Add(vm.PoiLayer);
            if (!TourMap.Map.Layers.Contains(vm.UserLayer))
                TourMap.Map.Layers.Add(vm.UserLayer);

            TourMap?.Refresh();
            MainThread.BeginInvokeOnMainThread(RemoveLoggingWidget);

            heatmapLoaded = true;
            _ = Task.Run(LoadPoiHeatmapAsync);

            _ = Task.Run(async () =>
            {
                await Task.Delay(3000);
                await AutoCacheCurrentAreaAsync();
            });
        }
        catch (Exception ex)
        {
            await DisplayAlert("Map Error", ex.Message, "OK");
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // OFFLINE MAP: DOWNLOAD HANDLERS
    // ══════════════════════════════════════════════════════════════════

    private async void OnDownloadMapTapped(object sender, TappedEventArgs e)
    {
        if (offlineMapService.Downloader.IsDownloading)
        {
            await DisplayAlert("Đang tải", "Vui lòng đợi lần tải hiện tại hoàn thành.", "OK");
            return;
        }

        if (!OfflineMapService.IsConnected())
        {
            await DisplayAlert("Không có mạng",
                "Bạn đang offline. Kết nối mạng để tải bản đồ.", "OK");
            return;
        }

        double centerLat, centerLng;
        if (currentLocation != null)
        {
            centerLat = currentLocation.Latitude;
            centerLng = currentLocation.Longitude;
        }
        else
        {
            var loc = await Geolocation.GetLastKnownLocationAsync();
            if (loc == null) { await DisplayAlert("Lỗi", "Không lấy được vị trí GPS.", "OK"); return; }
            centerLat = loc.Latitude;
            centerLng = loc.Longitude;
        }

        var (newTiles, sizeMB, cached, desc) = offlineMapService.GetDownloadEstimate(
            centerLat, centerLng, 2.5);

        if (newTiles == 0)
        {
            await DisplayAlert("Đã có sẵn",
                $"Bản đồ khu vực này đã được tải!\n{cached} tiles trong cache.", "OK");
            return;
        }

        bool confirm = await DisplayAlert(
            "Tải bản đồ offline",
            $"{desc}\n\nBán kính: 2.5km xung quanh vị trí hiện tại\nZoom: 14 - 18",
            "Tải ngay", "Hủy");

        if (!confirm) return;

        await StartDownloadAsync(centerLat, centerLng);
    }

    private async void OnOfflineBannerDownloadTap(object sender, TappedEventArgs e)
    {
        if (currentLocation != null)
            await StartDownloadAsync(currentLocation.Latitude, currentLocation.Longitude);
    }

    private void OnCancelDownloadTap(object sender, TappedEventArgs e)
    {
        offlineMapService.CancelDownload();
        HideDownloadProgress();
    }

    private async Task StartDownloadAsync(double lat, double lng, double radiusKm = 2.5)
    {
        ShowDownloadProgress();
        try
        {
            await offlineMapService.DownloadAreaForTourAsync(lat, lng, radiusKm);
        }
        finally
        {
            await Task.Delay(1500);
            HideDownloadProgress();
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // OFFLINE MAP: AUTO-CACHE
    // ══════════════════════════════════════════════════════════════════

    private async Task AutoCacheCurrentAreaAsync()
    {
        if (!OfflineMapService.IsConnected()) return;
        if (currentLocation == null) return;

        try
        {
            await offlineMapService.Downloader.DownloadAreaAsync(
                currentLocation.Latitude, currentLocation.Longitude,
                radiusKm: 1.5,
                minZoom: 14, maxZoom: 16);

            System.Diagnostics.Debug.WriteLine("[MapPage] Auto-cache done");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MapPage] Auto-cache error: {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // OFFLINE MAP: CONNECTIVITY EVENT HANDLERS
    // ══════════════════════════════════════════════════════════════════

    private void OnConnectivityChanged(bool isOnline)
    {
        MainThread.BeginInvokeOnMainThread(() => UpdateConnectivityUI(isOnline));
    }

    private void UpdateConnectivityUI(bool isOnline)
    {
        ConnectivityDot.Fill = isOnline
            ? Color.FromArgb("#4CAF50")
            : Color.FromArgb("#F44336");

        if (!isOnline)
            ShowOfflineBanner("Chế độ ngoại tuyến — bản đồ chỉ hiện vùng đã tải");
        else
            HideOfflineBanner();
    }

    private void OnDownloadProgress(DownloadProgress progress)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            DownloadStatusLabel.Text = progress.Message;
            DownloadProgressLabel.Text = $"{(int)(progress.Percent * 100)}%";
            DownloadTileCountLabel.Text = progress.Total > 0
                ? $"{progress.Done}/{progress.Total}"
                : "";

            if (_progressBarMaxWidth > 0)
                DownloadProgressFill.WidthRequest = _progressBarMaxWidth * progress.Percent;

            if (progress.IsComplete)
            {
                DownloadSpinner.IsRunning = false;
                DownloadStatusLabel.TextColor = Color.FromArgb("#4CAF50");
            }
        });
    }

    // ══════════════════════════════════════════════════════════════════
    // UI HELPERS: OFFLINE BANNER & DOWNLOAD PROGRESS
    // ══════════════════════════════════════════════════════════════════

    private async void ShowOfflineBanner(string message)
    {
        OfflineBannerLabel.Text = message;
        OfflineBanner.IsVisible = true;
        DownloadProgressCard.IsVisible = false;
        await OfflineBanner.TranslateTo(0, 0, 300, Easing.CubicOut);
    }

    private async void HideOfflineBanner()
    {
        await OfflineBanner.TranslateTo(0, -80, 250, Easing.CubicIn);
        OfflineBanner.IsVisible = false;
    }

    private void ShowDownloadProgress()
    {
        OfflineBanner.IsVisible = false;
        DownloadSpinner.IsRunning = true;
        DownloadStatusLabel.TextColor = Color.FromArgb("#111111");
        DownloadProgressFill.WidthRequest = 0;
        DownloadProgressLabel.Text = "0%";
        DownloadTileCountLabel.Text = "";
        DownloadProgressCard.IsVisible = true;
    }

    private void HideDownloadProgress()
    {
        DownloadProgressCard.IsVisible = false;
        DownloadSpinner.IsRunning = false;
    }

    // ══════════════════════════════════════════════════════════════════
    // HEATMAP
    // ══════════════════════════════════════════════════════════════════

    private async Task LoadPoiHeatmapAsync()
    {
        try
        {
            var resp = await api.GetHeatmap();
            if (resp?.Data == null || resp.Data.Count == 0) return;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                //if (TourMap?.Map != null)
                //    vm.LoadPoiHeatmap(TourMap.Map, resp.Data);
            });
            System.Diagnostics.Debug.WriteLine($"🔥 HEATMAP loaded: {resp.Data.Count} POIs");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Heatmap load error: {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // MAP TAP — detect POI tap, swap icon
    // ══════════════════════════════════════════════════════════════════

    private void OnMapInfo(object? sender, MapInfoEventArgs e)
    {
        if (pois.Count == 0 || e.WorldPosition == null) return;
        var lonLat = SphericalMercator.ToLonLat(e.WorldPosition.X, e.WorldPosition.Y);
        const double tapThresholdMeters = 300;
        Poi? tapped = null;
        double minDist = double.MaxValue;

        foreach (var poi in pois)
        {
            var dist = Location.CalculateDistance(
                new Location(lonLat.lat, lonLat.lon),
                new Location(poi.Lat, poi.Lng),
                DistanceUnits.Kilometers) * 1000;
            if (dist < tapThresholdMeters && dist < minDist)
            {
                minDist = dist;
                tapped = poi;
            }
        }

        if (tapped != null)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                SelectPoi(tapped);
                cardManuallyClosed = false;
                vm.ClearRoute();
            });
        }
    }

    /// <summary>
    /// Select POI: swap icon sang mappin + show card.
    /// </summary>
    private void SelectPoi(Poi poi)
    {
        selectedPoi = poi;
        currentNearest = poi;
        cardManuallyClosed = false;

        // 🔥 Swap icon sang mappin
        vm.SelectPoiIcon(poi.Id);

        ShowCardForPoi(poi);
        vm.HighlightPoi(TourMap.Map, poi.Lat, poi.Lng);
    }

    private void ShowCardForPoi(Poi poi)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            NearestPoiLabel.Text = poi.Name;
            if (currentLocation != null)
            {
                var dist = Location.CalculateDistance(
                    currentLocation,
                    new Location(poi.Lat, poi.Lng),
                    DistanceUnits.Kilometers);
                PoiDistanceLabel.Text = dist < 1
                    ? $"{(int)(dist * 1000)} m"
                    : $"{dist:F1} km";
            }
            else PoiDistanceLabel.Text = "";
            RouteLoadingRow.IsVisible = false;
            PoiCard.IsVisible = true;
        });
    }

    private void UpdateLocation(Location loc)
    {
        if (loc == null || TourMap?.Map == null) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            vm.UpdateUser(TourMap.Map, loc);
            if (TourMap?.Map?.Navigator != null && followUser && !openedFromRoute)
            {
                var mercator = SphericalMercator.FromLonLat(loc.Longitude, loc.Latitude);
                var pos = new MPoint(mercator.x, mercator.y);
                TourMap.Map.Navigator.CenterOn(pos);
                firstZoom = false;
            }

            if (pois.Count == 0) return;

            Poi? nearest = null;
            double minDist = double.MaxValue;
            foreach (var p in pois)
            {
                var dist = Location.CalculateDistance(loc,
                    new Location(p.Lat, p.Lng), DistanceUnits.Kilometers);
                if (dist < minDist) { minDist = dist; nearest = p; }
            }

            currentLocation = loc;

            if (nearest != null && selectedPoi == null && !cardManuallyClosed)
            {
                currentNearest = nearest;
                ShowCardForPoi(nearest);
                vm.HighlightPoi(TourMap.Map, nearest.Lat, nearest.Lng);
            }
            else if (nearest == null && selectedPoi == null)
            {
                currentNearest = null;
                if (!cardManuallyClosed) PoiCard.IsVisible = false;
            }

            if (selectedPoi != null && currentLocation != null && !cardManuallyClosed)
            {
                var dist = Location.CalculateDistance(
                    currentLocation,
                    new Location(selectedPoi.Lat, selectedPoi.Lng),
                    DistanceUnits.Kilometers);
                PoiDistanceLabel.Text = dist < 1
                    ? $"{(int)(dist * 1000)} m"
                    : $"{dist:F1} km";
            }
        });
    }

    private void RemoveLoggingWidget()
    {
        if (TourMap.Map == null) return;
        var widgets = TourMap.Map.Widgets
            .Where(w => !w.GetType().Name.Contains("Logging") &&
                        !w.GetType().Name.Contains("Performance"))
            .ToList();
        TourMap.Map.Widgets.Clear();
        foreach (var w in widgets) TourMap.Map.Widgets.Enqueue(w);
    }

    // ══════════════════════════════════════════════════════════════════
    // ZOOM / LOCATE
    // ══════════════════════════════════════════════════════════════════

    private void ZoomIn_Clicked(object sender, EventArgs e)
        => TourMap?.Map?.Navigator?.ZoomIn();

    private void ZoomOut_Clicked(object sender, EventArgs e)
        => TourMap?.Map?.Navigator?.ZoomOut();

    private async void LocateUser_Clicked(object sender, EventArgs e)
    {
        followUser = true;
        openedFromRoute = false;
        var loc = await Geolocation.GetLastKnownLocationAsync();
        if (loc == null || TourMap?.Map?.Navigator == null) return;
        var mercator = SphericalMercator.FromLonLat(loc.Longitude, loc.Latitude);
        var pos = new MPoint(mercator.x, mercator.y);
        TourMap.Map.Navigator.CenterOnAndZoomTo(pos, 0.5, 400, Mapsui.Animations.Easing.CubicOut);
    }

    private void HandleRouteAfterLoad()
    {
        var poi = pois.FirstOrDefault(p => p.Id.ToString() == TargetPoiId);
        if (poi == null) return;
        openedFromRoute = true;
        followUser = false;
        var mercator = SphericalMercator.FromLonLat(poi.Lng, poi.Lat);
        var pos = new MPoint(mercator.x, mercator.y);
        TourMap.Map.Navigator.CenterOnAndZoomTo(pos, 0.5, 600);
        SelectPoi(poi);
        TargetPoiId = null;
    }

    // ══════════════════════════════════════════════════════════════════
    // POI CARD ACTIONS
    // ══════════════════════════════════════════════════════════════════

    private async void PoiCard_Tapped(object sender, TappedEventArgs e)
    {
        if (_isActionButtonTapped) { _isActionButtonTapped = false; return; }
        var target = selectedPoi ?? currentNearest;
        if (target == null) return;
        await PoiCard.ScaleToAsync(0.97, 60, Easing.CubicIn);
        await PoiCard.ScaleToAsync(1.0, 60, Easing.CubicOut);
        var detailPage = Application.Current?.Handler?.MauiContext?.Services
            .GetService<PoiDetailPage>();
        if (detailPage != null)
        {
            detailPage.SetOpenedFrom("map");
            await Shell.Current.GoToAsync(nameof(PoiDetailPage), true,
                new Dictionary<string, object> { ["poi"] = target });
        }
        else
        {
            await Shell.Current.GoToAsync(nameof(PoiDetailPage), true,
                new Dictionary<string, object> { ["poi"] = target });
        }
    }

    private async void CloseCard_Clicked(object sender, TappedEventArgs e)
    {
        _isActionButtonTapped = true;
        cardManuallyClosed = true;

        // 🔥 Reset icon poi về resicon khi đóng card
        vm.DeselectPoiIcon();

        selectedPoi = null;
        vm.ClearRoute();
        await PoiCard.TranslateTo(0, 60, 200, Easing.CubicIn);
        PoiCard.IsVisible = false;
        await PoiCard.TranslateTo(0, 0, 0);
    }

    private async void Route_Clicked_Action(object sender, TappedEventArgs e)
    {
        _isActionButtonTapped = true;
        await ExecuteRouteAsync();
    }

    private async void Route_Clicked(object sender, EventArgs e)
        => await ExecuteRouteAsync();

    private async Task ExecuteRouteAsync()
    {
        var target = selectedPoi ?? currentNearest;
        if (currentLocation == null || target == null) return;
        try
        {
            RouteLoadingRow.IsVisible = true;
            vm.ClearRoute();
            await vm.DrawRoute(TourMap.Map, currentLocation, target);
            await Task.Delay(300);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Lỗi", "Không thể tính đường đi: " + ex.Message, "OK");
        }
        finally
        {
            RouteLoadingRow.IsVisible = false;
        }
    }

    private void Save_Clicked(object sender, TappedEventArgs e)
    {
        _isActionButtonTapped = true;
        // TODO: implement save logic
    }

    private void Share_Clicked(object sender, TappedEventArgs e)
    {
        _isActionButtonTapped = true;
        // TODO: implement share logic
    }
}