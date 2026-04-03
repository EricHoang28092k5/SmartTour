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

            // ✅ Nếu nằm trong radius → add candidate
            if (meters <= poi.Radius)
            {
                // 🔥 check cooldown
                if (lastTrigger.ContainsKey(poi.Id))
                {
                    if ((DateTime.Now - lastTrigger[poi.Id]).TotalSeconds < COOLDOWN_SECONDS)
                        continue;
                }

                candidates.Add((poi, meters));
            }
            else
            {
                // 🔥 ra khỏi vùng thì remove active
                if (meters > poi.Radius * EXIT_BUFFER)
                    activeZones.Remove(poi.Id);
            }
        }

        if (!candidates.Any())
            return null;

        // =====================================================
        // 🔥 FIX CHÍNH: chọn POI gần nhất
        // =====================================================
        var minDist = candidates.Min(x => x.dist);

        var closestPois = candidates
            .Where(x => Math.Abs(x.dist - minDist) < 0.5) // tolerance 0.5m
            .Select(x => x.poi)
            .ToList();

        // 🔥 nếu nhiều POI bằng nhau → chọn random hoặc first
        var random = new Random();
        var best = closestPois.Count == 1
            ? closestPois.First()
            : closestPois[random.Next(closestPois.Count)];

        // =====================================================
        // 🔥 chỉ trigger nếu chưa active
        // =====================================================
        if (!activeZones.Contains(best.Id))
        {
            activeZones.Add(best.Id);
            lastTrigger[best.Id] = DateTime.Now;
            return best;
        }

        return null;
    }

    // 🔥 RESET giữ nguyên
    public void Reset()
    {
        activeZones.Clear();
        lastTrigger.Clear();
    }
}