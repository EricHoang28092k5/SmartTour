using Microsoft.Maui.Devices.Sensors;
using SmartTour.Shared.Models; // Quan trọng nhất là dòng này

namespace SmartTourApp.Services;

public class GeofencingEngine
{
    public Poi? FindNearest(Location user, List<Poi> pois)
    {
        Poi? nearest = null;

        double min = double.MaxValue;

        foreach (var poi in pois)
        {
            var distanceKm =
                Location.CalculateDistance(
                    user.Latitude,
                    user.Longitude,
                    poi.Lat,
                    poi.Lng,
                    DistanceUnits.Kilometers);

            double distanceMeters = distanceKm * 1000;

            if (distanceMeters < poi.Radius && distanceMeters < min)
            {
                nearest = poi;
                min = distanceMeters;
            }
        }

        return nearest;
    }

    public Poi? GetNearest(Location user, List<Poi> pois)
    {
        Poi? nearest = null;

        double min = double.MaxValue;

        foreach (var poi in pois)
        {
            var distanceKm =
                Location.CalculateDistance(
                    user.Latitude,
                    user.Longitude,
                    poi.Lat,
                    poi.Lng,
                    DistanceUnits.Kilometers);

            double meters = distanceKm * 1000;

            if (meters < min)
            {
                min = meters;
                nearest = poi;
            }
        }

        return nearest;
    }
}