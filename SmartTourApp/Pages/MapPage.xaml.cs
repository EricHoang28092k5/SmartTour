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
    private const string OfflineCenterLatKey = "map_offline_center_lat";
    private const string OfflineCenterLngKey = "map_offline_center_lng";

    private bool followUser = true;
    private readonly TrackingService tracking;
    private readonly PoiRepository repo;
    private readonly GeofencingEngine geo;
    private readonly ApiService api;
    private readonly OfflineMapService offlineMapService;
    private readonly LocalizationService loc;
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

    // YC3: cardManuallyClosed chỉ reset khi navigate sang page khác rồi về lại
    // Không reset khi đang ở map page
    private bool cardManuallyClosed = false;
    private bool _isActionButtonTapped = false;

    // YC3: Track xem card đang show cho POI nào để biết có cần update hay không
    private int? _cardShownForPoiId = null;

    // Offline download state
    private CancellationTokenSource? _downloadCts;
    private double _progressBarMaxWidth = 0;

    public MapPage(
        TrackingService tracking,
        PoiRepository repo,
        GeofencingEngine geo,
        ApiService api,
        OfflineMapService offlineMapService,
        LocalizationService loc)
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
        this.loc = loc;

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

        DownloadProgressFill.SizeChanged += (s, e) =>
        {
            if (DownloadProgressFill.Parent is View p && p.Width > 0)
                _progressBarMaxWidth = p.Width;
        };

        ApplyLocalization();
    }

    // ══════════════════════════════════════════════════════════════════
    // LIFECYCLE
    // ══════════════════════════════════════════════════════════════════
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ApplyLocalization();
        tracking.OnLocationChanged -= UpdateLocation;
        tracking.OnLocationChanged += UpdateLocation;

        if (!trackingStarted)
        {
            await tracking.Start();
            trackingStarted = true;
        }

        heatmapLoaded = true;

        var loc = await Geolocation.GetLastKnownLocationAsync();
        if (loc == null)
            loc = await Geolocation.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(5)));

        if (TourMap?.Map?.Navigator == null) return;

        // ── YC5: Navigate đến POI cụ thể ──
        if (!string.IsNullOrEmpty(TargetPoiId))
        {
            if (pois.Count == 0) await Task.Delay(300);
            var poi = pois.FirstOrDefault(p => p.Id.ToString() == TargetPoiId);
            if (poi != null)
            {
                openedFromRoute = true;
                followUser = false;
                // YC3: Khi navigate tới POI cụ thể, reset cardManuallyClosed
                cardManuallyClosed = false;
                var mercator = SphericalMercator.FromLonLat(poi.Lng, poi.Lat);
                TourMap.Map.Navigator.CenterOnAndZoomTo(
                    new MPoint(mercator.x, mercator.y), 0.5, 600, Mapsui.Animations.Easing.CubicOut);
                SelectPoi(poi);
            }
            TargetPoiId = null;
            return;
        }

        // ── YC3: Logic hiển thị card khi quay lại map page ──
        // Nếu user đã chọn POI cụ thể (tap icon trên map) và chưa tắt card → vẫn hiện
        if (selectedPoi != null && !cardManuallyClosed)
        {
            ShowCardForPoi(selectedPoi);
            if (loc != null)
            {
                followUser = false;
                openedFromRoute = true;
                var mercator = SphericalMercator.FromLonLat(selectedPoi.Lng, selectedPoi.Lat);
                TourMap.Map.Navigator.CenterOnAndZoomTo(
                    new MPoint(mercator.x, mercator.y), 0.5, 600, Mapsui.Animations.Easing.CubicOut);
            }
        }
        else
        {
            // YC3: cardManuallyClosed = true => user đã tắt card thủ công,
            //      khi quay lại map page thì hiện card POI gần nhất (reset trạng thái tắt thủ công)
            // Hoặc chưa có card nào -> hiện card POI gần nhất
            cardManuallyClosed = false;
            selectedPoi = null;
            _cardShownForPoiId = null;

            // Center về user location và để UpdateLocation tự hiện card nearest
            if (loc != null)
            {
                followUser = true;
                openedFromRoute = false;
                var mercator = SphericalMercator.FromLonLat(loc.Longitude, loc.Latitude);
                TourMap.Map.Navigator.CenterOnAndZoomTo(
                    new MPoint(mercator.x, mercator.y), 0.5, 600, Mapsui.Animations.Easing.CubicOut);
                vm.UpdateUser(TourMap.Map, loc, false);

                // Hiện ngay card của POI gần nhất nếu có
                if (currentNearest != null)
                    ShowCardForPoi(currentNearest);
                else
                    FindAndShowNearestCard(loc);
            }
            else if (TryGetOfflineCenter(out var cachedLat, out var cachedLng))
            {
                // Khi mở app từ trạng thái offline, vẫn center vào vùng đã cache để tránh map trắng
                followUser = false;
                openedFromRoute = false;
                var mercator = SphericalMercator.FromLonLat(cachedLng, cachedLat);
                TourMap.Map.Navigator.CenterOnAndZoomTo(
                    new MPoint(mercator.x, mercator.y), 0.7, 600, Mapsui.Animations.Easing.CubicOut);
            }
        }

        UpdateConnectivityUI(OfflineMapService.IsConnected());
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        tracking.OnLocationChanged -= UpdateLocation;
        // YC3: Không reset cardManuallyClosed ở đây
        // Trạng thái sẽ được xử lý ở OnAppearing
    }

    // YC3: Tìm và hiện card POI gần nhất từ user location
    private void FindAndShowNearestCard(Location loc)
    {
        if (pois.Count == 0) return;
        Poi? nearest = null;
        double minDist = double.MaxValue;
        foreach (var p in pois)
        {
            var dist = Location.CalculateDistance(loc, new Location(p.Lat, p.Lng),
                DistanceUnits.Kilometers);
            if (dist < minDist) { minDist = dist; nearest = p; }
        }
        if (nearest != null)
        {
            currentNearest = nearest;
            ShowCardForPoi(nearest);
            if (TourMap?.Map != null)
                vm.SetNearestPoi(TourMap.Map, nearest);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // MAP INIT — YC2: Fix offline tile rendering
    // ══════════════════════════════════════════════════════════════════
    private async Task InitMap()
    {
        try
        {
            if (TourMap.Map == null)
            {
                var map = new Mapsui.Map();
                map.BackColor = Mapsui.Styles.Color.White;
                Mapsui.Logging.Logger.LogDelegate = null;
                TourMap.Map = map;
                TourMap.Map.Widgets.Clear();
            }

            TourMap.Map.Info += OnMapInfo;

            // YC2: Luôn tạo offline tile layer khi init, không check IsConnected()
            // Layer này sẽ đọc từ cache trước, chỉ fetch network nếu có mạng
            if (!TourMap.Map.Layers.OfType<TileLayer>().Any())
            {
                var offlineTileLayer = OfflineOpenStreetMap.CreateOfflineTileLayer(offlineMapService.TileCache);
                TourMap.Map.Layers.Add(offlineTileLayer);
            }

            pois = await repo.GetPois();
            if (!string.IsNullOrEmpty(TargetPoiId)) HandleRouteAfterLoad();

            vm.LoadPois(TourMap.Map, pois);
            if (!TourMap.Map.Layers.Contains(vm.PoiLayer)) TourMap.Map.Layers.Add(vm.PoiLayer);
            if (!TourMap.Map.Layers.Contains(vm.UserLayer)) TourMap.Map.Layers.Add(vm.UserLayer);

            // Offline startup fallback: nếu chưa có GPS, center map về vị trí cache hoặc POI đầu tiên
            if (currentLocation == null && TourMap?.Map?.Navigator != null)
            {
                if (TryGetOfflineCenter(out var cachedLat, out var cachedLng))
                {
                    var center = SphericalMercator.FromLonLat(cachedLng, cachedLat);
                    TourMap.Map.Navigator.CenterOnAndZoomTo(
                        new MPoint(center.x, center.y), 0.7, 500, Mapsui.Animations.Easing.CubicOut);
                }
                else if (pois.Count > 0)
                {
                    var firstPoi = pois[0];
                    var center = SphericalMercator.FromLonLat(firstPoi.Lng, firstPoi.Lat);
                    TourMap.Map.Navigator.CenterOnAndZoomTo(
                        new MPoint(center.x, center.y), 0.7, 500, Mapsui.Animations.Easing.CubicOut);
                }
            }

            TourMap?.Refresh();
            MainThread.BeginInvokeOnMainThread(RemoveLoggingWidget);

            heatmapLoaded = true;

            // YC2: Auto-cache bất kể online/offline (nếu online mới tải, offline thì skip)
            _ = Task.Run(async () => { await Task.Delay(2000); await AutoCacheCurrentAreaAsync(); });
        }
        catch (Exception ex)
        {
            await DisplayAlert("Map Error", ex.Message, "OK");
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // MAP TAP
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
            if (dist < tapThresholdMeters && dist < minDist) { minDist = dist; tapped = poi; }
        }

        if (tapped != null)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                SelectPoi(tapped);
                cardManuallyClosed = false;
                vm.ClearRoute();
                vm.ClearNearestHighlight();
            });
        }
    }

    private readonly Dictionary<int, object> _poiFeatureMapRef = new();

    private void SelectPoi(Poi poi)
    {
        selectedPoi = poi;
        currentNearest = poi;
        cardManuallyClosed = false;
        _cardShownForPoiId = poi.Id;
        vm.SelectPoiIcon(poi.Id);
        ShowCardForPoi(poi);
    }

    private void ShowCardForPoi(Poi poi)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            NearestPoiLabel.Text = poi.Name;
            if (currentLocation != null)
            {
                var dist = Location.CalculateDistance(
                    currentLocation, new Location(poi.Lat, poi.Lng), DistanceUnits.Kilometers);
                PoiDistanceLabel.Text = dist < 1 ? $"{(int)(dist * 1000)} m" : $"{dist:F1} km";
            }
            else PoiDistanceLabel.Text = "";
            RouteLoadingRow.IsVisible = false;
            PoiCard.IsVisible = true;
            _cardShownForPoiId = poi.Id;
        });
    }

    // ══════════════════════════════════════════════════════════════════
    // ROUTE
    // ══════════════════════════════════════════════════════════════════
    private async void Route_Clicked_Action(object sender, TappedEventArgs e)
    {
        _isActionButtonTapped = true;
        await ExecuteRouteAsync();
    }

    private async void Route_Clicked(object sender, EventArgs e) => await ExecuteRouteAsync();

    private async Task ExecuteRouteAsync()
    {
        var target = selectedPoi ?? currentNearest;
        if (currentLocation == null || target == null) return;
        await ExecuteBlueRouteAsync(target);
    }

    private async Task ExecuteBlueRouteAsync(Poi target)
    {
        if (currentLocation == null) return;
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
        var detailPage = Application.Current?.Handler?.MauiContext?.Services.GetService<PoiDetailPage>();
        if (detailPage != null) detailPage.SetOpenedFrom("map");
        await Shell.Current.GoToAsync(nameof(PoiDetailPage), true, new Dictionary<string, object> { ["poi"] = target });
    }

    private async void CloseCard_Clicked(object sender, TappedEventArgs e)
    {
        _isActionButtonTapped = true;
        // YC3: cardManuallyClosed = true, khi quay lại sẽ reset và hiện nearest
        cardManuallyClosed = true;

        vm.DeselectPoiIcon();
        vm.ClearRoute();
        vm.ClearNearestHighlight();

        // YC3: Reset selectedPoi khi đóng card thủ công
        selectedPoi = null;
        _cardShownForPoiId = null;

        await PoiCard.TranslateTo(0, 60, 200, Easing.CubicIn);
        PoiCard.IsVisible = false;
        await PoiCard.TranslateTo(0, 0, 0);
    }

    private void Save_Clicked(object sender, TappedEventArgs e) => _isActionButtonTapped = true;
    private void Share_Clicked(object sender, TappedEventArgs e) => _isActionButtonTapped = true;

    // Đây là handler cho nút Google Maps directions
    private async void OnOpenGoogleMapsTapped(object sender, TappedEventArgs e)
    {
        _isActionButtonTapped = true;
        var target = selectedPoi ?? currentNearest;
        if (target == null) return;
        try
        {
            var url = $"https://www.google.com/maps/dir/?api=1&destination={target.Lat},{target.Lng}&travelmode=walking";
            await Launcher.Default.OpenAsync(url);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MapPage] Google Maps error: {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // LOCATION UPDATE
    // ══════════════════════════════════════════════════════════════════
    private void UpdateLocation(Location loc)
    {
        if (loc == null || TourMap?.Map == null) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            vm.UpdateUser(TourMap.Map, loc);
            if (TourMap?.Map?.Navigator != null && followUser && !openedFromRoute)
            {
                var m = SphericalMercator.FromLonLat(loc.Longitude, loc.Latitude);
                TourMap.Map.Navigator.CenterOn(new MPoint(m.x, m.y));
                firstZoom = false;
            }

            if (pois.Count == 0) return;

            Poi? nearest = null;
            double minDist = double.MaxValue;
            foreach (var p in pois)
            {
                var dist = Location.CalculateDistance(loc, new Location(p.Lat, p.Lng), DistanceUnits.Kilometers);
                if (dist < minDist) { minDist = dist; nearest = p; }
            }

            currentLocation = loc;

            // YC3: Hiện card nearest chỉ khi:
            //   - Không có selectedPoi (user chưa tap icon cụ thể)
            //   - cardManuallyClosed = false
            if (nearest != null && selectedPoi == null && !cardManuallyClosed)
            {
                bool nearestChanged = currentNearest?.Id != nearest.Id;
                currentNearest = nearest;

                // Chỉ update UI nếu card chưa hiện hoặc nearest đã đổi
                if (!PoiCard.IsVisible || nearestChanged)
                    ShowCardForPoi(nearest);

                if (nearestChanged && TourMap?.Map != null)
                    vm.SetNearestPoi(TourMap.Map, nearest);
            }
            else if (nearest == null && selectedPoi == null)
            {
                currentNearest = null;
                if (!cardManuallyClosed) PoiCard.IsVisible = false;
                vm.ClearNearestHighlight();
            }

            // Update distance label nếu đang show card của selectedPoi
            if (selectedPoi != null && currentLocation != null && !cardManuallyClosed)
            {
                var dist = Location.CalculateDistance(
                    currentLocation, new Location(selectedPoi.Lat, selectedPoi.Lng), DistanceUnits.Kilometers);
                PoiDistanceLabel.Text = dist < 1 ? $"{(int)(dist * 1000)} m" : $"{dist:F1} km";
            }
        });
    }

    // ══════════════════════════════════════════════════════════════════
    // BUTTONS
    // ══════════════════════════════════════════════════════════════════
    private async void LocateUser_Clicked(object sender, EventArgs e)
    {
        var loc = await Geolocation.GetLastKnownLocationAsync();
        if (loc == null)
            loc = await Geolocation.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(3)));
        if (loc == null || TourMap?.Map?.Navigator == null) return;
        followUser = true;
        var m = SphericalMercator.FromLonLat(loc.Longitude, loc.Latitude);
        TourMap.Map.Navigator.CenterOnAndZoomTo(
            new MPoint(m.x, m.y), 0.5, 400, Mapsui.Animations.Easing.CubicOut);
    }

    private void ZoomIn_Clicked(object sender, EventArgs e) => TourMap?.Map?.Navigator?.ZoomIn();
    private void ZoomOut_Clicked(object sender, EventArgs e) => TourMap?.Map?.Navigator?.ZoomOut();

    // ══════════════════════════════════════════════════════════════════
    // OFFLINE MAP
    // ══════════════════════════════════════════════════════════════════
    private async void OnDownloadMapTapped(object sender, TappedEventArgs e)
    {
        if (offlineMapService.Downloader.IsDownloading)
        {
            await DisplayAlert(loc.Downloading, loc.DownloadingMap, loc.OK);
            return;
        }
        if (!OfflineMapService.IsConnected())
        {
            await DisplayAlert(loc.NoNetwork, loc.NoNetworkMsg, loc.OK);
            return;
        }

        double centerLat, centerLng;
        if (currentLocation != null)
        {
            centerLat = currentLocation.Latitude; centerLng = currentLocation.Longitude;
        }
        else
        {
            var gps = await Geolocation.GetLastKnownLocationAsync();
            if (gps == null) { await DisplayAlert(loc.NoGps, loc.NoGpsMsg, loc.OK); return; }
            centerLat = gps.Latitude; centerLng = gps.Longitude;
        }

        var (newTiles, sizeMB, cached, desc) = offlineMapService.GetDownloadEstimate(centerLat, centerLng, 2.5);
        if (newTiles == 0)
        {
            await DisplayAlert(loc.AlreadyCached, string.Format(loc.MapAlreadyDownloaded, cached), loc.OK);
            return;
        }

        if (!await DisplayAlert(loc.DownloadAreaTitle,
            $"{desc}\n\nBán kính: 2.5km xung quanh vị trí hiện tại\nZoom: 14 - 18", loc.DownloadNow, loc.Cancel)) return;

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
        // Lưu tâm khu vực tải để lần mở app offline vẫn hiển thị đúng vùng đã cache
        SaveOfflineCenter(lat, lng);
        ShowDownloadProgress();
        try { await offlineMapService.DownloadAreaForTourAsync(lat, lng, radiusKm); }
        finally { await Task.Delay(1500); HideDownloadProgress(); }
    }

    private static void SaveOfflineCenter(double lat, double lng)
    {
        try
        {
            Preferences.Default.Set(OfflineCenterLatKey, lat);
            Preferences.Default.Set(OfflineCenterLngKey, lng);
        }
        catch { }
    }

    private static bool TryGetOfflineCenter(out double lat, out double lng)
    {
        lat = 0;
        lng = 0;
        try
        {
            if (!Preferences.Default.ContainsKey(OfflineCenterLatKey) ||
                !Preferences.Default.ContainsKey(OfflineCenterLngKey))
                return false;

            lat = Preferences.Default.Get(OfflineCenterLatKey, 0d);
            lng = Preferences.Default.Get(OfflineCenterLngKey, 0d);
            return Math.Abs(lat) > 0.000001 || Math.Abs(lng) > 0.000001;
        }
        catch
        {
            return false;
        }
    }

    private async Task AutoCacheCurrentAreaAsync()
    {
        if (!OfflineMapService.IsConnected() || currentLocation == null) return;
        try
        {
            await offlineMapService.Downloader.DownloadAreaAsync(
                currentLocation.Latitude, currentLocation.Longitude,
                radiusKm: 1.5, minZoom: 14, maxZoom: 16);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MapPage] Auto-cache error: {ex.Message}");
        }
    }

    private void OnConnectivityChanged(bool isOnline) =>
        MainThread.BeginInvokeOnMainThread(() => UpdateConnectivityUI(isOnline));

    private void UpdateConnectivityUI(bool isOnline)
    {
        ConnectivityDot.Fill = isOnline ? Color.FromArgb("#4CAF50") : Color.FromArgb("#F44336");
        if (!isOnline)
            ShowOfflineBanner(loc.OfflineMapHint);
        else
            HideOfflineBanner();
    }

    private void OnDownloadProgress(DownloadProgress progress)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            DownloadStatusLabel.Text = progress.Message;
            DownloadProgressLabel.Text = $"{(int)(progress.Percent * 100)}%";
            DownloadTileCountLabel.Text = progress.Total > 0 ? $"{progress.Done}/{progress.Total}" : "";
            if (_progressBarMaxWidth > 0)
                DownloadProgressFill.WidthRequest = _progressBarMaxWidth * progress.Percent;
            if (progress.IsComplete)
            {
                DownloadSpinner.IsRunning = false;
                DownloadStatusLabel.TextColor = Color.FromArgb("#4CAF50");
            }
        });
    }

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
        DownloadStatusLabel.Text = loc.DownloadingMap;
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

    private async Task LoadPoiHeatmapAsync()
    {
        System.Diagnostics.Debug.WriteLine("[MapPage] Heatmap rendering skipped — CMS only");
        await Task.CompletedTask;
    }

    private void HandleRouteAfterLoad()
    {
        var poi = pois.FirstOrDefault(p => p.Id.ToString() == TargetPoiId);
        if (poi == null) return;
        openedFromRoute = true;
        followUser = false;
        var m = SphericalMercator.FromLonLat(poi.Lng, poi.Lat);
        TourMap.Map.Navigator.CenterOnAndZoomTo(new MPoint(m.x, m.y), 0.5, 600);
        SelectPoi(poi);
        TargetPoiId = null;
    }

    private void RemoveLoggingWidget()
    {
        if (TourMap.Map == null) return;
        var widgets = TourMap.Map.Widgets
            .Where(w => !w.GetType().Name.Contains("Logging") && !w.GetType().Name.Contains("Performance"))
            .ToList();
        TourMap.Map.Widgets.Clear();
        foreach (var w in widgets) TourMap.Map.Widgets.Enqueue(w);
    }

    private void ApplyLocalization()
    {
        OfflineBannerLabel.Text = loc.OfflineMapHint;
        OfflineBannerDownloadLabel.Text = loc.DownloadMap;
        DownloadStatusLabel.Text = loc.DownloadingMap;
        DownloadCancelLabel.Text = loc.Cancel;
        LblDirectionsBtn.Text = loc.DirectionsBtn;
        RouteLoadingLabel.Text = loc.CalculatingRoute;
    }
}
