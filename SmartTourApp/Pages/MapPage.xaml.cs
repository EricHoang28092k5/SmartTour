using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Tiling;
using Mapsui.Tiling.Layers;
using Microsoft.Maui.Devices.Sensors;
using SmartTour.Shared.Models;
using SmartTourApp.Services;
using SmartTourApp.ViewModels;
using System.Collections.Concurrent;
using System.Linq;

namespace SmartTourApp.Pages;

public partial class MapPage : ContentPage
{
    private readonly TrackingService tracking;
    private readonly PoiRepository repo;
    private readonly GeofencingEngine geo;

    private readonly MapViewModel vm = new();

    private List<Poi> pois = new();

    private bool mapInitialized = false;
    private bool trackingStarted = false;

    // dùng để zoom lần đầu
    private bool firstZoom = true;

    public MapPage(
    TrackingService tracking,
    PoiRepository repo,
    GeofencingEngine geo)
    {
        InitializeComponent();

        this.tracking = tracking;
        this.repo = repo;
        this.geo = geo;

        // Khởi tạo map sau khi MapControl load xong
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

        // Zoom tới vị trí user nếu có GPS
        var loc = await Geolocation.GetLastKnownLocationAsync();

        if (loc != null && TourMap?.Map?.Navigator != null)
        {
            var mercator = SphericalMercator.FromLonLat(
                loc.Longitude,
                loc.Latitude);

            var pos = new MPoint(mercator.x, mercator.y);

            TourMap.Map.Navigator.CenterOnAndZoomTo(pos, 100);
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

                Mapsui.Logging.Logger.LogDelegate = null;

                TourMap.Map = map;

                // 🔴 XÓA TOÀN BỘ DEBUG WIDGET
                TourMap.Map.Widgets.Clear();
            }

            // tránh add layer nhiều lần
            if (!TourMap.Map.Layers.OfType<TileLayer>().Any())
            {
                TourMap.Map.Layers.Add(OpenStreetMap.CreateTileLayer());
            }

            // Load POI
            pois = await repo.GetPois();
            System.Diagnostics.Debug.WriteLine($"POI COUNT = {pois.Count}");

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

            if (TourMap?.Map != null)
            {
                TourMap.Refresh();
            }

            MainThread.BeginInvokeOnMainThread(RemoveLoggingWidget);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(
                "Map Error",
                ex.Message,
                "OK");
        }
    }

    private void UpdateLocation(Location loc)
    {
        if (loc == null || TourMap?.Map == null)
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                vm.UpdateUser(TourMap.Map, loc);

                // zoom lần đầu theo GPS
                if (firstZoom && TourMap?.Map?.Navigator != null)
                {
                    var mercator = SphericalMercator.FromLonLat(
                        loc.Longitude,
                        loc.Latitude);

                    var pos = new MPoint(mercator.x, mercator.y);

                    TourMap.Map.Navigator.CenterOnAndZoomTo(pos, 100);

                    firstZoom = false;
                }

                if (pois.Count == 0)
                    return;

                var poi = geo.FindBestPoi(loc, pois);

                if (poi != null)
                {
                    NearestPoiLabel.Text = poi.Name;
                }
                else
                {
                    NearestPoiLabel.Text = "Không có POI gần";
                }

                if (TourMap?.Map != null)
                    TourMap.Refresh();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Map update error: {ex.Message}");
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
}