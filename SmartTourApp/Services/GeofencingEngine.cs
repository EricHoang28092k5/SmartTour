using Microsoft.Maui.Devices.Sensors;
using SmartTour.Shared.Models;

namespace SmartTourApp.Services;

public class GeofencingEngine
{
    public Poi? FindBestPoi(Location user, List<Poi> pois)
    {
        Poi? best = null;

        double minDistance = double.MaxValue;

        foreach (var poi in pois)
        {
            var distanceKm =
                Location.CalculateDistance(
                    user.Latitude,
                    user.Longitude,
                    poi.Lat,
                    poi.Lng,
                    DistanceUnits.Kilometers);

            var meters = distanceKm * 1000;

            if (meters <= poi.Radius)
            {
                if (best == null ||
                    poi.Priority > best.Priority ||
                    meters < minDistance)
                {
                    best = poi;
                    minDistance = meters;
                }
            }
        }

        return best;
    }
}