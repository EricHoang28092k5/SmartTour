using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Tiling;
using Mapsui.Tiling.Layers;
using Microsoft.Maui.Devices.Sensors;
using SmartTour.Shared.Models;
using SmartTourApp.Data;
using SmartTourApp.Services;
using SmartTourApp.ViewModels;
using System.Linq;

namespace SmartTourApp.Pages;

[QueryProperty(nameof(TargetPoiId), "targetPoi")]
public partial class MapPage : ContentPage
{
    private bool followUser = true;
    private readonly TrackingService tracking;
    private readonly PoiRepository repo;
    private readonly GeofencingEngine geo;

    private readonly MapViewModel vm = new();

    private List<Poi> pois = new();

    private bool mapInitialized = false;
    private bool trackingStarted = false;
    private bool firstZoom = true;

    private DateTime lastCenterTime = DateTime.MinValue;

    public string? TargetPoiId { get; set; }
    private bool openedFromRoute = false;

    private Location? currentLocation;
    private Poi? currentNearest;

    public MapPage(
        TrackingService tracking,
        PoiRepository repo,
        GeofencingEngine geo)
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

        var loc = await Geolocation.GetLastKnownLocationAsync();
        if (loc == null)
            loc = await Geolocation.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(5)));

        if (TourMap?.Map?.Navigator == null || loc == null)
            return;

        // 🔥 CASE: đi từ Home → focus POI
        if (!string.IsNullOrEmpty(TargetPoiId))
        {
            if (pois.Count == 0)
            {
                await Task.Delay(300); // chờ load
                return;
            }
            var poi = pois.FirstOrDefault(p => p.Id.ToString() == TargetPoiId);

            if (poi != null)
            {
                openedFromRoute = true;
                followUser = false;

                var mercator = SphericalMercator.FromLonLat(
                    poi.Lng,
                    poi.Lat);

                var pos = new MPoint(mercator.x, mercator.y);

                TourMap.Map.Navigator.CenterOnAndZoomTo(pos, 0.5, 600, Mapsui.Animations.Easing.CubicOut);

                vm.HighlightPoi(TourMap.Map, poi.Lat, poi.Lng);
            }

            TargetPoiId = null; // 🔥 reset
        }
        else
        {
            followUser = true;
            openedFromRoute = false;

            var mercator = SphericalMercator.FromLonLat(
                loc.Longitude,
                loc.Latitude);

            var pos = new MPoint(mercator.x, mercator.y);

            TourMap.Map.Navigator.CenterOnAndZoomTo(pos, 0.5, 600, Mapsui.Animations.Easing.CubicOut);

            vm.UpdateUser(TourMap.Map, loc, false);
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        tracking.OnLocationChanged -= UpdateLocation;
    }
    private bool layersAdded = false;

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

            if (!TourMap.Map.Layers.OfType<TileLayer>().Any())
            {
                TourMap.Map.Layers.Add(OpenStreetMap.CreateTileLayer());
            }

            pois = await repo.GetPois();
            if (!string.IsNullOrEmpty(TargetPoiId))
            {
                HandleRouteAfterLoad();
            }

            vm.LoadPois(TourMap.Map, pois);

            if (!layersAdded)
            {
                TourMap.Map.Layers.Add(vm.PoiLayer);
                TourMap.Map.Layers.Add(vm.UserLayer);
                TourMap.Map.Layers.Add(vm.RouteLayer);
                layersAdded = true;
            }

            TourMap?.Refresh();

            MainThread.BeginInvokeOnMainThread(RemoveLoggingWidget);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Map Error", ex.Message, "OK");
        }
    }

    private void UpdateLocation(Location loc)
    {
        if (currentLocation != null)
        {
            var moved = Location.CalculateDistance(
                currentLocation,
                loc,
                DistanceUnits.Kilometers);

            if (moved < 0.01) // 10m
                return;
        }

        if (loc == null || TourMap?.Map == null)
            return;

        _ = Task.Run(() =>
        {
            var nearbyPois = pois.Where(p =>
    Math.Abs(p.Lat - loc.Latitude) < 0.02 &&
    Math.Abs(p.Lng - loc.Longitude) < 0.02);

            Poi? nearest = null;
            double minDist = double.MaxValue;

            foreach (var p in nearbyPois)
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

            return nearest;
        })
.ContinueWith(t =>
{
    if (t.IsFaulted)
        return;

    var nearest = t.Result;

    MainThread.BeginInvokeOnMainThread(() =>
    {
        vm.UpdateUser(TourMap.Map, loc);
        if (TourMap?.Map?.Navigator != null && followUser && !openedFromRoute)
        {
            var mercator = SphericalMercator.FromLonLat(
                loc.Longitude,
                loc.Latitude);

            var pos = new MPoint(mercator.x, mercator.y);

            if (firstZoom)
            {
                TourMap.Map.Navigator.CenterOnAndZoomTo(pos, 0.5);
                firstZoom = false;
            }
            else
            {
                if ((DateTime.Now - lastCenterTime).TotalMilliseconds > 500)
                {
                    TourMap.Map.Navigator.CenterOn(pos);
                    lastCenterTime = DateTime.Now;
                }
            }
        }

        if (currentNearest?.Id != nearest?.Id)
        {
            vm.ClearRoute();
            if (nearest != null)
            {
                NearestPoiLabel.Text = nearest.Name;
                PoiCard.IsVisible = true;

                vm.HighlightPoi(TourMap.Map, nearest.Lat, nearest.Lng);
            }
            else
            {
                PoiCard.IsVisible = false;
            }
        }

        currentLocation = loc;
        currentNearest = nearest;
    });
}, TaskScheduler.Default);
    }

    private void RemoveLoggingWidget()
    {
        if (TourMap.Map == null)
            return;

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
    {
        TourMap?.Map?.Navigator?.ZoomIn();
    }

    private void ZoomOut_Clicked(object sender, EventArgs e)
    {
        TourMap?.Map?.Navigator?.ZoomOut();
    }

    private async void LocateUser_Clicked(object sender, EventArgs e)
    {
        followUser = true;
        openedFromRoute = false;

        var loc = await Geolocation.GetLastKnownLocationAsync();

        if (loc == null || TourMap?.Map?.Navigator == null)
            return;

        var mercator = SphericalMercator.FromLonLat(
            loc.Longitude,
            loc.Latitude);

        var pos = new MPoint(mercator.x, mercator.y);

        TourMap.Map.Navigator.CenterOnAndZoomTo(pos, 0.5);
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
        vm.HighlightPoi(TourMap.Map, poi.Lat, poi.Lng);

        TargetPoiId = null;
    }

    private async void Route_Clicked(object sender, EventArgs e)
    {
        if (currentLocation == null || currentNearest == null)
            return;

        await vm.DrawRoute(TourMap.Map, currentLocation, currentNearest);
    }
}