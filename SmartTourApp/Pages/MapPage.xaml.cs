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
[QueryProperty(nameof(TourIdParam), "tourId")]
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

    // Offline download state
    private CancellationTokenSource? _downloadCts;
    private double _progressBarMaxWidth = 0;

    // ══════════════════════════════════════════════════════════════════
    // TOUR MODE STATE
    // ══════════════════════════════════════════════════════════════════
    public string? TourIdParam { get; set; }
    private bool _isTourMode = false;
    private int _tourCurrentCardIndex = 0;

    /// <summary>YC7: Track xem có card mở khi rời page không.</summary>
    private bool _hadOpenCardWhenLeaving = false;

    // ── YC4 Tour: Track radius arrival để advance card ──
    /// <summary>
    /// Lưu POI index đang hiển thị card (0-based).
    /// Chỉ advance sang index+1 khi user thực sự vào radius của POI [index].
    /// </summary>
    private int _displayedTourPoiIndex = 0;

    /// <summary>
    /// Set các tourPoiId mà user đã "chạm vào" radius (để không re-trigger).
    /// </summary>
    private readonly HashSet<int> _tourRadiusReached = new();

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
            if (!openedFromRoute && !_isTourMode) followUser = false;
        };

        this.tracking = tracking;
        this.repo = repo;
        this.geo = geo;
        this.api = api;
        this.offlineMapService = offlineMapService;

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
        tracking.OnLocationChanged -= UpdateLocation;
        tracking.OnLocationChanged += UpdateLocation;

        if (!trackingStarted)
        {
            await tracking.Start();
            trackingStarted = true;
        }

        // Heatmap không được hiển thị trên map (handled by CMS)
        heatmapLoaded = true;

        var loc = await Geolocation.GetLastKnownLocationAsync();
        if (loc == null)
            loc = await Geolocation.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(5)));

        if (TourMap?.Map?.Navigator == null) return;

        // ── Kích hoạt tour mới ──
        if (!string.IsNullOrEmpty(TourIdParam))
        {
            await ActivateTourModeAsync(TourIdParam);
            TourIdParam = null;
            return;
        }

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

        // ── Tour mode đang duy trì ──
        if (TourSession.IsActive && _isTourMode)
        {
            if (!_hadOpenCardWhenLeaving)
            {
                if (pois.Count == 0) await Task.Delay(300);
                CenterOnTour(TourSession.ActiveTour!);
                await Task.Delay(400);
                ShowTourPoiCard(_displayedTourPoiIndex);
            }
            else
            {
                ShowTourBanner(TourSession.ActiveTour!);
                if (selectedPoi != null && !cardManuallyClosed)
                    ShowCardForPoi(selectedPoi);
            }
            return;
        }

        // ── Normal mode ──
        if (selectedPoi != null && !cardManuallyClosed)
            ShowCardForPoi(selectedPoi);
        else if (!cardManuallyClosed && currentNearest != null)
            ShowCardForPoi(currentNearest);
        else if (cardManuallyClosed)
            PoiCard.IsVisible = false;

        // Center về user location
        if (loc != null)
        {
            followUser = true;
            openedFromRoute = false;
            var mercator = SphericalMercator.FromLonLat(loc.Longitude, loc.Latitude);
            TourMap.Map.Navigator.CenterOnAndZoomTo(
                new MPoint(mercator.x, mercator.y), 0.5, 600, Mapsui.Animations.Easing.CubicOut);
            vm.UpdateUser(TourMap.Map, loc, false);
        }

        UpdateConnectivityUI(OfflineMapService.IsConnected());
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        tracking.OnLocationChanged -= UpdateLocation;
        _hadOpenCardWhenLeaving = PoiCard.IsVisible && !cardManuallyClosed;
    }

    // ══════════════════════════════════════════════════════════════════
    // TOUR MODE — ACTIVATION / CENTER / EXIT
    // ══════════════════════════════════════════════════════════════════
    private async Task ActivateTourModeAsync(string tourIdStr)
    {
        if (!TourSession.IsActive) return;
        var tour = TourSession.ActiveTour!;

        if (pois.Count == 0)
        {
            pois = await repo.GetPois();
            if (mapInitialized) vm.LoadPois(TourMap.Map!, pois);
        }

        _isTourMode = true;
        _tourCurrentCardIndex = 0;
        _displayedTourPoiIndex = 0;
        _tourRadiusReached.Clear();
        followUser = false;
        openedFromRoute = false;
        selectedPoi = null;
        cardManuallyClosed = false;
        _hadOpenCardWhenLeaving = false;

        vm.LoadTourOverlay(TourMap.Map!, tour.Pois);
        ZoomToFitTourPois(tour.Pois);
        ShowTourBanner(tour);

        await Task.Delay(600);
        ShowTourPoiCard(0);
    }

    private void CenterOnTour(TourViewModel tour)
    {
        if (TourMap?.Map?.Navigator == null || tour.Pois.Count == 0) return;
        ZoomToFitTourPois(tour.Pois);
        ShowTourBanner(tour);
    }

    private void ZoomToFitTourPois(List<TourPoiDto> tourPois)
    {
        if (tourPois.Count == 0 || TourMap?.Map?.Navigator == null) return;

        double minLat = tourPois.Min(p => p.Lat), maxLat = tourPois.Max(p => p.Lat);
        double minLng = tourPois.Min(p => p.Lng), maxLng = tourPois.Max(p => p.Lng);

        double latPad = Math.Max((maxLat - minLat) * 0.15, 0.002);
        double lngPad = Math.Max((maxLng - minLng) * 0.15, 0.002);

        var sw = SphericalMercator.FromLonLat(minLng - lngPad, minLat - latPad);
        var ne = SphericalMercator.FromLonLat(maxLng + lngPad, maxLat + latPad);

        TourMap.Map.Navigator.ZoomToBox(new MRect(sw.x, sw.y, ne.x, ne.y), MBoxFit.Fit, 700);
    }

    private async void ShowTourBanner(TourViewModel tour)
    {
        TourBannerLabel.Text = $"🗺 {tour.Name}";
        TourBannerSubLabel.Text = $"{tour.Pois.Count} điểm • Đường đỏ = lộ trình";
        TourModeBanner.IsVisible = true;
        OfflineBanner.IsVisible = false;
        await TourModeBanner.TranslateTo(0, 0, 350, Easing.CubicOut);
    }

    private async void HideTourBanner()
    {
        await TourModeBanner.TranslateTo(0, -100, 280, Easing.CubicIn);
        TourModeBanner.IsVisible = false;
    }

    /// <summary>
    /// Hiển thị card cho POI tại index trong tour.
    /// Luôn hiển thị không điều kiện — việc ADVANCE card mới có điều kiện radius.
    /// </summary>
    private void ShowTourPoiCard(int index)
    {
        if (!TourSession.IsActive || TourSession.ActiveTour == null) return;
        var tourPois = TourSession.ActiveTour.Pois;
        if (index < 0 || index >= tourPois.Count) return;

        _tourCurrentCardIndex = index;
        _displayedTourPoiIndex = index;
        var dto = tourPois[index];

        var poi = pois.FirstOrDefault(p => p.Id == dto.PoiId) ?? new Poi
        {
            Id = dto.PoiId,
            Name = dto.Name,
            Lat = dto.Lat,
            Lng = dto.Lng
        };

        selectedPoi = poi;
        cardManuallyClosed = false;

        TourStepIndicator.IsVisible = true;
        TourStepNumber.Text = (index + 1).ToString();
        TourStepLabel.Text = $"Điểm {index + 1} / {tourPois.Count} trong tour";

        ShowCardForPoi(poi);
        vm.SelectPoiIcon(dto.PoiId);
    }

    /// <summary>
    /// YC4: Kiểm tra xem user có vào radius của POI đang hiển thị không.
    /// Nếu có → advance card sang POI tiếp theo trong tour.
    /// Gọi từ UpdateLocation mỗi khi vị trí thay đổi.
    /// </summary>
    private void CheckTourRadiusAdvance(Location userLoc)
    {
        if (!_isTourMode || !TourSession.IsActive || TourSession.ActiveTour == null)
            return;

        var tourPois = TourSession.ActiveTour.Pois;
        if (_displayedTourPoiIndex >= tourPois.Count) return;

        var currentDto = tourPois[_displayedTourPoiIndex];

        // Đã từng đến POI này rồi → không check lại
        if (_tourRadiusReached.Contains(currentDto.PoiId)) return;

        // Tìm POI đầy đủ trong danh sách pois
        var currentPoi = pois.FirstOrDefault(p => p.Id == currentDto.PoiId);
        if (currentPoi == null)
        {
            // Fallback: tạo Poi tạm từ dto với radius mặc định
            currentPoi = new Poi
            {
                Id = currentDto.PoiId,
                Name = currentDto.Name,
                Lat = currentDto.Lat,
                Lng = currentDto.Lng,
                Radius = 50 // mặc định 50m nếu không có data
            };
        }

        // Tính khoảng cách
        var distanceMeters = Location.CalculateDistance(
            userLoc,
            new Location(currentPoi.Lat, currentPoi.Lng),
            DistanceUnits.Kilometers) * 1000.0;

        var radiusThreshold = Math.Max(currentPoi.Radius, 30); // tối thiểu 30m

        if (distanceMeters <= radiusThreshold)
        {
            // User đã vào radius của POI đang hiển thị
            _tourRadiusReached.Add(currentDto.PoiId);

            System.Diagnostics.Debug.WriteLine(
                $"[TourAdvance] ✅ Reached POI #{_displayedTourPoiIndex + 1} " +
                $"({currentPoi.Name}) dist={distanceMeters:F0}m");

            // Advance sang POI tiếp theo (nếu còn)
            int nextIndex = _displayedTourPoiIndex + 1;
            if (nextIndex < tourPois.Count)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ShowTourPoiCard(nextIndex);

                    // Center map về POI tiếp theo
                    var nextDto = tourPois[nextIndex];
                    var m = SphericalMercator.FromLonLat(nextDto.Lng, nextDto.Lat);
                    TourMap.Map?.Navigator?.CenterOn(new MPoint(m.x, m.y));
                });
            }
            else
            {
                // Đã đến POI cuối cùng trong tour
                System.Diagnostics.Debug.WriteLine("[TourAdvance] 🏁 Last POI reached!");
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    TourStepLabel.Text = $"🏁 Điểm cuối — hoàn thành tour!";
                });
            }
        }
    }

    private async void OnExitTourTapped(object sender, TappedEventArgs e)
    {
        await ExitTourModeAsync();
    }

    private async Task ExitTourModeAsync()
    {
        _isTourMode = false;
        _tourCurrentCardIndex = 0;
        _displayedTourPoiIndex = 0;
        _tourRadiusReached.Clear();
        _hadOpenCardWhenLeaving = false;

        if (TourMap?.Map != null) vm.ClearTourOverlay(TourMap.Map);
        vm.ClearRoute();
        HideTourBanner();

        PoiCard.IsVisible = false;
        TourStepIndicator.IsVisible = false;
        selectedPoi = null;
        cardManuallyClosed = false;

        TourSession.EndTour();
        followUser = true;
        openedFromRoute = false;

        var loc = await Geolocation.GetLastKnownLocationAsync();
        if (loc != null && TourMap?.Map?.Navigator != null)
        {
            var m = SphericalMercator.FromLonLat(loc.Longitude, loc.Latitude);
            TourMap.Map.Navigator.CenterOnAndZoomTo(
                new MPoint(m.x, m.y), 0.5, 500, Mapsui.Animations.Easing.CubicOut);
        }

        UpdateConnectivityUI(OfflineMapService.IsConnected());
    }

    // ══════════════════════════════════════════════════════════════════
    // MAP INIT
    // ══════════════════════════════════════════════════════════════════
    private async Task InitMap()
    {
        try
        {
            // Không load heatmap từ DB vào map — handled by CMS
            // var db = Application.Current?.Handler?.MauiContext?.Services.GetService<Database>();
            // if (db != null) vm.LoadHeatMap(TourMap.Map, db.GetLocations()); // REMOVED

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

            // Thêm NearestHighlightLayer vào map (dưới PoiLayer)
            // Layer này sẽ được populate khi UpdateLocation detect nearest POI
            // (không add ngay vì vm.HighlightPoi sẽ tự add khi có dữ liệu)

            TourMap?.Refresh();
            MainThread.BeginInvokeOnMainThread(RemoveLoggingWidget);

            heatmapLoaded = true;
            // Không gọi LoadPoiHeatmapAsync — heatmap chỉ hiển thị trên CMS
            _ = Task.Run(async () => { await Task.Delay(3000); await AutoCacheCurrentAreaAsync(); });
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
                if (_isTourMode && TourSession.IsActive)
                    HandleTourPoiTap(tapped);
                else
                {
                    SelectPoi(tapped);
                    cardManuallyClosed = false;
                    vm.ClearRoute();
                    // Clear nearest highlight khi user tap chọn POI cụ thể
                    vm.ClearNearestHighlight();
                }
            });
        }
    }

    private void HandleTourPoiTap(Poi tapped)
    {
        if (TourSession.ActiveTour == null) return;
        var tourPois = TourSession.ActiveTour.Pois;
        int tourIndex = tourPois.FindIndex(p => p.PoiId == tapped.Id);

        if (tourIndex >= 0)
        {
            _tourCurrentCardIndex = tourIndex;
            selectedPoi = tapped;
            cardManuallyClosed = false;
            TourStepIndicator.IsVisible = true;
            TourStepNumber.Text = (tourIndex + 1).ToString();
            TourStepLabel.Text = $"Điểm {tourIndex + 1} / {tourPois.Count} trong tour";
            vm.SelectPoiIcon(tapped.Id);
            ShowCardForPoi(tapped);
        }
        else
        {
            selectedPoi = tapped;
            cardManuallyClosed = false;
            TourStepIndicator.IsVisible = false;
            vm.SelectPoiIcon(tapped.Id);
            ShowCardForPoi(tapped);
        }
    }

    private readonly Dictionary<int, object> _poiFeatureMapRef = new();

    private void SelectPoi(Poi poi)
    {
        selectedPoi = poi;
        currentNearest = poi;
        cardManuallyClosed = false;
        TourStepIndicator.IsVisible = false;
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
        if (_isTourMode && TourSession.IsActive)
        {
            var target = selectedPoi ?? currentNearest;
            if (target == null) return;

            if (!vm.IsPoiInTour(target.Id))
            {
                await ExecuteBlueRouteAsync(target);
                return;
            }

            await ExecuteTourRouteAsync();
        }
        else
        {
            var target = selectedPoi ?? currentNearest;
            if (currentLocation == null || target == null) return;
            await ExecuteBlueRouteAsync(target);
        }
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

    private async Task ExecuteTourRouteAsync()
    {
        if (TourSession.ActiveTour == null || currentLocation == null) return;
        var tourPois = TourSession.ActiveTour.Pois;
        if (tourPois.Count == 0) return;

        var firstPoi = tourPois[0];
        bool isFirstPoiCard = selectedPoi?.Id == firstPoi.PoiId || _tourCurrentCardIndex == 0;

        if (!isFirstPoiCard)
        {
            await DisplayAlert("Bắt đầu từ điểm 1", $"Hành trình cần bắt đầu từ điểm 1: {firstPoi.Name}", "OK");
            return;
        }

        var targetPoi = pois.FirstOrDefault(p => p.Id == firstPoi.PoiId)
            ?? new Poi { Id = firstPoi.PoiId, Name = firstPoi.Name, Lat = firstPoi.Lat, Lng = firstPoi.Lng };

        try
        {
            RouteLoadingRow.IsVisible = true;
            vm.ClearRoute();
            await vm.DrawRoute(TourMap.Map, currentLocation, targetPoi);
            await Task.Delay(400);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Lỗi", "Không thể tính đường đi: " + ex.Message, "OK");
            return;
        }
        finally
        {
            RouteLoadingRow.IsVisible = false;
        }

        // Sau khi vẽ route đến poi[0] → hiện card poi[1]
        if (tourPois.Count > 1)
        {
            await Task.Delay(800);
            ShowTourPoiCard(1);
            _tourCurrentCardIndex = 1;
            var next = tourPois[1];
            var m = SphericalMercator.FromLonLat(next.Lng, next.Lat);
            TourMap.Map?.Navigator?.CenterOn(new MPoint(m.x, m.y));
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

        if (_isTourMode)
        {
            if (selectedPoi != null)
            {
                if (!vm.IsPoiInTour(selectedPoi.Id))
                    vm.ForceDeselectPoiIcon(selectedPoi.Id);
                vm.ClearRoute();
            }
            TourStepIndicator.IsVisible = false;
        }
        else
        {
            vm.DeselectPoiIcon();
            vm.ClearRoute();
            // Khi đóng card trong normal mode → xóa highlight nearest
            vm.ClearNearestHighlight();
        }

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
    private void UpdateLocation(Location loc)
    {
        if (loc == null || TourMap?.Map == null) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            vm.UpdateUser(TourMap.Map, loc);
            if (TourMap?.Map?.Navigator != null && followUser && !openedFromRoute && !_isTourMode)
            {
                var m = SphericalMercator.FromLonLat(loc.Longitude, loc.Latitude);
                TourMap.Map.Navigator.CenterOn(new MPoint(m.x, m.y));
                firstZoom = false;
            }

            if (pois.Count == 0) return;

            // ── Tìm POI gần nhất ──
            Poi? nearest = null;
            double minDist = double.MaxValue;
            foreach (var p in pois)
            {
                var dist = Location.CalculateDistance(loc, new Location(p.Lat, p.Lng), DistanceUnits.Kilometers);
                if (dist < minDist) { minDist = dist; nearest = p; }
            }

            currentLocation = loc;

            if (!_isTourMode)
            {
                if (nearest != null && selectedPoi == null && !cardManuallyClosed)
                {
                    bool nearestChanged = currentNearest?.Id != nearest.Id;
                    currentNearest = nearest;
                    ShowCardForPoi(nearest);

                    // Highlight vòng nhấp nháy quanh POI gần nhất
                    if (nearestChanged && TourMap?.Map != null)
                        vm.SetNearestPoi(TourMap.Map, nearest);
                }
                else if (nearest == null && selectedPoi == null)
                {
                    currentNearest = null;
                    if (!cardManuallyClosed) PoiCard.IsVisible = false;
                    vm.ClearNearestHighlight();
                }
            }
            else
            {
                // Tour mode: kiểm tra xem user có vào radius POI hiện tại không
                // → tự động advance card nếu đúng điều kiện
                CheckTourRadiusAdvance(loc);
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
        var loc = await Geolocation.GetLastKnownLocationAsync();
        if (loc == null)
            loc = await Geolocation.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(3)));

        if (loc == null || TourMap?.Map?.Navigator == null) return;

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
            var loc = await Geolocation.GetLastKnownLocationAsync();
            if (loc == null) { await DisplayAlert("Lỗi", "Không lấy được vị trí GPS.", "OK"); return; }
            centerLat = loc.Latitude; centerLng = loc.Longitude;
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
        if (!isOnline && !_isTourMode)
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
        // Heatmap chỉ hiển thị trên CMS dashboard — không render trên mobile map
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
