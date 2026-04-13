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
using Font = Mapsui.Styles.Font;

namespace SmartTourApp.ViewModels;

public class MapViewModel
{
    // GIỮ field để không break caller — nhưng KHÔNG bao giờ add vào poiFeatures nữa
    private PointFeature? highlightFeature;

    public MemoryLayer UserLayer = new();
    public MemoryLayer PoiLayer = new();
    public MemoryLayer HeatLayer = new();
    public MemoryLayer RouteLayer = new() { Name = "Route" };
    public MemoryLayer HeatmapLayer = new();
    public MemoryLayer TourPoiLayer = new() { Name = "TourPoi" };
    public MemoryLayer TourRouteLayer = new() { Name = "TourRoute" };

    private PointFeature? userFeature;
    private PointFeature? pulseFeature;
    private PointFeature? accuracyFeature;

    // CHỈ chứa POI ImageStyle features — KHÔNG có SymbolStyle feature nào
    private readonly List<PointFeature> poiFeatures = new();
    private readonly Dictionary<int, PointFeature> _poiFeatureMap = new();
    private int? _selectedPoiId = null;
    private readonly HashSet<int> _tourPoiIds = new();
    private Location? lastLocation;
    private double pulseScale = 1.0;
    private bool pulseGrowing = true;

    // Alias giữ để không break nếu code ngoài ref
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
            var distance = Location.CalculateDistance(lastLocation, loc, DistanceUnits.Kilometers);
            if (distance < 0.003) return;
        }
        lastLocation = loc;

        var spherical = SphericalMercator.FromLonLat(loc.Longitude, loc.Latitude);
        var newPoint = new MPoint(spherical.x, spherical.y);

        if (userFeature == null)
        {
            accuracyFeature = new PointFeature(newPoint)
            {
                Styles = { new SymbolStyle { SymbolScale = 2.0, Fill = new Brush(new Color(30, 136, 229, 35)) } }
            };
            pulseFeature = new PointFeature(newPoint)
            {
                Styles = { new SymbolStyle { SymbolScale = 1.2, Fill = new Brush(new Color(30, 136, 229, 70)) } }
            };
            userFeature = new PointFeature(newPoint)
            {
                Styles = { new SymbolStyle { SymbolScale = 0.35, Fill = new Brush(new Color(30, 136, 229)), Outline = new Pen(Color.White, 4) } }
            };
            UserLayer.Features = new[] { accuracyFeature, pulseFeature, userFeature };
            centerMap = true;
        }
        else
        {
            accuracyFeature!.Point.X = newPoint.X; accuracyFeature!.Point.Y = newPoint.Y;
            pulseFeature!.Point.X = newPoint.X; pulseFeature!.Point.Y = newPoint.Y;
            userFeature!.Point.X = newPoint.X; userFeature!.Point.Y = newPoint.Y;
            UserLayer.DataHasChanged();
        }

        if (centerMap && map?.Navigator != null)
            map.Navigator.CenterOnAndZoomTo(newPoint, 0.5, 500, Mapsui.Animations.Easing.CubicOut);
    }

    // ═══════════════════════════════════════════════════════════════
    // ROUTE DRAW — blue line, luôn overlay lên TourRouteLayer
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

        EnsureRouteLayerOnTop(map);

        var bbox = feature.Extent;
        if (bbox != null)
        {
            var padded = bbox.Grow(bbox.Width * 0.15, bbox.Height * 0.15);
            map.Navigator.ZoomToBox(padded, MBoxFit.Fit, 600);
        }
    }

    private void EnsureRouteLayerOnTop(Map map)
    {
        if (!map.Layers.Contains(RouteLayer)) { map.Layers.Add(RouteLayer); return; }

        var list = map.Layers.ToList();
        int routeIdx = list.IndexOf(RouteLayer);
        int maxBelow = Math.Max(list.IndexOf(TourRouteLayer), list.IndexOf(TourPoiLayer));
        if (maxBelow < 0 || routeIdx > maxBelow) return;

        map.Layers.Remove(RouteLayer);
        map.Layers.Add(RouteLayer);
    }

    public void ClearRoute()
    {
        RouteLayer.Features = Array.Empty<IFeature>();
        RouteLayer.DataHasChanged();
    }

    // ═══════════════════════════════════════════════════════════════
    // TOUR OVERLAY
    // ═══════════════════════════════════════════════════════════════
    public MPoint? LoadTourOverlay(Map map, List<TourPoiDto> tourPois)
    {
        _tourPoiIds.Clear();

        TourPoiLayer.Style = null;
        TourRouteLayer.Style = null;

        foreach (var f in _poiFeatureMap)
            SetPoiIconImage(f.Value, "embedded://SmartTourApp.Resources.Images.resicon.png");

        var tourLabelFeatures = new List<IFeature>();
        var routeCoords = new List<NtsGeometry.Coordinate>();

        for (int i = 0; i < tourPois.Count; i++)
        {
            var dto = tourPois[i];
            _tourPoiIds.Add(dto.PoiId);

            if (_poiFeatureMap.TryGetValue(dto.PoiId, out var mainFeature))
                SetPoiIconImage(mainFeature, "embedded://SmartTourApp.Resources.Images.mappin.png");

            var s = SphericalMercator.FromLonLat(dto.Lng, dto.Lat);
            var pt = new MPoint(s.x, s.y);
            routeCoords.Add(new NtsGeometry.Coordinate(pt.X, pt.Y));

            // 1. Tạo Feature nhãn số
            var lf = new PointFeature(new MPoint(pt.X, pt.Y));

            // 🔥 CHIÊU CUỐI: Dùng một ImageStyle NHƯNG cho nó tàng hình
            // Để Mapsui thấy "À, có ảnh rồi" nên nó sẽ KHÔNG vẽ vòng tròn trắng mặc định nữa.
            lf.Styles.Add(new ImageStyle
            {
                Image = "embedded://SmartTourApp.Resources.Images.resicon.png",
                SymbolScale = 0.01, // Nhỏ đến mức mắt thường không thấy
                Opacity = 0,        // Trong suốt hoàn toàn
                Enabled = true      // Phải để True để nó ghi đè cái mặc định
            });

            // 2. Thêm số đỏ đè lên
            lf.Styles.Add(new LabelStyle
            {
                Text = (i + 1).ToString(),
                Font = new Font { Size = 11, Bold = true },
                ForeColor = Color.White,
                BackColor = new Brush(new Color(220, 38, 38)), // Nền đỏ rực

                // Tắt mọi thứ liên quan đến màu trắng
                Halo = new Pen(Color.Transparent, 0),
                BorderThickness = 0,
                BorderColor = Color.Transparent,

                // Vị trí: Đẩy lên cao để lọt vào vòng tròn của mappin.png
                Offset = new Offset(0, -28),
                HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
                VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Center,
                MaxWidth = 20
            });

            tourLabelFeatures.Add(lf);
        }

        TourPoiLayer.Features = tourLabelFeatures;
        TourPoiLayer.DataHasChanged();
        if (!map.Layers.Contains(TourPoiLayer)) map.Layers.Add(TourPoiLayer);

        if (routeCoords.Count >= 2)
        {
            var redLine = new NtsGeometry.LineString(routeCoords.ToArray());
            var rf = new GeometryFeature { Geometry = redLine };
            rf.Styles.Add(new VectorStyle { Line = new Pen(Color.White, 8), Fill = null });
            rf.Styles.Add(new VectorStyle { Line = new Pen(new Color(220, 38, 38), 4), Fill = null });

            TourRouteLayer.Features = new[] { rf };
            TourRouteLayer.DataHasChanged();

            if (!map.Layers.Contains(TourRouteLayer))
            {
                var list = map.Layers.ToList();
                int poiIdx = list.IndexOf(PoiLayer);
                if (poiIdx >= 0) map.Layers.Insert(poiIdx, TourRouteLayer);
                else map.Layers.Add(TourRouteLayer);
            }
        }

        if (map.Layers.Contains(RouteLayer)) EnsureRouteLayerOnTop(map);

        PoiLayer.DataHasChanged();

        if (routeCoords.Count == 0) return null;
        return new MPoint(routeCoords.Average(c => c.X), routeCoords.Average(c => c.Y));
    }

    public void ClearTourOverlay(Map map)
    {
        foreach (var id in _tourPoiIds)
            if (_poiFeatureMap.TryGetValue(id, out var f))
                SetPoiIconImage(f, "embedded://SmartTourApp.Resources.Images.resicon.png");

        _tourPoiIds.Clear();
        TourPoiLayer.Features = Array.Empty<IFeature>();
        TourPoiLayer.DataHasChanged();
        TourRouteLayer.Features = Array.Empty<IFeature>();
        TourRouteLayer.DataHasChanged();
        PoiLayer.DataHasChanged();
    }

    public bool IsPoiInTour(int poiId) => _tourPoiIds.Contains(poiId);

    // ═══════════════════════════════════════════════════════════════
    // POI LOAD
    // ═══════════════════════════════════════════════════════════════
    public void LoadPois(Map map, List<Poi> pois)
    {
        poiFeatures.Clear();
        _poiFeatureMap.Clear();
        _selectedPoiId = null;
        _tourPoiIds.Clear();

        PoiLayer.Style = null;

        highlightFeature = null;

        // FIX YC1: null highlightFeature — nó không còn trong poiFeatures nên sẽ
        // không được render. Mapsui 5.0.2 cache feature references nên phải null rõ ràng.
        highlightFeature = null;

        PoiLayer.Style = null;

        foreach (var poi in pois)
        {
            var s = SphericalMercator.FromLonLat(poi.Lng, poi.Lat);
            var feature = new PointFeature(new MPoint(s.x, s.y))
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

        // Chỉ set poiFeatures — KHÔNG add highlightFeature
        PoiLayer.Features = poiFeatures;
        PoiLayer.DataHasChanged();
    }

    // ═══════════════════════════════════════════════════════════════
    // ICON SELECT / DESELECT
    // ═══════════════════════════════════════════════════════════════
    public void SelectPoiIcon(int poiId)
    {
        if (_selectedPoiId.HasValue &&
            _selectedPoiId.Value != poiId &&
            !_tourPoiIds.Contains(_selectedPoiId.Value))
        {
            ResetPoiIcon(_selectedPoiId.Value);
        }

        if (_poiFeatureMap.TryGetValue(poiId, out var feature))
            SetPoiIconImage(feature, "embedded://SmartTourApp.Resources.Images.mappin.png");

        _selectedPoiId = poiId;
        PoiLayer.DataHasChanged();
    }

    public void DeselectPoiIcon()
    {
        if (_selectedPoiId.HasValue)
        {
            if (!_tourPoiIds.Contains(_selectedPoiId.Value))
                ResetPoiIcon(_selectedPoiId.Value);
            _selectedPoiId = null;
            PoiLayer.DataHasChanged();
        }
    }

    /// <summary>
    /// Force reset icon về resicon bất kể tour state.
    /// Dùng khi bấm X trên POI ngoài tour trong tour mode.
    /// </summary>
    public void ForceDeselectPoiIcon(int poiId)
    {
        ResetPoiIcon(poiId);
        if (_selectedPoiId == poiId) _selectedPoiId = null;
        PoiLayer.DataHasChanged();
    }

    private void ResetPoiIcon(int poiId)
    {
        if (_poiFeatureMap.TryGetValue(poiId, out var f))
            SetPoiIconImage(f, "embedded://SmartTourApp.Resources.Images.resicon.png");
    }

    private static void SetPoiIconImage(PointFeature feature, string imageUri)
    {
        if (feature.Styles.FirstOrDefault() is ImageStyle imgStyle)
            imgStyle.Image = imageUri;
    }

    // ═══════════════════════════════════════════════════════════════
    // HIGHLIGHT POI — GIỮ signature, NOP hoàn toàn
    // Mapsui 5.0.2: SymbolStyle mặc định = vòng tròn trắng
    // SelectPoiIcon (mappin.png) đã đủ để visual highlight
    // ═══════════════════════════════════════════════════════════════
    public void HighlightPoi(Map map, double lat, double lng)
    {
        // NOP — intentionally empty
        // DO NOT add SymbolStyle features here — causes white circle bug in Mapsui 5.0.2
    }

    // ═══════════════════════════════════════════════════════════════
    // HEAT MAPS
    // ═══════════════════════════════════════════════════════════════
    public void LoadHeatMap(Map map, List<UserLocationLog> logs)
    {
        var features = logs.Select(l =>
        {
            var s = SphericalMercator.FromLonLat((double)l.Longitude, (double)l.Latitude);
            var f = new PointFeature(new MPoint(s.x, s.y));
            f.Styles.Add(new SymbolStyle { SymbolScale = 0.6, Fill = new Brush(new Color(255, 80, 80, 60)) });
            return f;
        }).ToList();

        HeatLayer.Features = features;
        HeatLayer.DataHasChanged();
        if (!map.Layers.Contains(HeatLayer)) map.Layers.Add(HeatLayer);
    }

    public void LoadPoiHeatmap(Map map, List<HeatmapPoiData> data)
    {
        if (data == null || data.Count == 0) return;

        var maxSum = data.Max(d => d.Sum);
        var features = new List<PointFeature>();

        foreach (var item in data)
        {
            if (item.Lat == 0 && item.Lng == 0) continue;
            var s = SphericalMercator.FromLonLat(item.Lng, item.Lat);
            var pt = new MPoint(s.x, s.y);
            double norm = maxSum > 0 ? (double)item.Sum / maxSum : 0.1;
            double scale = 0.4 + norm * 2.6;
            int alpha = (int)(40 + norm * 120);

            var glow = new PointFeature(new MPoint(pt.X, pt.Y));
            glow.Styles.Add(new SymbolStyle { SymbolScale = scale * 1.8, Fill = new Brush(new Color(255, 100, 30, alpha / 3)), Outline = new Pen(Color.Transparent, 0) });
            var core = new PointFeature(new MPoint(pt.X, pt.Y));
            core.Styles.Add(new SymbolStyle { SymbolScale = scale, Fill = new Brush(new Color(255, 80, 0, alpha)), Outline = new Pen(new Color(255, 120, 40, 180), 1.5) });
            features.Add(glow); features.Add(core);
        }

        HeatmapLayer.Features = features;
        HeatmapLayer.DataHasChanged();
        if (!map.Layers.Contains(HeatmapLayer)) map.Layers.Insert(0, HeatmapLayer);
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
            if (pulseFeature?.Styles.FirstOrDefault() is not SymbolStyle style) return true;
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
            throw new Exception($"OSRM error: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");

        var json = await response.Content.ReadAsStringAsync();
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var coords = doc.RootElement.GetProperty("routes")[0].GetProperty("geometry").GetProperty("coordinates");

        var points = new List<MPoint>();
        foreach (var c in coords.EnumerateArray())
        {
            var m = SphericalMercator.FromLonLat(c[0].GetDouble(), c[1].GetDouble());
            points.Add(new MPoint(m.x, m.y));
        }
        return points;
    }
}