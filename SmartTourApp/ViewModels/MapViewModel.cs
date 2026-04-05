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

namespace SmartTourApp.ViewModels;

public class MapViewModel
{
    private PointFeature? highlightFeature;

    public MemoryLayer UserLayer = new();
    public MemoryLayer PoiLayer = new();
    public MemoryLayer HeatLayer = new();

    // 🔥 NEW
    public MemoryLayer RouteLayer = new();

    private PointFeature? userFeature;
    private PointFeature? pulseFeature;
    private PointFeature? accuracyFeature;

    private readonly List<PointFeature> poiFeatures = new();

    private Location? lastLocation;

    private double pulseScale = 1.0;
    private bool pulseGrowing = true;
    private DateTime lastUserRefresh = DateTime.MinValue;
    private DateTime lastPoiRefresh = DateTime.MinValue;
    private List<MPoint>? cachedRoute;
    private string? lastRouteKey;

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

            if ((DateTime.Now - lastUserRefresh).TotalMilliseconds > 100)
            {
                UserLayer.DataHasChanged();
                lastUserRefresh = DateTime.Now;
            }
        }

        if (centerMap && map?.Navigator != null)
        {
            map.Navigator.CenterOnAndZoomTo(newPoint, 0.5, 500, Mapsui.Animations.Easing.CubicOut);
        }
    }

    // =============================
    // 🔥 ROUTE DRAW
    // =============================
    public async Task DrawRoute(Map map, Location from, Poi to)
    {
        var key = $"{Math.Round(from.Latitude, 4)},{Math.Round(from.Longitude, 4)}-{to.Id}";

        List<MPoint> points;

        if (key == lastRouteKey && cachedRoute != null)
        {
            points = cachedRoute;
        }
        else
        {
            points = await GetRoutePoints(from, to);
            cachedRoute = points;
            lastRouteKey = key;
        }

        if (points.Count < 2)
            return;

        var line = new NtsGeometry.LineString(
            points.Select(p => new NtsGeometry.Coordinate(p.X, p.Y)).ToArray()
        );

        var feature = new GeometryFeature
        {
            Geometry = line
        };

        feature.Styles.Add(new VectorStyle
        {
            Line = new Pen(Color.White, 8)
        });

        feature.Styles.Add(new VectorStyle
        {
            Line = new Pen(new Color(33, 150, 243), 5)
        });
        RouteLayer.Features = new List<IFeature> { feature };
        RouteLayer.DataHasChanged();

        if (!map.Layers.Contains(RouteLayer))
            map.Layers.Add(RouteLayer);

        var bbox = feature.Extent;
        if (bbox != null && map.Navigator.Viewport != null)
        {
            map.Navigator.ZoomToBox(bbox, MBoxFit.Fit, 500);
        }
    }

    public void ClearRoute()
    {
        if (RouteLayer.Features != null)
        {
            RouteLayer.Features = new List<IFeature>();
            RouteLayer.DataHasChanged();
        }
    }

    // =============================
    // POI + HEAT + HIGHLIGHT (GIỮ NGUYÊN)
    // =============================
    public void LoadPois(Map map, List<Poi> pois)
    {
        if (poiFeatures.Count == pois.Count && poiFeatures.Count > 0)
            return;

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
        if ((DateTime.Now - lastPoiRefresh).TotalMilliseconds > 200)
        {
            PoiLayer.DataHasChanged();
            lastPoiRefresh = DateTime.Now;
        }
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
                        Fill = new Brush(Color.Green)
                    }
                }
            };

            if (!poiFeatures.Any(f => f == highlightFeature))
                poiFeatures.Add(highlightFeature);
        }
        else
        {
            highlightFeature.Point.X = newPoint.X;
            highlightFeature.Point.Y = newPoint.Y;
        }

        if ((DateTime.Now - lastPoiRefresh).TotalMilliseconds > 200)
        {
            PoiLayer.DataHasChanged();
            lastPoiRefresh = DateTime.Now;
        }
    }

    public void LoadHeatMap(Map map, List<UserLocationLog> logs)
    {
        var features = new List<PointFeature>();

        foreach (var l in logs.Where((x, i) => i % 10 == 0).Take(300))
        {
            var spherical = SphericalMercator.FromLonLat(
                (double)l.Longitude,
                (double)l.Latitude);

            var f = new PointFeature(new MPoint(spherical.x, spherical.y))
            {
                Styles =
            {
                new SymbolStyle
                {
                    SymbolScale = 0.6,
                    Fill = new Brush(new Color(255, 0, 0, 80))
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

            pulseScale = pulseGrowing ? pulseScale + 0.04 : pulseScale - 0.04;

            if (pulseScale > 1.6) pulseGrowing = false;
            if (pulseScale < 1.0) pulseGrowing = true;

            if (Math.Abs(style.SymbolScale - pulseScale) > 0.05)
            {
                style.SymbolScale = pulseScale;
                if ((DateTime.Now - lastUserRefresh).TotalMilliseconds > 100)
                {
                    UserLayer.DataHasChanged();
                    lastUserRefresh = DateTime.Now;
                }
            }

            return true;
        });
    }
    private static readonly HttpClient http = new HttpClient();
    static MapViewModel()
    {
        http.Timeout = TimeSpan.FromSeconds(10);
    }
    public async Task<List<MPoint>> GetRoutePoints(Location from, Poi to)
    {
        var url =
            $"https://router.project-osrm.org/route/v1/driving/" +
            $"{from.Longitude.ToString(CultureInfo.InvariantCulture)}," +
            $"{from.Latitude.ToString(CultureInfo.InvariantCulture)};" +
            $"{to.Lng.ToString(CultureInfo.InvariantCulture)}," +
            $"{to.Lat.ToString(CultureInfo.InvariantCulture)}" +
            $"?overview=full&geometries=geojson";

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