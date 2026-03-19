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

    // Tái sử dụng style POI để tránh tạo nhiều lần
    private static readonly ImageStyle poiIconStyle = new ImageStyle
    {
        Image = "embedded://SmartTourApp.Resources.Images.mappin.png",
        SymbolScale = 0.6,
        Offset = new Offset(0, 20)
    };

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

        var newPoint = new MPoint(spherical.x, spherical.y);

        if (userFeature == null)
        {
            // Tạo các feature lần đầu và gắn style
            accuracyFeature = new PointFeature(newPoint)
            {
                Styles = {
                    new SymbolStyle
                    {
                        SymbolScale = 2.0,
                        Fill = new Brush(new Color(66, 133, 244, 40))
                    }
                }
            };

            pulseFeature = new PointFeature(newPoint)
            {
                Styles = {
                    new SymbolStyle
                    {
                        SymbolScale = 1.2,
                        Fill = new Brush(new Color(66, 133, 244, 80))
                    }
                }
            };

            userFeature = new PointFeature(newPoint)
            {
                Styles = {
                    new SymbolStyle
                    {
                        SymbolScale = 0.35,
                        Fill = new Brush(new Color(66, 133, 244)),
                        Outline = new Pen(Color.White, 4)
                    }
                }
            };

            UserLayer.Features = new[] { accuracyFeature, pulseFeature, userFeature };
        }
        else
        {
            // Cập nhật tọa độ bằng cách gán Point mới
            accuracyFeature!.Point.X = newPoint.X;
            accuracyFeature!.Point.Y = newPoint.Y;
            pulseFeature!.Point.X = newPoint.X;
            pulseFeature!.Point.Y = newPoint.Y;
            userFeature!.Point.X = newPoint.X;
            userFeature!.Point.Y = newPoint.Y;

            UserLayer.DataHasChanged();
        }
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

            // Cập nhật scale pulse
            pulseScale = pulseGrowing ? pulseScale + 0.04 : pulseScale - 0.04;

            if (pulseScale > 1.6) pulseGrowing = false;
            if (pulseScale < 1.0) pulseGrowing = true;

            // Chỉ cập nhật nếu thực sự thay đổi scale
            if (Math.Abs(style.SymbolScale - pulseScale) > 0.01)
            {
                style.SymbolScale = pulseScale;
                UserLayer.DataHasChanged();
            }

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

        foreach (var poi in pois)
        {
            var spherical = SphericalMercator.FromLonLat(poi.Lng, poi.Lat);

            var feature = new PointFeature(new MPoint(spherical.x, spherical.y))
            {
                Styles = { poiIconStyle } // tái sử dụng style
            };

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
        var newPoint = new MPoint(spherical.x, spherical.y);

        if (highlightFeature == null)
        {
            highlightFeature = new PointFeature(newPoint)
            {
                Styles = {
                    new SymbolStyle
                    {
                        SymbolScale = 1.3,
                        Fill = new Brush(Color.Green)
                    }
                }
            };

            poiFeatures.Add(highlightFeature); // thêm 1 lần
        }
        else
        {
            highlightFeature.Point.X = newPoint.X;
            highlightFeature.Point.Y = newPoint.Y;
        }

        PoiLayer.DataHasChanged();
    }
}