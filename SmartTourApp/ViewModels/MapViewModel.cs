using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using SmartTour.Shared.Models;

using Map = Mapsui.Map;
using Color = Mapsui.Styles.Color;
using Brush = Mapsui.Styles.Brush;

namespace SmartTourApp.ViewModels;

public class MapViewModel
{
    public MemoryLayer UserLayer = new();
    public MemoryLayer PoiLayer = new();

    public void UpdateUser(Map map, Location loc)
    {
        var spherical =
            SphericalMercator.FromLonLat(
                loc.Longitude,
                loc.Latitude);

        var feature = new PointFeature(
            new MPoint(spherical.x, spherical.y));

        feature.Styles.Add(new SymbolStyle
        {
            SymbolScale = 1.2,
            Fill = new Brush(Color.Red)
        });

        UserLayer.Features = new[] { feature };
        UserLayer.DataHasChanged();

        //map.Navigator.CenterOn(feature.Point);
    }

    public void LoadPois(Map map, List<Poi> pois)
    {
        var features = new List<PointFeature>();

        foreach (var poi in pois)
        {
            var spherical =
                SphericalMercator.FromLonLat(
                    poi.Lng,
                    poi.Lat);

            var f = new PointFeature(
                new MPoint(spherical.x, spherical.y));

            f.Styles.Add(new SymbolStyle
            {
                SymbolScale = 0.8,
                Fill = new Brush(Color.Blue)
            });

            features.Add(f);
        }

        PoiLayer.Features = features;

        // ⭐ dòng cực quan trọng
        PoiLayer.DataHasChanged();
    }
    public void HighlightPoi(double lat, double lng)
    {
        var spherical =
            SphericalMercator.FromLonLat(lng, lat);

        var point = new MPoint(spherical.x, spherical.y);

        var feature = new PointFeature(point);

        feature.Styles.Add(new SymbolStyle
        {
            SymbolScale = 1.2,
            Fill = new Brush(Color.Green)
        });

        PoiLayer.Features = PoiLayer.Features.Append(feature).ToList();
        PoiLayer.DataHasChanged();
    }
}