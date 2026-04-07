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
    private readonly ApiService api;   // 🔥 để load heatmap

    private readonly MapViewModel vm = new();

    private List<Poi> pois = new();

    private bool mapInitialized = false;
    private bool trackingStarted = false;
    private bool firstZoom = true;
    private bool heatmapLoaded = false;   // 🔥 chỉ load 1 lần

    public string? TargetPoiId { get; set; }
    private bool openedFromRoute = false;

    private Location? currentLocation;
    private Poi? currentNearest;

    // State that persists across page visits
    private Poi? selectedPoi = null;
    private bool cardManuallyClosed = false;

    public MapPage(
        TrackingService tracking,
        PoiRepository repo,
        GeofencingEngine geo,
        ApiService api)        // 🔥
    {
        InitializeComponent();

        TourMap.Map!.Navigator.ViewportChanged += (s, e) =>
        {
            if (!openedFromRoute)
                followUser = false;
        };

        this.tracking = tracking;
        this.repo = repo;
        this.geo = geo;
        this.api = api;

        TourMap.Loaded += async (_, _) =>
        {
            if (!mapInitialized)
            {
                await InitMap();
                mapInitialized = true;
            }
        };
    }

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

        // 🔥 Load POI heatmap bubbles lần đầu (background, non-blocking)
        if (!heatmapLoaded && mapInitialized)
        {
            heatmapLoaded = true;
            _ = Task.Run(LoadPoiHeatmapAsync);
        }

        var loc = await Geolocation.GetLastKnownLocationAsync();
        if (loc == null)
            loc = await Geolocation.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Medium,
                    TimeSpan.FromSeconds(5)));

        if (TourMap?.Map?.Navigator == null || loc == null)
            return;

        // Case: opened from route/home with targetPoi
        if (!string.IsNullOrEmpty(TargetPoiId))
        {
            if (pois.Count == 0)
            {
                await Task.Delay(300);
                return;
            }

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
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        tracking.OnLocationChanged -= UpdateLocation;
    }

    // ══════════════════════════════════════════════════════════════════
    // 🔥 Load POI visit-count heatmap từ backend rồi render bubbles
    // ══════════════════════════════════════════════════════════════════
    private async Task LoadPoiHeatmapAsync()
    {
        try
        {
            var resp = await api.GetHeatmap();
            if (resp?.Data == null || resp.Data.Count == 0) return;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (TourMap?.Map != null)
                    vm.LoadPoiHeatmap(TourMap.Map, resp.Data);
            });

            System.Diagnostics.Debug.WriteLine(
                $"🔥 HEATMAP loaded: {resp.Data.Count} POIs");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Heatmap load error: {ex.Message}");
        }
    }

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
                TourMap.Map.Layers.Add(OpenStreetMap.CreateTileLayer());

            pois = await repo.GetPois();

            if (!string.IsNullOrEmpty(TargetPoiId))
                HandleRouteAfterLoad();

            vm.LoadPois(TourMap.Map, pois);

            if (!TourMap.Map.Layers.Contains(vm.PoiLayer))
                TourMap.Map.Layers.Add(vm.PoiLayer);

            if (!TourMap.Map.Layers.Contains(vm.UserLayer))
                TourMap.Map.Layers.Add(vm.UserLayer);

            TourMap?.Refresh();
            MainThread.BeginInvokeOnMainThread(RemoveLoggingWidget);

            // 🔥 Load heatmap ngay sau khi map init xong
            heatmapLoaded = true;
            _ = Task.Run(LoadPoiHeatmapAsync);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Map Error", ex.Message, "OK");
        }
    }

    // ─────────────────────────────────────────────
    // MAP TAP → find nearest POI to tap point
    // ─────────────────────────────────────────────
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

    private void SelectPoi(Poi poi)
    {
        selectedPoi = poi;
        currentNearest = poi;
        cardManuallyClosed = false;
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
                    ? $"📍 {(int)(dist * 1000)} m"
                    : $"📍 {dist:F1} km";
            }
            else
            {
                PoiDistanceLabel.Text = "";
            }

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
                var dist = Location.CalculateDistance(
                    loc,
                    new Location(p.Lat, p.Lng),
                    DistanceUnits.Kilometers);

                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = p;
                }
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
                if (!cardManuallyClosed)
                    PoiCard.IsVisible = false;
            }

            if (selectedPoi != null && currentLocation != null && !cardManuallyClosed)
            {
                var dist = Location.CalculateDistance(
                    currentLocation,
                    new Location(selectedPoi.Lat, selectedPoi.Lng),
                    DistanceUnits.Kilometers);

                PoiDistanceLabel.Text = dist < 1
                    ? $"📍 {(int)(dist * 1000)} m"
                    : $"📍 {dist:F1} km";
            }
        });
    }

    private void RemoveLoggingWidget()
    {
        if (TourMap.Map == null) return;

        var widgets = TourMap.Map.Widgets
            .Where(w =>
                !w.GetType().Name.Contains("Logging") &&
                !w.GetType().Name.Contains("Performance"))
            .ToList();

        TourMap.Map.Widgets.Clear();

        foreach (var w in widgets)
            TourMap.Map.Widgets.Enqueue(w);
    }

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
        TourMap.Map.Navigator.CenterOnAndZoomTo(pos, 0.5, 400,
            Mapsui.Animations.Easing.CubicOut);
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

    private async void CloseCard_Clicked(object sender, EventArgs e)
    {
        cardManuallyClosed = true;
        selectedPoi = null;
        vm.ClearRoute();

        await PoiCard.TranslateTo(0, 60, 200, Easing.CubicIn);
        PoiCard.IsVisible = false;
        await PoiCard.TranslateTo(0, 0, 0);
    }

    private async void Route_Clicked(object sender, EventArgs e)
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
            await DisplayAlertAsync("Lỗi", "Không thể tính đường đi: " + ex.Message, "OK");
        }
        finally
        {
            RouteLoadingRow.IsVisible = false;
        }
    }

    private void Save_Clicked(object sender, TappedEventArgs e) { }
    private void Share_Clicked(object sender, TappedEventArgs e) { }
}
