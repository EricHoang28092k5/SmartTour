using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Features;
using Microsoft.Maui.Devices.Sensors;
using SmartTour.Shared.Models;

using Map = Mapsui.Map;
using Color = Mapsui.Styles.Color;
using Brush = Mapsui.Styles.Brush;

namespace SmartTourApp.ViewModels;

public class MapViewModel
{
    public MemoryLayer UserLayer = new();
    public MemoryLayer PoiLayer = new();

    private PointFeature? userFeature;
    private readonly List<PointFeature> poiFeatures = new();

    private Location? lastLocation;

    // =============================
    // USER LOCATION
    // =============================
    public void UpdateUser(Map map, Location loc)
    {
        if (lastLocation != null)
        {
            var distance = Location.CalculateDistance(
                lastLocation,
                loc,
                DistanceUnits.Kilometers);

            if (distance < 0.005)
                return;
        }

        lastLocation = loc;

        var spherical = SphericalMercator.FromLonLat(
            loc.Longitude,
            loc.Latitude);

        if (userFeature == null)
        {
            userFeature = new PointFeature(
                new MPoint(spherical.x, spherical.y));

            userFeature.Styles.Add(new SymbolStyle
            {
                SymbolScale = 1.2,
                Fill = new Brush(Color.Blue)
            });

            UserLayer.Features = new[] { userFeature };
        }
        else
        {
            userFeature.Point.X = spherical.x;
            userFeature.Point.Y = spherical.y;
        }

        UserLayer.DataHasChanged();
    }

    // =============================
    // LOAD POI
    // =============================
    public void LoadPois(Map map, List<Poi> pois)
    {
        poiFeatures.Clear();

        // Tắt style mặc định của layer
        PoiLayer.Style = null;

        var iconStyle = new ImageStyle
        {
            Image = "embedded://SmartTourApp.Resources.Images.mappin.png",
            SymbolScale = 0.7,
            Offset = new Offset(0, 20)
        };

        foreach (var poi in pois)
        {
            var spherical = SphericalMercator.FromLonLat(
                poi.Lng,
                poi.Lat);

            var feature = new PointFeature(
                new MPoint(spherical.x, spherical.y));

            // chỉ dùng ImageStyle
            feature.Styles.Clear();
            feature.Styles.Add(iconStyle);

            poiFeatures.Add(feature);
        }

        PoiLayer.Features = poiFeatures;
        PoiLayer.DataHasChanged();
    }

    // =============================
    // HIGHLIGHT POI
    // =============================
    public void HighlightPoi(double lat, double lng)
    {
        var spherical = SphericalMercator.FromLonLat(lng, lat);

        var highlight = new PointFeature(
            new MPoint(spherical.x, spherical.y));

        highlight.Styles.Add(new SymbolStyle
        {
            SymbolScale = 1.4,
            Fill = new Brush(Color.Green)
        });

        poiFeatures.Add(highlight);

        PoiLayer.DataHasChanged();
    }
}