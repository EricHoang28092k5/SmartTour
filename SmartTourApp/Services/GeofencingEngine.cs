using Microsoft.Maui.Devices.Sensors;
using SmartTour.Shared.Models;

namespace SmartTourApp.Services;

public class GeofencingEngine
{
    private HashSet<int> activeZones = new();

    public Poi? FindBestPoi(Location user, List<Poi> pois)
    {
        var candidates = new List<(Poi poi, double dist)>();

        foreach (var poi in pois)
        {
            var meters =
                Location.CalculateDistance(
                    user,
                    new Location(poi.Lat, poi.Lng),
                    DistanceUnits.Kilometers) * 1000;

            if (meters <= poi.Radius)
            {
                candidates.Add((poi, meters));
            }
            else
            {
                activeZones.Remove(poi.Id);
            }
        }

        if (!candidates.Any())
            return null;

        var best = candidates
            .OrderByDescending(x => x.poi.Priority)
            .ThenBy(x => x.dist)
            .First().poi;

        if (!activeZones.Contains(best.Id))
        {
            activeZones.Add(best.Id);
            return best;
        }

        return null;
    }
}