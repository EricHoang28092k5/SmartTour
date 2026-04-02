using Microsoft.Maui.Devices.Sensors;
using SmartTour.Shared.Models;

namespace SmartTourApp.Services;

public class GeofencingEngine
{
    private HashSet<int> activeZones = new();
    private Dictionary<int, DateTime> lastTrigger = new();

    private const int COOLDOWN_SECONDS = 30;
    private const double EXIT_BUFFER = 1.2;

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
                if (lastTrigger.ContainsKey(poi.Id))
                {
                    if ((DateTime.Now - lastTrigger[poi.Id]).TotalSeconds < COOLDOWN_SECONDS)
                        continue;
                }

                candidates.Add((poi, meters));
            }
            else
            {
                if (meters > poi.Radius * EXIT_BUFFER)
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
            lastTrigger[best.Id] = DateTime.Now;
            return best;
        }

        return null;
    }

    // 🔥 FIX QUAN TRỌNG
    public void Reset()
    {
        activeZones.Clear();
        lastTrigger.Clear();
    }
}