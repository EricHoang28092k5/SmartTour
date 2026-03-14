using Microsoft.Maui.Devices.Sensors;
using SmartTour.Shared.Models;

namespace SmartTourApp.Services;

public class GeofencingEngine
{
    private HashSet<int> activeZones = new();

    public Poi? FindBestPoi(Location user, List<Poi> pois)
    {
        Poi? best = null;

        foreach (var poi in pois)
        {
            var meters =
                Location.CalculateDistance(
                    user,
                    new Location(poi.Lat, poi.Lng),
                    DistanceUnits.Kilometers) * 1000;

            if (meters <= poi.Radius)
            {
                if (!activeZones.Contains(poi.Id))
                {
                    activeZones.Add(poi.Id);
                    best = poi;
                }
            }
            else
            {
                activeZones.Remove(poi.Id);
            }
        }

        return best;
    }
}