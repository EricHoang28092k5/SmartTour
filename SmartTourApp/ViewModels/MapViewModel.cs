using Mapsui;
using Mapsui.Features;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Microsoft.Maui.Devices.Sensors;
using NtsGeometry = NetTopologySuite.Geometries;
using SmartTour.Shared.Models;
using System.Linq;
using System.Globalization;
using Brush = Mapsui.Styles.Brush;
using Color = Mapsui.Styles.Color;
using Map = Mapsui.Map;
using static SmartTour.Services.ApiService;

namespace SmartTourApp.ViewModels;

public class MapViewModel
{
    private PointFeature? highlightFeature;

    public MemoryLayer UserLayer = new();
    public MemoryLayer PoiLayer = new();
    public MemoryLayer HeatLayer = new();  // user location heat (lịch sử di chuyển)
    public MemoryLayer RouteLayer = new();
    public MemoryLayer HeatmapLayer = new();  // 🔥 POI visit-count heatmap bubbles

    private PointFeature? userFeature;
    private PointFeature? pulseFeature;
    private PointFeature? accuracyFeature;

    private readonly List<PointFeature> poiFeatures = new();

    private Location? lastLocation;

    private double pulseScale = 1.0;
    private bool pulseGrowing = true;

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
    public void UpdateUser(Map map, Location loc, bool centerMap = false)
    {
        if (lastLocation != null)
        {
            var distance = Location.CalculateDistance(
                lastLocation, loc, DistanceUnits.Kilometers);

            if (distance < 0.003) return;
        }

        lastLocation = loc;

        var spherical = SphericalMercator.FromLonLat(loc.Longitude, loc.Latitude);
        var newPoint = new MPoint(spherical.x, spherical.y);

        if (userFeature == null)
        {
            accuracyFeature = new PointFeature(newPoint)
            {
                Styles = {
                    new SymbolStyle
                    {
                        SymbolScale = 2.0,
                        Fill = new Brush(new Color(30, 136, 229, 35))
                    }
                }
            };

            pulseFeature = new PointFeature(newPoint)
            {
                Styles = {
                    new SymbolStyle
                    {
                        SymbolScale = 1.2,
                        Fill = new Brush(new Color(30, 136, 229, 70))
                    }
                }
            };

            userFeature = new PointFeature(newPoint)
            {
                Styles = {
                    new SymbolStyle
                    {
                        SymbolScale = 0.35,
                        Fill    = new Brush(new Color(30, 136, 229)),
                        Outline = new Pen(Color.White, 4)
                    }
                }
            };

            UserLayer.Features = new[] { accuracyFeature, pulseFeature, userFeature };
            centerMap = true;
        }
        else
        {
            accuracyFeature!.Point.X = newPoint.X;
            accuracyFeature!.Point.Y = newPoint.Y;
            pulseFeature!.Point.X = newPoint.X;
            pulseFeature!.Point.Y = newPoint.Y;
            userFeature!.Point.X = newPoint.X;
            userFeature!.Point.Y = newPoint.Y;
            UserLayer.DataHasChanged();
        }

        if (centerMap && map?.Navigator != null)
        {
            map.Navigator.CenterOnAndZoomTo(newPoint, 0.5, 500,
                Mapsui.Animations.Easing.CubicOut);
        }
    }

    // =============================
    // ROUTE DRAW — blue line
    // =============================
    public async Task DrawRoute(Map map, Location from, Poi to)
    {
        var points = await GetRoutePoints(from, to);
        if (points.Count < 2) return;

        var line = new NtsGeometry.LineString(
            points.Select(p => new NtsGeometry.Coordinate(p.X, p.Y)).ToArray());

        var feature = new GeometryFeature { Geometry = line };

        feature.Styles.Add(new VectorStyle { Line = new Pen(Color.White, 10) });
        feature.Styles.Add(new VectorStyle { Line = new Pen(new Color(30, 136, 229), 6) });

        RouteLayer.Features = new[] { feature };
        RouteLayer.DataHasChanged();

        if (!map.Layers.Contains(RouteLayer))
            map.Layers.Add(RouteLayer);

        var bbox = feature.Extent;
        if (bbox != null)
        {
            var padded = bbox.Grow(bbox.Width * 0.15, bbox.Height * 0.15);
            map.Navigator.ZoomToBox(padded, MBoxFit.Fit, 600);
        }
    }

    public void ClearRoute()
    {
        RouteLayer.Features = Array.Empty<IFeature>();
        RouteLayer.DataHasChanged();
    }

