using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Tiling;
using Mapsui.Tiling.Layers;
using Microsoft.Maui.Devices.Sensors;
using SmartTour.Shared.Models;
using SmartTourApp.Services;
using SmartTourApp.ViewModels;
using System.Linq;

namespace SmartTourApp.Pages;

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

    public MapPage(
        TrackingService tracking,
        PoiRepository repo,
        GeofencingEngine geo)
    {
        InitializeComponent();

        TourMap.Map!.Navigator.ViewportChanged += (s, e) =>
        {
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
        followUser = true;

        var loc = await Geolocation.GetLastKnownLocationAsync();
        if (loc == null)
            loc = await Geolocation.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(5)));

        if (loc != null && TourMap?.Map?.Navigator != null)
        {
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

            if (!TourMap.Map.Layers.OfType<TileLayer>().Any())
            {
                TourMap.Map.Layers.Add(OpenStreetMap.CreateTileLayer());
            }

            pois = await repo.GetPois();

            vm.LoadPois(TourMap.Map, pois);

            if (!TourMap.Map.Layers.Contains(vm.PoiLayer))
                TourMap.Map.Layers.Add(vm.PoiLayer);

            if (!TourMap.Map.Layers.Contains(vm.UserLayer))
                TourMap.Map.Layers.Add(vm.UserLayer);

            if (pois.Count > 0 && TourMap?.Map?.Navigator != null)
            {
                var first = pois[0];

                var mercator = SphericalMercator.FromLonLat(
                    first.Lng,
                    first.Lat);

                var pos = new MPoint(mercator.x, mercator.y);

                TourMap.Map.Navigator.CenterOnAndZoomTo(pos, 500);
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
        if (loc == null || TourMap?.Map == null)
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            vm.UpdateUser(TourMap.Map, loc);

            if (TourMap?.Map?.Navigator != null && followUser)
            {
                var mercator = SphericalMercator.FromLonLat(
        loc.Longitude,
        loc.Latitude);

                var pos = new MPoint(mercator.x, mercator.y);

                TourMap.Map.Navigator.CenterOn(pos);

                firstZoom = false;
            }

            if (pois.Count == 0)
                return;

            var poi = pois
                .OrderBy(p => Location.CalculateDistance(
                    loc,
                    new Location(p.Lat, p.Lng),
                    DistanceUnits.Kilometers))
                .FirstOrDefault();

            if (poi != null)
            {
                NearestPoiLabel.Text = poi.Name;

                vm.HighlightPoi(TourMap.Map, poi.Lat, poi.Lng);
            }
            else
            {
                NearestPoiLabel.Text = "Không có POI gần";
            }
        });
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

        var loc = await Geolocation.GetLastKnownLocationAsync();

        if (loc == null || TourMap?.Map?.Navigator == null)
            return;

        var mercator = SphericalMercator.FromLonLat(
            loc.Longitude,
            loc.Latitude);

        var pos = new MPoint(mercator.x, mercator.y);

        TourMap.Map.Navigator.CenterOnAndZoomTo(pos, 0.5);
    }
}