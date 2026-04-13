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
    // ── highlight feature (SymbolStyle cũ — giữ để không break các caller khác) ──
    private PointFeature? highlightFeature;

    public MemoryLayer UserLayer = new();
    public MemoryLayer PoiLayer = new();
    public MemoryLayer HeatLayer = new();
    public MemoryLayer RouteLayer = new();
    public MemoryLayer HeatmapLayer = new();

    private PointFeature? userFeature;
    private PointFeature? pulseFeature;
    private PointFeature? accuracyFeature;

    private readonly List<PointFeature> poiFeatures = new();

    // ── Map poiId → PointFeature để swap icon chính xác ──
    private readonly Dictionary<int, PointFeature> _poiFeatureMap = new();

    // ── Track poiId đang được select (để deselect khi cần) ──
    private int? _selectedPoiId = null;

    private Location? lastLocation;

    private double pulseScale = 1.0;
    private bool pulseGrowing = true;

    // ── Styles — khởi tạo 1 lần, dùng lại để tránh tạo object thừa ──
    private static readonly ImageStyle _normalIconStyle = new ImageStyle
    {
        Image = "embedded://SmartTourApp.Resources.Images.resicon.png",
        SymbolScale = 0.6,
        Offset = new Offset(0, 20)
    };

    private static readonly ImageStyle _selectedIconStyle = new ImageStyle
    {
        Image = "embedded://SmartTourApp.Resources.Images.mappin.png",
        SymbolScale = 0.6,
        Offset = new Offset(0, 20)
    };

    // Giữ alias để LoadPois không bị break nếu code ngoài ref tới
    private static readonly ImageStyle poiIconStyle = _normalIconStyle;

    public MapViewModel()
    {
        StartPulseAnimation();
    }

    // ═══════════════════════════════════════════════════════════════
    // USER LOCATION
    // ═══════════════════════════════════════════════════════════════
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
                Styles =
                {
                    new SymbolStyle
                    {
                        SymbolScale = 2.0,
                        Fill = new Brush(new Color(30, 136, 229, 35))
                    }
                }
            };

            pulseFeature = new PointFeature(newPoint)
            {
                Styles =
                {
                    new SymbolStyle
                    {
                        SymbolScale = 1.2,
                        Fill = new Brush(new Color(30, 136, 229, 70))
                    }
                }
            };

            userFeature = new PointFeature(newPoint)
            {
                Styles =
                {
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

    // ═══════════════════════════════════════════════════════════════
    // ROUTE DRAW — blue line
    // ═══════════════════════════════════════════════════════════════
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

    // ═══════════════════════════════════════════════════════════════
    // POI — Load với dict mapping poiId → feature
    // ═══════════════════════════════════════════════════════════════
    public void LoadPois(Map map, List<Poi> pois)
    {
        poiFeatures.Clear();
        _poiFeatureMap.Clear();
        _selectedPoiId = null;
        PoiLayer.Style = null;

        foreach (var poi in pois)
        {
            var spherical = SphericalMercator.FromLonLat(poi.Lng, poi.Lat);

            // Mỗi feature có ImageStyle riêng (instance riêng) để swap độc lập
            var feature = new PointFeature(new MPoint(spherical.x, spherical.y))
            {
                Styles =
                {
                    new ImageStyle
                    {
                        Image       = "embedded://SmartTourApp.Resources.Images.resicon.png",
                        SymbolScale = 0.6,
                        Offset      = new Offset(0, 20)
                    }
                }
            };

            poiFeatures.Add(feature);
            _poiFeatureMap[poi.Id] = feature;
        }

        PoiLayer.Features = poiFeatures;
        PoiLayer.DataHasChanged();
    }

    // ═══════════════════════════════════════════════════════════════
    // 🔥 SELECT POI ICON — đổi sang mappin, reset poi cũ về resicon
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Swap icon của POI được chọn sang mappin.png,
    /// đồng thời reset POI trước đó về resicon.png.
    /// Gọi khi user tap vào POI icon hoặc tap card.
    /// </summary>
    public void SelectPoiIcon(int poiId)
    {
        // Deselect poi cũ nếu khác
        if (_selectedPoiId.HasValue && _selectedPoiId.Value != poiId)
            ResetPoiIcon(_selectedPoiId.Value);

        // Set selected icon
        if (_poiFeatureMap.TryGetValue(poiId, out var feature))
        {
            SetPoiIconImage(feature, "embedded://SmartTourApp.Resources.Images.mappin.png");
        }

        _selectedPoiId = poiId;
        PoiLayer.DataHasChanged();
    }

    /// <summary>
    /// Reset icon POI về resicon.png (normal state).
    /// Gọi khi user bấm X trên bottom card.
    /// </summary>
    public void DeselectPoiIcon()
    {
        if (_selectedPoiId.HasValue)
        {
            ResetPoiIcon(_selectedPoiId.Value);
            _selectedPoiId = null;
            PoiLayer.DataHasChanged();
        }
    }

    // ── Internal helper: reset 1 POI về normal icon ──
    private void ResetPoiIcon(int poiId)
    {
        if (_poiFeatureMap.TryGetValue(poiId, out var feature))
            SetPoiIconImage(feature, "embedded://SmartTourApp.Resources.Images.resicon.png");
    }

    // ── Internal helper: thay Image string trên ImageStyle của feature ──
    private static void SetPoiIconImage(PointFeature feature, string imageUri)
    {
        if (feature.Styles.FirstOrDefault() is ImageStyle imgStyle)
            imgStyle.Image = imageUri;
    }

    // ═══════════════════════════════════════════════════════════════
    // HIGHLIGHT POI (giữ nguyên — dùng SymbolStyle overlay riêng)
    // ═══════════════════════════════════════════════════════════════
    public void HighlightPoi(Map map, double lat, double lng)
    {
        var spherical = SphericalMercator.FromLonLat(lng, lat);
        var newPoint = new MPoint(spherical.x, spherical.y);

        if (highlightFeature == null)
        {
            highlightFeature = new PointFeature(newPoint)
            {
                Styles =
                {
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

    // ═══════════════════════════════════════════════════════════════
    // LOCATION HISTORY HEAT MAP
    // ═══════════════════════════════════════════════════════════════
    public void LoadHeatMap(Map map, List<UserLocationLog> logs)
    {
        var features = new List<PointFeature>();

        foreach (var l in logs)
        {
            var spherical = SphericalMercator.FromLonLat(
                (double)l.Longitude, (double)l.Latitude);

            var f = new PointFeature(new MPoint(spherical.x, spherical.y))
            {
                Styles =
                {
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

    // ═══════════════════════════════════════════════════════════════
    // 🔥 POI VISIT-COUNT HEATMAP BUBBLES
    // ═══════════════════════════════════════════════════════════════
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

            double norm = maxSum > 0 ? (double)item.Sum / maxSum : 0.1;
            double scale = 0.4 + norm * 2.6;
            int alpha = (int)(40 + norm * 120);

            var glowFeature = new PointFeature(new MPoint(pt.X, pt.Y))
            {
                Styles =
                {
                    new SymbolStyle
                    {
                        SymbolScale = scale * 1.8,
                        Fill        = new Brush(new Color(255, 100, 30, alpha / 3)),
                        Outline     = new Pen(Color.Transparent, 0)
                    }
                }
            };

            var coreFeature = new PointFeature(new MPoint(pt.X, pt.Y))
            {
                Styles =
                {
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
            map.Layers.Insert(0, HeatmapLayer);
    }

    public void ClearPoiHeatmap()
    {
        HeatmapLayer.Features = Array.Empty<IFeature>();
        HeatmapLayer.DataHasChanged();
    }

    // ═══════════════════════════════════════════════════════════════
    // PULSE ANIMATION
    // ═══════════════════════════════════════════════════════════════
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

    // ═══════════════════════════════════════════════════════════════
    // ROUTE CALCULATION (OSRM)
    // ═══════════════════════════════════════════════════════════════
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