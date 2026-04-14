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
    // GIỮ field để không break caller
    private PointFeature? highlightFeature;

    public MemoryLayer UserLayer = new();
    public MemoryLayer PoiLayer = new();
    public MemoryLayer HeatLayer = new();
    public MemoryLayer RouteLayer = new() { Name = "Route" };

    // HeatmapLayer giữ field để không break code gọi — nhưng KHÔNG add vào map
    public MemoryLayer HeatmapLayer = new();

    public MemoryLayer TourPoiLayer = new() { Name = "TourPoi" };
    public MemoryLayer TourRouteLayer = new() { Name = "TourRoute" };

    // ── Nearest POI highlight layer — vòng tròn nhấp nháy màu cam/vàng ──
    public MemoryLayer NearestHighlightLayer = new() { Name = "NearestHighlight" };

    private PointFeature? userFeature;
    private PointFeature? pulseFeature;
    private PointFeature? accuracyFeature;

    private readonly List<PointFeature> poiFeatures = new();
    private readonly Dictionary<int, PointFeature> _poiFeatureMap = new();
    private int? _selectedPoiId = null;
    private readonly HashSet<int> _tourPoiIds = new();
    private Location? lastLocation;
    private double pulseScale = 1.0;
    private bool pulseGrowing = true;

    // ── Nearest highlight state ──
    private int? _nearestPoiId = null;
    private PointFeature? _nearestPulse1;
    private PointFeature? _nearestPulse2;
    private PointFeature? _nearestCore;
    private double _nearestPulseScale1 = 1.0;
    private double _nearestPulseScale2 = 1.3;
    private bool _nearestPulseGrowing = true;

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
        StartNearestHighlightAnimation();
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
    // NEAREST POI HIGHLIGHT (YC3 — thực sự vẽ vòng nhấp nháy)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Highlight POI gần nhất bằng vòng tròn nhấp nháy màu cam/vàng.
    /// Dùng 2 vòng pulse lệch pha nhau để tạo hiệu ứng "ripple" sống động.
    /// Mapsui 5.0.2: Dùng SymbolStyle trực tiếp KHÔNG dùng ImageStyle để tránh white circle bug.
    /// </summary>
    public void HighlightPoi(Map map, double lat, double lng)
    {
        var s = SphericalMercator.FromLonLat(lng, lat);
        var pt = new MPoint(s.x, s.y);

        // Outer pulse ring 1 — cam nhạt, to hơn, nhấp nháy chậm
        _nearestPulse1 = new PointFeature(new MPoint(pt.X, pt.Y))
        {
            Styles =
            {
                new SymbolStyle
                {
                    SymbolScale = 1.8,
                    Fill = new Brush(new Color(255, 165, 0, 55)),  // cam nhạt
                    Outline = new Pen(new Color(255, 165, 0, 120), 2.5)
                }
            }
        };

        // Outer pulse ring 2 — vàng, lệch pha 180°
        _nearestPulse2 = new PointFeature(new MPoint(pt.X, pt.Y))
        {
            Styles =
            {
                new SymbolStyle
                {
                    SymbolScale = 1.3,
                    Fill = new Brush(new Color(255, 200, 0, 80)),  // vàng trong
                    Outline = new Pen(new Color(255, 200, 0, 180), 2.0)
                }
            }
        };

        // Core dot — cam đậm, không nhấp nháy — làm "tâm"
        _nearestCore = new PointFeature(new MPoint(pt.X, pt.Y))
        {
            Styles =
            {
                new SymbolStyle
                {
                    SymbolScale = 0.5,
                    Fill = new Brush(new Color(255, 120, 0, 220)),  // cam đậm
                    Outline = new Pen(new Color(255, 255, 255, 200), 3.0)
                }
            }
        };

        NearestHighlightLayer.Features = new[] { _nearestPulse1, _nearestPulse2, _nearestCore };
        NearestHighlightLayer.DataHasChanged();

        // Đảm bảo layer đã add vào map (dưới POI layer để không che icon)
        if (!map.Layers.Contains(NearestHighlightLayer))
        {
            var layers = map.Layers.ToList();
            int poiIdx = layers.IndexOf(PoiLayer);
            if (poiIdx >= 0)
                map.Layers.Insert(poiIdx, NearestHighlightLayer);
            else
                map.Layers.Add(NearestHighlightLayer);
        }

        System.Diagnostics.Debug.WriteLine(
            $"[MapVM] HighlightPoi: lat={lat:F4}, lng={lng:F4}");
    }

    /// <summary>
    /// Đặt POI gần nhất và vẽ highlight — API mới rõ ràng hơn.
    /// MapPage gọi hàm này khi nearest POI thay đổi.
    /// </summary>
    public void SetNearestPoi(Map map, Poi poi)
    {
        if (_nearestPoiId == poi.Id) return; // Không re-render nếu cùng POI

        _nearestPoiId = poi.Id;
        HighlightPoi(map, poi.Lat, poi.Lng);
    }

    /// <summary>
    /// Xóa highlight nearest POI (khi user đã chọn POI cụ thể hoặc ra khỏi vùng).
    /// </summary>
    public void ClearNearestHighlight()
    {
        _nearestPoiId = null;
        _nearestPulse1 = null;
        _nearestPulse2 = null;
        _nearestCore = null;

        NearestHighlightLayer.Features = Array.Empty<IFeature>();
        NearestHighlightLayer.DataHasChanged();
    }

    /// <summary>
    /// Animation loop cho vòng tròn nhấp nháy nearest POI.
    /// Hai vòng lệch pha 180° tạo hiệu ứng ripple đẹp mắt.
    /// </summary>
    private void StartNearestHighlightAnimation()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        dispatcher.StartTimer(TimeSpan.FromMilliseconds(50), () =>
        {
            if (_nearestPulse1 == null || _nearestPulse2 == null) return true;

            try
            {
                // Pulse 1: 1.2 → 2.2 → 1.2 (chậm)
                _nearestPulseScale1 = _nearestPulseGrowing
                    ? _nearestPulseScale1 + 0.025
                    : _nearestPulseScale1 - 0.025;

                if (_nearestPulseScale1 > 2.2) _nearestPulseGrowing = false;
                if (_nearestPulseScale1 < 1.2) _nearestPulseGrowing = true;

                // Pulse 2: lệch pha — khi 1 to thì 2 nhỏ lại
                _nearestPulseScale2 = 3.4 - _nearestPulseScale1;
                _nearestPulseScale2 = Math.Clamp(_nearestPulseScale2, 1.0, 2.2);

                // Alpha thay đổi theo scale: to hơn → trong hơn (fade out effect)
                int alpha1 = (int)(200 - (_nearestPulseScale1 - 1.2) / 1.0 * 150);
                int alpha2 = (int)(200 - (_nearestPulseScale2 - 1.0) / 1.2 * 150);
                alpha1 = Math.Clamp(alpha1, 30, 200);
                alpha2 = Math.Clamp(alpha2, 30, 200);

                if (_nearestPulse1.Styles.FirstOrDefault() is SymbolStyle s1)
                {
                    s1.SymbolScale = _nearestPulseScale1;
                    s1.Fill = new Brush(new Color(255, 165, 0, alpha1 / 3));
                    s1.Outline = new Pen(new Color(255, 165, 0, alpha1), 2.5);
                }

                if (_nearestPulse2.Styles.FirstOrDefault() is SymbolStyle s2)
                {
                    s2.SymbolScale = _nearestPulseScale2;
                    s2.Fill = new Brush(new Color(255, 200, 0, alpha2 / 3));
                    s2.Outline = new Pen(new Color(255, 200, 0, alpha2), 2.0);
                }

                NearestHighlightLayer.DataHasChanged();
            }
            catch { /* ignore animation errors */ }

            return true;
        });
    }

    // ═══════════════════════════════════════════════════════════════
    // ROUTE DRAW
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

            var lf = new PointFeature(new MPoint(pt.X, pt.Y));

            lf.Styles.Add(new ImageStyle
            {
                Image = "embedded://SmartTourApp.Resources.Images.resicon.png",
                SymbolScale = 0.01,
                Opacity = 0,
                Enabled = true
            });

            lf.Styles.Add(new LabelStyle
            {
                Text = (i + 1).ToString(),
                Font = new Font { Size = 11, Bold = true },
                ForeColor = Color.White,
                BackColor = new Brush(new Color(220, 38, 38)),
                Halo = new Pen(Color.Transparent, 0),
                BorderThickness = 0,
                BorderColor = Color.Transparent,
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
    // HEATMAP — GIỮ signature, không render lên map
    // Heatmap chỉ hiển thị qua CMS dashboard, không phải trên mobile map
    // ═══════════════════════════════════════════════════════════════
    public void LoadHeatMap(Map map, List<UserLocationLog> logs)
    {
        // INTENTIONALLY NOT RENDERED ON MAP
        // Heatmap visualization is handled by the CMS dashboard (web)
        // Keeping method signature for backward compatibility
        System.Diagnostics.Debug.WriteLine(
            $"[MapVM] LoadHeatMap: {logs.Count} logs — not rendered on map (CMS only)");
    }

    public void LoadPoiHeatmap(Map map, List<HeatmapPoiData> data)
    {
        // INTENTIONALLY NOT RENDERED ON MAP
        // Heatmap visualization is handled by the CMS dashboard (web)
        System.Diagnostics.Debug.WriteLine(
            $"[MapVM] LoadPoiHeatmap: {data?.Count ?? 0} entries — not rendered on map (CMS only)");
    }

    public void ClearPoiHeatmap()
    {
        // NOP — heatmap không được render trên map
        HeatmapLayer.Features = Array.Empty<IFeature>();
    }

    // ═══════════════════════════════════════════════════════════════
    // PULSE ANIMATION (user dot)
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
