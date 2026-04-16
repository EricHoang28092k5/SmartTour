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
    private readonly LocalizationService loc;
    private readonly LanguageService lang;

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

    // ── Language: cache last applied lang để tránh re-apply không cần thiết ──
    private string _lastAppliedLang = "";

    // Offline download state
    private CancellationTokenSource? _downloadCts;
    private double _progressBarMaxWidth = 0;

    // ── Translation cache cho POI names trên card ──
    private readonly Dictionary<int, string> _translatedPoiNames = new();

    public MapPage(
        TrackingService tracking,
        PoiRepository repo,
        GeofencingEngine geo,
        ApiService api,
        OfflineMapService offlineMapService,
        LocalizationService loc,
        LanguageService lang)
    {
        InitializeComponent();

        this.tracking = tracking;
        this.repo = repo;
        this.geo = geo;
        this.api = api;
        this.offlineMapService = offlineMapService;
        this.loc = loc;
        this.lang = lang;

        // Subscribe language changes để update card text ngay lập tức
        this.lang.OnLanguageChanged += OnLanguageChanged;

        TourMap.Map!.Navigator.ViewportChanged += (s, e) =>
        {
            if (!openedFromRoute) followUser = false;
        };

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
    }

    // ══════════════════════════════════════════════════════════════════
    // LIFECYCLE
    // ══════════════════════════════════════════════════════════════════
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // ── Chỉ apply localization khi ngôn ngữ thực sự thay đổi ──
        if (_lastAppliedLang != lang.Current)
        {
            ApplyLocalization();
            _lastAppliedLang = lang.Current;

            // Refresh card nếu đang hiển thị
            if (PoiCard.IsVisible)
            {
                var target = selectedPoi ?? currentNearest;
                if (target != null) ShowCardForPoi(target);
            }
        }

        tracking.OnLocationChanged -= UpdateLocation;
        tracking.OnLocationChanged += UpdateLocation;

        if (!trackingStarted)
        {
            await tracking.Start();
            trackingStarted = true;
        }

        heatmapLoaded = true;

        // ── Lấy location: ưu tiên last known để tránh delay ──
        var loc2 = await Geolocation.GetLastKnownLocationAsync();
        if (loc2 == null)
            loc2 = await Geolocation.GetLocationAsync(
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
                cardManuallyClosed = false;
                var mercator = SphericalMercator.FromLonLat(poi.Lng, poi.Lat);
                TourMap.Map.Navigator.CenterOnAndZoomTo(
                    new MPoint(mercator.x, mercator.y), 0.5, 600, Mapsui.Animations.Easing.CubicOut);
                SelectPoi(poi);
            }
            TargetPoiId = null;
            return;
        }

        // ── Normal mode ──
        if (selectedPoi != null && !cardManuallyClosed)
            ShowCardForPoi(selectedPoi);
        else if (!cardManuallyClosed && currentNearest != null)
            ShowCardForPoi(currentNearest);
        else if (cardManuallyClosed)
            PoiCard.IsVisible = false;

        if (loc2 != null)
        {
            followUser = true;
            openedFromRoute = false;
            var mercator = SphericalMercator.FromLonLat(loc2.Longitude, loc2.Latitude);
            TourMap.Map.Navigator.CenterOnAndZoomTo(
                new MPoint(mercator.x, mercator.y), 0.5, 600, Mapsui.Animations.Easing.CubicOut);
            vm.UpdateUser(TourMap.Map, loc2, false);
        }

        UpdateConnectivityUI(OfflineMapService.IsConnected());
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        tracking.OnLocationChanged -= UpdateLocation;
    }

    // ══════════════════════════════════════════════════════════════════
    // LOCALIZATION
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Áp dụng ngôn ngữ cho tất cả text tĩnh trên MapPage.
    /// Chỉ gọi khi ngôn ngữ thực sự thay đổi — tránh lag khi navigate.
    /// </summary>
    private void ApplyLocalization()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            LblDirectionsBtn.Text = loc.DirectionsBtn;
            OfflineBannerLabel.Text = loc.OfflineMapHint;
        });
    }

    /// <summary>
    /// Gọi khi LanguageService.OnLanguageChanged fire.
    /// </summary>
    private void OnLanguageChanged(string newLang)
    {
        // Clear translation cache để fetch lại cho ngôn ngữ mới
        _translatedPoiNames.Clear();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            _lastAppliedLang = newLang;
            ApplyLocalization();

            // Refresh card ngay nếu đang hiển thị
            var target = selectedPoi ?? currentNearest;
            if (target != null && PoiCard.IsVisible)
                ShowCardForPoi(target);
        });
    }

    // ══════════════════════════════════════════════════════════════════
    // MAP INIT
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

            TourMap?.Refresh();
            MainThread.BeginInvokeOnMainThread(RemoveLoggingWidget);

            heatmapLoaded = true;

            // Áp dụng localization sau khi map ready
            ApplyLocalization();
            _lastAppliedLang = lang.Current;

            // Background: pre-fetch translated names cho visible POIs
            _ = Task.Run(async () => await PrefetchTranslatedNamesAsync(pois));

            _ = Task.Run(async () => { await Task.Delay(3000); await AutoCacheCurrentAreaAsync(); });
        }
        catch (Exception ex)
        {
            await DisplayAlert("Map Error", ex.Message, "OK");
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // TRANSLATION CACHE (để POI name trên card đồng bộ ngôn ngữ)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pre-fetch translated names cho tất cả POIs và lưu vào cache.
    /// Chạy background — không block UI.
    /// </summary>
    private async Task PrefetchTranslatedNamesAsync(List<Poi> poiList)
    {
        var currentLang = lang.Current;
        foreach (var poi in poiList)
        {
            if (_translatedPoiNames.ContainsKey(poi.Id)) continue;
            try
            {
                var scripts = await api.GetTtsScripts(poi.Id);
                if (scripts == null || scripts.Count == 0) continue;

                var selected = scripts.FirstOrDefault(x =>
                    x.LanguageCode.StartsWith(currentLang, StringComparison.OrdinalIgnoreCase))
                    ?? scripts.FirstOrDefault(x =>
                    x.LanguageCode.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                    ?? scripts.FirstOrDefault();

                if (selected != null && !string.IsNullOrWhiteSpace(selected.Title))
                    _translatedPoiNames[poi.Id] = selected.Title;
            }
            catch { /* ignore — fallback to poi.Name */ }
        }
    }

    /// <summary>
    /// Lấy tên POI đã được dịch. Nếu chưa có trong cache → dùng poi.Name.
    /// Background fetch sẽ tự động cập nhật cache.
    /// </summary>
    private string GetTranslatedPoiName(Poi poi)
    {
        if (_translatedPoiNames.TryGetValue(poi.Id, out var translated))
            return translated;

        // Fetch async nếu chưa có (không await để không block)
        _ = Task.Run(async () =>
        {
            await PrefetchTranslatedNamesAsync(new List<Poi> { poi });
            // Refresh card sau khi fetch xong
            if ((selectedPoi?.Id == poi.Id || currentNearest?.Id == poi.Id) && PoiCard.IsVisible)
            {
                MainThread.BeginInvokeOnMainThread(() => ShowCardForPoi(poi));
            }
        });

        // Fallback: trả về DisplayName nếu đã được set bởi HomePage, hoặc Name
        return string.IsNullOrWhiteSpace(poi.DisplayName) ? poi.Name : poi.DisplayName;
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
        vm.SelectPoiIcon(poi.Id);
        ShowCardForPoi(poi);
    }

    private void ShowCardForPoi(Poi poi)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // ── YC3: Hiển thị tên đã được dịch theo ngôn ngữ hiện tại ──
            NearestPoiLabel.Text = GetTranslatedPoiName(poi);

            // ── YC3: Localize nút Chỉ đường ──
            LblDirectionsBtn.Text = loc.DirectionsBtn;

            if (currentLocation != null)
            {
                var dist = Location.CalculateDistance(
                    currentLocation, new Location(poi.Lat, poi.Lng), DistanceUnits.Kilometers);
                PoiDistanceLabel.Text = dist < 1 ? $"{(int)(dist * 1000)} m" : $"{dist:F1} km";
            }
            else PoiDistanceLabel.Text = "";

            RouteLoadingRow.IsVisible = false;
            PoiCard.IsVisible = true;
        });
    }

    // ══════════════════════════════════════════════════════════════════
    // ROUTE — giữ nguyên signature
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
    // YC2: NÚT CHỈ ĐƯỜNG → MỞ GOOGLE MAPS NAVIGATION
    // Thay thế vẽ route nội bộ bằng mở Google Maps turn-by-turn
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Mở Google Maps ở chế độ navigation turn-by-turn đến POI được chọn.
    /// Ưu tiên Google Maps app, fallback sang browser nếu không cài.
    /// Clean, không lag — chỉ build URI và mở.
    /// </summary>
    private async void OnOpenGoogleMapsTapped(object sender, TappedEventArgs e)
    {
        _isActionButtonTapped = true;

        var target = selectedPoi ?? currentNearest;
        if (target == null) return;

        try
        {
            // ── Build Google Maps Navigation URI ──
            // geo:0,0?q=lat,lng(label) → mở directions
            // Dùng daddr để chỉ định điểm đến với turn-by-turn navigation
            var destinationLat = target.Lat.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var destinationLng = target.Lng.ToString(System.Globalization.CultureInfo.InvariantCulture);

            // Encode tên POI để tránh ký tự đặc biệt
            var encodedName = Uri.EscapeDataString(GetTranslatedPoiName(target));

            // Google Maps navigation URI — mode=d là driving, tdir= là turn-by-turn
            var googleMapsUri = $"google.navigation:q={destinationLat},{destinationLng}&mode=w";

            // Thử mở Google Maps app trước
            bool opened = false;

            if (await Launcher.CanOpenAsync(new Uri(googleMapsUri)))
            {
                await Launcher.OpenAsync(new Uri(googleMapsUri));
                opened = true;
            }

            if (!opened)
            {
                // Fallback: Google Maps web với directions
                // saddr để tự động lấy current location làm điểm xuất phát
                var webUri = $"https://www.google.com/maps/dir/?api=1&destination={destinationLat},{destinationLng}&travelmode=walking&destination_place_name={encodedName}";
                await Browser.OpenAsync(webUri, BrowserLaunchMode.SystemPreferred);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MapPage] Open Google Maps error: {ex.Message}");

            // Final fallback: mở browser maps
            try
            {
                var destinationLat = target.Lat.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var destinationLng = target.Lng.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var fallbackUri = $"https://maps.google.com/?q={destinationLat},{destinationLng}";
                await Browser.OpenAsync(fallbackUri, BrowserLaunchMode.SystemPreferred);
            }
            catch { /* ignore */ }
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
        cardManuallyClosed = true;

        vm.DeselectPoiIcon();
        vm.ClearRoute();
        vm.ClearNearestHighlight();

        selectedPoi = null;
        await PoiCard.TranslateTo(0, 60, 200, Easing.CubicIn);
        PoiCard.IsVisible = false;
        await PoiCard.TranslateTo(0, 0, 0);
    }

    private void Save_Clicked(object sender, TappedEventArgs e) => _isActionButtonTapped = true;
    private void Share_Clicked(object sender, TappedEventArgs e) => _isActionButtonTapped = true;

    // ══════════════════════════════════════════════════════════════════
    // LOCATION UPDATE
    // ══════════════════════════════════════════════════════════════════
    private void UpdateLocation(Location loc3)
    {
        if (loc3 == null || TourMap?.Map == null) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            vm.UpdateUser(TourMap.Map, loc3);
            if (TourMap?.Map?.Navigator != null && followUser && !openedFromRoute)
            {
                var m = SphericalMercator.FromLonLat(loc3.Longitude, loc3.Latitude);
                TourMap.Map.Navigator.CenterOn(new MPoint(m.x, m.y));
                firstZoom = false;
            }

            if (pois.Count == 0) return;

            Poi? nearest = null;
            double minDist = double.MaxValue;
            foreach (var p in pois)
            {
                var dist = Location.CalculateDistance(loc3, new Location(p.Lat, p.Lng), DistanceUnits.Kilometers);
                if (dist < minDist) { minDist = dist; nearest = p; }
            }

            currentLocation = loc3;

            if (nearest != null && selectedPoi == null && !cardManuallyClosed)
            {
                bool nearestChanged = currentNearest?.Id != nearest.Id;
                currentNearest = nearest;
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
        var loc2 = await Geolocation.GetLastKnownLocationAsync();
        if (loc2 == null)
            loc2 = await Geolocation.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(3)));

        if (loc2 == null || TourMap?.Map?.Navigator == null) return;

        var m = SphericalMercator.FromLonLat(loc2.Longitude, loc2.Latitude);
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
            await DisplayAlert("Đang tải", "Vui lòng đợi lần tải hiện tại hoàn thành.", "OK");
            return;
        }
        if (!OfflineMapService.IsConnected())
        {
            await DisplayAlert("Không có mạng", "Bạn đang offline. Kết nối mạng để tải bản đồ.", "OK");
            return;
        }

        double centerLat, centerLng;
        if (currentLocation != null)
        {
            centerLat = currentLocation.Latitude; centerLng = currentLocation.Longitude;
        }
        else
        {
            var loc2 = await Geolocation.GetLastKnownLocationAsync();
            if (loc2 == null) { await DisplayAlert("Lỗi", "Không lấy được vị trí GPS.", "OK"); return; }
            centerLat = loc2.Latitude; centerLng = loc2.Longitude;
        }

        var (newTiles, sizeMB, cached, desc) = offlineMapService.GetDownloadEstimate(centerLat, centerLng, 2.5);
        if (newTiles == 0)
        {
            await DisplayAlert("Đã có sẵn", $"Bản đồ khu vực này đã được tải!\n{cached} tiles trong cache.", "OK");
            return;
        }

        if (!await DisplayAlert("Tải bản đồ offline",
            $"{desc}\n\nBán kính: 2.5km xung quanh vị trí hiện tại\nZoom: 14 - 18", "Tải ngay", "Hủy")) return;

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
        try { await offlineMapService.DownloadAreaForTourAsync(lat, lng, radiusKm); }
        finally { await Task.Delay(1500); HideDownloadProgress(); }
    }

    private async Task AutoCacheCurrentAreaAsync()
    {
        if (!OfflineMapService.IsConnected() || currentLocation == null) return;
        try
        {
            await offlineMapService.Downloader.DownloadAreaAsync(
                currentLocation.Latitude, currentLocation.Longitude, radiusKm: 1.5, minZoom: 14, maxZoom: 16);
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
            ShowOfflineBanner("Chế độ ngoại tuyến — bản đồ chỉ hiện vùng đã tải");
        else if (isOnline)
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
    // HEATMAP — không load trên map (CMS only)
    // ══════════════════════════════════════════════════════════════════
    private async Task LoadPoiHeatmapAsync()
    {
        System.Diagnostics.Debug.WriteLine("[MapPage] Heatmap rendering skipped — CMS only");
        await Task.CompletedTask;
    }

    // ══════════════════════════════════════════════════════════════════
    // HELPERS
    // ══════════════════════════════════════════════════════════════════
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
}
