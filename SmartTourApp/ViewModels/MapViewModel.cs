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
    public MemoryLayer HeatmapLayer = new();
    public MemoryLayer NearestHighlightLayer = new() { Name = "NearestHighlight" };

    private PointFeature? userFeature;
    private PointFeature? pulseFeature;
    private PointFeature? accuracyFeature;

    private readonly List<PointFeature> poiFeatures = new();
    private readonly Dictionary<int, PointFeature> _poiFeatureMap = new();
    private int? _selectedPoiId = null;
    private Location? lastLocation;

    // YC3: giảm fps animation
    private double pulseScale = 1.0;
    private bool pulseGrowing = true;

    // Nearest highlight state
    private int? _nearestPoiId = null;
    private PointFeature? _nearestPulse1;
    private PointFeature? _nearestPulse2;
    private PointFeature? _nearestCore;
    private double _nearestPulseScale1 = 1.0;
    private double _nearestPulseScale2 = 1.3;
    private bool _nearestPulseGrowing = true;

    // YC3: Chỉ dirty-flag khi cần update thực sự
    private bool _nearestLayerDirty = false;
    private bool _userLayerDirty = false;

    // YC3: Giảm khoảng cách threshold để tránh update quá nhiều
    private const double UserMoveThresholdKm = 0.005; // 5m thay vì 3m cũ

    public MapViewModel()
    {
        // YC3: Giảm FPS animation từ ~16fps → ~10fps để tiết kiệm CPU
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
            // YC3: Tăng threshold để giảm re-render
            if (distance < UserMoveThresholdKm) return;
        }
        lastLocation = loc;

        var spherical = SphericalMercator.FromLonLat(loc.Longitude, loc.Latitude);
        var newPoint = new MPoint(spherical.x, spherical.y);

        if (userFeature == null)
        {
            accuracyFeature = new PointFeature(newPoint)
            {
                Styles = { new SymbolStyle
                {
                    SymbolScale = 2.0,
                    Fill = new Brush(new Color(30, 136, 229, 35))
                }}
            };
            pulseFeature = new PointFeature(newPoint)
            {
                Styles = { new SymbolStyle
                {
                    SymbolScale = 1.2,
                    Fill = new Brush(new Color(30, 136, 229, 70))
                }}
            };
            userFeature = new PointFeature(newPoint)
            {
                Styles = { new SymbolStyle
                {
                    SymbolScale = 0.35,
                    Fill = new Brush(new Color(30, 136, 229)),
                    Outline = new Pen(Color.White, 4)
                }}
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
            _userLayerDirty = true;
        }

        if (centerMap && map?.Navigator != null)
            map.Navigator.CenterOnAndZoomTo(newPoint, 0.5, 500,
                Mapsui.Animations.Easing.CubicOut);
    }

    // ═══════════════════════════════════════════════════════════════
    // NEAREST POI HIGHLIGHT
    // ═══════════════════════════════════════════════════════════════

    public void HighlightPoi(Map map, double lat, double lng)
    {
        var s = SphericalMercator.FromLonLat(lng, lat);
        var pt = new MPoint(s.x, s.y);

        _nearestPulse1 = new PointFeature(new MPoint(pt.X, pt.Y))
        {
            Styles =
            {
                new SymbolStyle
                {
                    SymbolScale = 1.8,
                    Fill = new Brush(new Color(255, 165, 0, 55)),
                    Outline = new Pen(new Color(255, 165, 0, 120), 2.5)
                }
            }
        };

        _nearestPulse2 = new PointFeature(new MPoint(pt.X, pt.Y))
        {
            Styles =
            {
                new SymbolStyle
                {
                    SymbolScale = 1.3,
                    Fill = new Brush(new Color(255, 200, 0, 80)),
                    Outline = new Pen(new Color(255, 200, 0, 180), 2.0)
                }
            }
        };

        _nearestCore = new PointFeature(new MPoint(pt.X, pt.Y))
        {
            Styles =
            {
                new SymbolStyle
                {
                    SymbolScale = 0.5,
                    Fill = new Brush(new Color(255, 120, 0, 220)),
                    Outline = new Pen(new Color(255, 255, 255, 200), 3.0)
                }
            }
        };

        NearestHighlightLayer.Features = new[] { _nearestPulse1, _nearestPulse2, _nearestCore };
        NearestHighlightLayer.DataHasChanged();

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

    public void SetNearestPoi(Map map, Poi poi)
    {
        if (_nearestPoiId == poi.Id) return;
        _nearestPoiId = poi.Id;
        HighlightPoi(map, poi.Lat, poi.Lng);
    }

    public void ClearNearestHighlight()
    {
        _nearestPoiId = null;
        _nearestPulse1 = null;
        _nearestPulse2 = null;
        _nearestCore = null;
        _nearestLayerDirty = false;

        NearestHighlightLayer.Features = Array.Empty<IFeature>();
        NearestHighlightLayer.DataHasChanged();
    }

    /// <summary>
    /// YC3: Giảm FPS animation nearest highlight từ 20fps → 10fps
    /// Dùng dirty flag để chỉ call DataHasChanged khi thực sự thay đổi
    /// </summary>
    private void StartNearestHighlightAnimation()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        // YC3: 100ms thay vì 50ms → giảm từ 20fps xuống 10fps
        dispatcher.StartTimer(TimeSpan.FromMilliseconds(100), () =>
        {
            if (_nearestPulse1 == null || _nearestPulse2 == null) return true;

            try
            {
                var prevScale1 = _nearestPulseScale1;

                _nearestPulseScale1 = _nearestPulseGrowing
                    ? _nearestPulseScale1 + 0.04   // step lớn hơn để bù interval chậm
                    : _nearestPulseScale1 - 0.04;

                if (_nearestPulseScale1 > 2.2) _nearestPulseGrowing = false;
                if (_nearestPulseScale1 < 1.2) _nearestPulseGrowing = true;

                // YC3: Chỉ update nếu thay đổi đáng kể (tránh update quá nhiều)
                if (Math.Abs(_nearestPulseScale1 - prevScale1) < 0.01) return true;

                _nearestPulseScale2 = 3.4 - _nearestPulseScale1;
                _nearestPulseScale2 = Math.Clamp(_nearestPulseScale2, 1.0, 2.2);

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
        map.Layers.Remove(RouteLayer);
        map.Layers.Add(RouteLayer);
    }

    public void ClearRoute()
    {
        RouteLayer.Features = Array.Empty<IFeature>();
        RouteLayer.DataHasChanged();
    }

    // ═══════════════════════════════════════════════════════════════
    // POI LOAD
    // ═══════════════════════════════════════════════════════════════
    public void LoadPois(Map map, List<Poi> pois)
    {
        poiFeatures.Clear();
        _poiFeatureMap.Clear();
        _selectedPoiId = null;

        PoiLayer.Style = null;
        highlightFeature = null;

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
        if (_selectedPoiId.HasValue && _selectedPoiId.Value != poiId)
            ResetPoiIcon(_selectedPoiId.Value);

        if (_poiFeatureMap.TryGetValue(poiId, out var feature))
            SetPoiIconImage(feature, "embedded://SmartTourApp.Resources.Images.mappin.png");

        _selectedPoiId = poiId;
        PoiLayer.DataHasChanged();
    }

    public void DeselectPoiIcon()
    {
        if (_selectedPoiId.HasValue)
        {
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
    // HEATMAP — giữ signature, không render lên map (CMS only)
    // ═══════════════════════════════════════════════════════════════
    public void LoadHeatMap(Map map, List<UserLocationLog> logs)
    {
        System.Diagnostics.Debug.WriteLine(
            $"[MapVM] LoadHeatMap: {logs.Count} logs — not rendered on map (CMS only)");
    }

    public void LoadPoiHeatmap(Map map, List<HeatmapPoiData> data)
    {
        System.Diagnostics.Debug.WriteLine(
            $"[MapVM] LoadPoiHeatmap: {data?.Count ?? 0} entries — not rendered on map (CMS only)");
    }

    public void ClearPoiHeatmap()
    {
        HeatmapLayer.Features = Array.Empty<IFeature>();
    }

    // ═══════════════════════════════════════════════════════════════
    // PULSE ANIMATION — YC3: Giảm từ 60ms → 100ms (10fps)
    // ═══════════════════════════════════════════════════════════════
    private void StartPulseAnimation()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        // YC3: 100ms interval (10fps) thay vì 60ms (16fps)
        dispatcher.StartTimer(TimeSpan.FromMilliseconds(100), () =>
        {
            if (pulseFeature?.Styles.FirstOrDefault() is not SymbolStyle style) return true;

            var prevScale = pulseScale;
            pulseScale = pulseGrowing ? pulseScale + 0.06 : pulseScale - 0.06;
            if (pulseScale > 1.6) pulseGrowing = false;
            if (pulseScale < 1.0) pulseGrowing = true;

            // YC3: Chỉ update khi thay đổi đáng kể
            if (Math.Abs(style.SymbolScale - pulseScale) > 0.02)
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
        http.Timeout = TimeSpan.FromSeconds(10); // YC3: thêm timeout
        var response = await http.GetAsync(url);
        if (!response.IsSuccessStatusCode)
            throw new Exception(
                $"OSRM error: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");

        var json = await response.Content.ReadAsStringAsync();
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var coords = doc.RootElement
            .GetProperty("routes")[0]
            .GetProperty("geometry")
            .GetProperty("coordinates");

        var points = new List<MPoint>();
        foreach (var c in coords.EnumerateArray())
        {
            var m = SphericalMercator.FromLonLat(c[0].GetDouble(), c[1].GetDouble());
            points.Add(new MPoint(m.x, m.y));
        }
        return points;
    }
}
