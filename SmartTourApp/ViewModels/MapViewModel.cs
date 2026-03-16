using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Features;
using Microsoft.Maui.Devices.Sensors;
using SmartTour.Shared.Models;
using System.Linq;

using Map = Mapsui.Map;
using Color = Mapsui.Styles.Color;
using Brush = Mapsui.Styles.Brush;

namespace SmartTourApp.ViewModels;

public class MapViewModel
{
    private PointFeature? highlightFeature;

    public MemoryLayer UserLayer = new();
    public MemoryLayer PoiLayer = new();

    private PointFeature? userFeature;
    private PointFeature? pulseFeature;
    private PointFeature? accuracyFeature;

    private readonly List<PointFeature> poiFeatures = new();

    private Location? lastLocation;

    private double pulseScale = 1.0;
    private bool pulseGrowing = true;

    public MapViewModel()
    {
        StartPulseAnimation();
    }

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

            if (distance < 0.003)
                return;
        }

        lastLocation = loc;

        var spherical = SphericalMercator.FromLonLat(
            loc.Longitude,
            loc.Latitude);

        if (userFeature == null)
        {
            accuracyFeature = new PointFeature(
                new MPoint(spherical.x, spherical.y));

            accuracyFeature.Styles.Add(new SymbolStyle
            {
                SymbolScale = 2.0,
                Fill = new Brush(new Color(66, 133, 244, 40))
            });

            pulseFeature = new PointFeature(
                new MPoint(spherical.x, spherical.y));

            pulseFeature.Styles.Add(new SymbolStyle
            {
                SymbolScale = 1.2,
                Fill = new Brush(new Color(66, 133, 244, 80))
            });

            userFeature = new PointFeature(
                new MPoint(spherical.x, spherical.y));

            userFeature.Styles.Add(new SymbolStyle
            {
                SymbolScale = 0.35,
                Fill = new Brush(new Color(66, 133, 244)),
                Outline = new Pen(Color.White, 4)
            });

            UserLayer.Features = new[]
            {
                accuracyFeature,
                pulseFeature,
                userFeature
            };
        }
        else
        {
            accuracyFeature!.Point.X = spherical.x;
            accuracyFeature.Point.Y = spherical.y;

            pulseFeature!.Point.X = spherical.x;
            pulseFeature.Point.Y = spherical.y;

            userFeature!.Point.X = spherical.x;
            userFeature.Point.Y = spherical.y;
        }

        UserLayer.DataHasChanged();
    }

    // =============================
    // PULSE ANIMATION
    // =============================
    private void StartPulseAnimation()
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher == null)
            return;

        dispatcher.StartTimer(TimeSpan.FromMilliseconds(60), () =>
        {
            if (pulseFeature == null)
                return true;

            var style = pulseFeature.Styles.FirstOrDefault() as SymbolStyle;
            if (style == null)
                return true;

            if (pulseGrowing)
                pulseScale += 0.04;
            else
                pulseScale -= 0.04;

            if (pulseScale > 1.6)
                pulseGrowing = false;

            if (pulseScale < 1.0)
                pulseGrowing = true;

            style.SymbolScale = pulseScale;

            UserLayer.DataHasChanged();

            return true;
        });
    }

    // =============================
    // LOAD POI (GIỮ NGUYÊN)
    // =============================
    public void LoadPois(Map map, List<Poi> pois)
    {
        poiFeatures.Clear();

        PoiLayer.Style = null;

        var iconStyle = new ImageStyle
        {
            Image = "embedded://SmartTourApp.Resources.Images.mappin.png",
            SymbolScale = 0.6,
            Offset = new Offset(0, 20)
        };

        foreach (var poi in pois)
        {
            var spherical = SphericalMercator.FromLonLat(
                poi.Lng,
                poi.Lat);

            var feature = new PointFeature(
                new MPoint(spherical.x, spherical.y));

            feature.Styles.Clear();
            feature.Styles.Add(iconStyle);

            poiFeatures.Add(feature);
        }

        PoiLayer.Features = poiFeatures;
        PoiLayer.DataHasChanged();
    }

    // =============================
    // HIGHLIGHT POI (GIỮ NGUYÊN)
    // =============================
    public void HighlightPoi(Map map, double lat, double lng)
    {
        var spherical = SphericalMercator.FromLonLat(lng, lat);

        if (highlightFeature == null)
        {
            highlightFeature = new PointFeature(
                new MPoint(spherical.x, spherical.y));

            highlightFeature.Styles.Add(new SymbolStyle
            {
                SymbolScale = 1.3,
                Fill = new Brush(Color.Green)
            });

            poiFeatures.Add(highlightFeature);
        }
        else
        {
            highlightFeature.Point.X = spherical.x;
            highlightFeature.Point.Y = spherical.y;
        }

        PoiLayer.DataHasChanged();
    }
}