    // =============================
    // POI
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
                Styles = { poiIconStyle }
            };
            poiFeatures.Add(feature);
        }

        PoiLayer.Features = poiFeatures;
        PoiLayer.DataHasChanged();
    }

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
                        Fill = new Brush(new Color(30, 136, 229))
                    }
                }
            };
            poiFeatures.Add(highlightFeature);
        }
        else
        {
            highlightFeature.Point.X = newPoint.X;
            highlightFeature.Point.Y = newPoint.Y;
        }

        PoiLayer.DataHasChanged();
    }

    // =============================
    // LOCATION HISTORY HEAT MAP
    // =============================
    public void LoadHeatMap(Map map, List<UserLocationLog> logs)
    {
        var features = new List<PointFeature>();

        foreach (var l in logs)
        {
            var spherical = SphericalMercator.FromLonLat(
                (double)l.Longitude, (double)l.Latitude);

            var f = new PointFeature(new MPoint(spherical.x, spherical.y))
            {
                Styles = {
                    new SymbolStyle
                    {
                        SymbolScale = 0.6,
                        Fill = new Brush(new Color(255, 80, 80, 60))
                    }
                }
            };
            features.Add(f);
        }

        HeatLayer.Features = features;
        HeatLayer.DataHasChanged();

        if (!map.Layers.Contains(HeatLayer))
            map.Layers.Add(HeatLayer);
    }

    // ══════════════════════════════════════════════════════════════════
    // 🔥 POI VISIT-COUNT HEATMAP BUBBLES
    // Render hình tròn bán kính/opacity tỉ lệ với số lần user bước vào
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Render heatmap bubbles lên map dựa trên API data.
    /// Scale: bubble lớn hơn + đậm hơn khi sum cao hơn.
    /// Dùng SymbolStyle vì Mapsui 5.0.2 không hỗ trợ true circle radius theo meters —
    /// scale tương đối đủ cho UX heatmap visualization.
    /// </summary>
    public void LoadPoiHeatmap(Map map, List<HeatmapPoiData> data)
    {
        if (data == null || data.Count == 0) return;

        var maxSum = data.Max(d => d.Sum);
        var features = new List<PointFeature>();

        foreach (var item in data)
        {
            if (item.Lat == 0 && item.Lng == 0) continue;

            var spherical = SphericalMercator.FromLonLat(item.Lng, item.Lat);
            var pt = new MPoint(spherical.x, spherical.y);

            // Normalize sum → scale [0.4 .. 3.0]
            double norm = maxSum > 0 ? (double)item.Sum / maxSum : 0.1;
            double scale = 0.4 + norm * 2.6;

            // Opacity: 40 → 160 (alpha 0..255)
            int alpha = (int)(40 + norm * 120);

            // Outer glow — lớn, mờ
            var glowFeature = new PointFeature(new MPoint(pt.X, pt.Y))
            {
                Styles = {
                    new SymbolStyle
                    {
                        SymbolScale = scale * 1.8,
                        Fill        = new Brush(new Color(255, 100, 30, alpha / 3)),
                        Outline     = new Pen(Color.Transparent, 0)
                    }
                }
            };

            // Core bubble — nhỏ hơn, đậm hơn
            var coreFeature = new PointFeature(new MPoint(pt.X, pt.Y))
            {
                Styles = {
                    new SymbolStyle
                    {
                        SymbolScale = scale,
                        Fill        = new Brush(new Color(255, 80, 0, alpha)),
                        Outline     = new Pen(new Color(255, 120, 40, 180), 1.5)
                    }
                }
            };

            features.Add(glowFeature);
            features.Add(coreFeature);
        }

        HeatmapLayer.Features = features;
        HeatmapLayer.DataHasChanged();

        if (!map.Layers.Contains(HeatmapLayer))
            map.Layers.Insert(0, HeatmapLayer);  // Insert below POI markers
    }

    /// <summary>Xoá toàn bộ POI heatmap bubbles.</summary>
    public void ClearPoiHeatmap()
    {
        HeatmapLayer.Features = Array.Empty<IFeature>();
        HeatmapLayer.DataHasChanged();
    }

    // =============================
    // PULSE ANIMATION
    // =============================
    private void StartPulseAnimation()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        dispatcher.StartTimer(TimeSpan.FromMilliseconds(60), () =>
        {
            if (pulseFeature == null) return true;

            var style = pulseFeature.Styles.FirstOrDefault() as SymbolStyle;
            if (style == null) return true;

            pulseScale = pulseGrowing ? pulseScale + 0.04 : pulseScale - 0.04;
            if (pulseScale > 1.6) pulseGrowing = false;
            if (pulseScale < 1.0) pulseGrowing = true;

            if (Math.Abs(style.SymbolScale - pulseScale) > 0.01)
            {
                style.SymbolScale = pulseScale;
                UserLayer.DataHasChanged();
            }

            return true;
        });
    }

    // =============================
    // ROUTE CALCULATION (OSRM)
    // =============================
    public async Task<List<MPoint>> GetRoutePoints(Location from, Poi to)
    {
        var url =
            $"https://router.project-osrm.org/route/v1/driving/" +
            $"{from.Longitude.ToString(CultureInfo.InvariantCulture)}," +
            $"{from.Latitude.ToString(CultureInfo.InvariantCulture)};" +
            $"{to.Lng.ToString(CultureInfo.InvariantCulture)}," +
            $"{to.Lat.ToString(CultureInfo.InvariantCulture)}" +
            $"?overview=full&geometries=geojson";

        using var http = new HttpClient();
        var response = await http.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"OSRM error: {response.StatusCode} - {error}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var doc = System.Text.Json.JsonDocument.Parse(json);

        var coords = doc.RootElement
            .GetProperty("routes")[0]
            .GetProperty("geometry")
            .GetProperty("coordinates");

        var points = new List<MPoint>();

        foreach (var c in coords.EnumerateArray())
        {
            var lon = c[0].GetDouble();
            var lat = c[1].GetDouble();
            var mercator = SphericalMercator.FromLonLat(lon, lat);
            points.Add(new MPoint(mercator.x, mercator.y));
        }

        return points;
    }
}
