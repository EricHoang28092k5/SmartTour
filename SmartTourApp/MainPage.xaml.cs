using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.UI.Maui;
using SmartTourApp.Services;
// Dùng alias để chỉ định rõ Poi nào là "xịn"
using SharedPoi = SmartTour.Shared.Models.Poi;

namespace SmartTourApp;

public partial class MainPage : ContentPage
{
    private readonly LocationService locationService;
    private readonly GeofencingEngine geofencing;
    private readonly NarrationEngine narration;
    private readonly PoiRepository repo;

    // Sử dụng SharedPoi để đồng bộ với Repository và Geofencing
    private List<SharedPoi> pois = new();
    private MemoryLayer? userLayer;

    public MainPage(
        LocationService locationService,
        GeofencingEngine geofencing,
        NarrationEngine narration,
        PoiRepository repo)
    {
        InitializeComponent();

        this.locationService = locationService;
        this.geofencing = geofencing;
        this.narration = narration;
        this.repo = repo;

        // Lấy dữ liệu POI từ Repository
        pois = repo.GetPois() ?? new List<SharedPoi>();

        InitMap();
        DrawPoiMarkers();
        StartTracking();
    }

    private void InitMap()
    {
    #if !DEBUG
        Mapsui.Logging.Logger.LogDelegate = null;
    #endif

        TourMap.Map = new Mapsui.Map();
        TourMap.Map.Widgets.Clear();

        TourMap.Map.Layers.Add(OpenStreetMap.CreateTileLayer());
    }

    private void DrawPoiMarkers()
    {
        var features = new List<PointFeature>();

        foreach (var poi in pois)
        {
            var spherical = SphericalMercator.FromLonLat(poi.Lng, poi.Lat);
            var point = new MPoint(spherical.x, spherical.y);
            var feature = new PointFeature(point);

            feature.Styles.Add(new SymbolStyle
            {
                SymbolScale = 0.7,
                Fill = new Mapsui.Styles.Brush(Mapsui.Styles.Color.Blue)
            });

            features.Add(feature);
        }

        var layer = new MemoryLayer { Features = features, Name = "POI Layer" };
        TourMap.Map.Layers.Add(layer);
    }

    private async void StartTracking()
    {
        while (true)
        {
            try
            {
                var location = await locationService.GetLocation();
                if (location != null)
                {
                    UpdateUserMarker(location);
                    var poi = geofencing.FindNearest(location, pois);
                    HighlightPoi(poi);
                    await narration.Play(poi, location);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Tracking error: {ex.Message}");
            }

            await Task.Delay(5000); // Check mỗi 5 giây
        }
    }

    private void UpdateUserMarker(Location location)
    {
        var spherical = SphericalMercator.FromLonLat(location.Longitude, location.Latitude);
        var point = new MPoint(spherical.x, spherical.y);
        var feature = new PointFeature(point);

        feature.Styles.Add(new SymbolStyle
        {
            SymbolScale = 1,
            Fill = new Mapsui.Styles.Brush(Mapsui.Styles.Color.Red)
        });

        if (userLayer != null) TourMap.Map.Layers.Remove(userLayer);

        userLayer = new MemoryLayer { Name = "User Location", Features = new[] { feature } };
        TourMap.Map.Layers.Add(userLayer);

        TourMap.Map.Navigator.CenterOn(point);
        TourMap.Refresh();
    }

    private void HighlightPoi(SharedPoi? nearest)
    {
        // Logic đổi màu marker khi đến gần POI
        if (nearest == null) return;
        // Bạn có thể tối ưu thêm phần này để không phải vẽ lại toàn bộ layer mỗi lần check
        TourMap.Refresh();
    }
}