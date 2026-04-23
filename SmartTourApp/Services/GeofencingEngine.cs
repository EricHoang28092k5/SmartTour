using Microsoft.Maui.Devices.Sensors;
using SmartTour.Shared.Models;

namespace SmartTourApp.Services;

public class GeofencingEngine
{
    private HashSet<int> activeZones = new();
    private Dictionary<int, DateTime> lastTrigger = new();
    private readonly Dictionary<int, int> poiPopularity = new();

    private const int COOLDOWN_SECONDS = 30;
    private const double EXIT_BUFFER = 1.2;
    private const double DISTANCE_TIE_TOLERANCE_METERS = 8.0;

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
            .Where(x => Math.Abs(x.dist - minDist) <= DISTANCE_TIE_TOLERANCE_METERS)
            .Select(x => x.poi)
            .ToList();

        var best = closestPois.Count == 1
            ? closestPois.First()
            : PickByPopularityThenRandom(closestPois);

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

    public void UpdatePoiPopularity(IDictionary<int, int> popularityByPoiId)
    {
        if (popularityByPoiId == null) return;
        lock (poiPopularity)
        {
            poiPopularity.Clear();
            foreach (var kv in popularityByPoiId)
            {
                poiPopularity[kv.Key] = kv.Value;
            }
        }
    }

    private Poi PickByPopularityThenRandom(List<Poi> tiedPois)
    {
        if (tiedPois.Count == 1) return tiedPois[0];

        Dictionary<int, int> popularitySnapshot;
        lock (poiPopularity)
        {
            popularitySnapshot = new Dictionary<int, int>(poiPopularity);
        }

        var maxPopularity = tiedPois.Max(p => popularitySnapshot.TryGetValue(p.Id, out var sum) ? sum : 0);
        var topPois = tiedPois
            .Where(p => (popularitySnapshot.TryGetValue(p.Id, out var sum) ? sum : 0) == maxPopularity)
            .ToList();

        if (topPois.Count == 1) return topPois[0];
        return topPois[Random.Shared.Next(topPois.Count)];
    }

    // 🔥 RESET giữ nguyên
    public void Reset()
    {
        activeZones.Clear();
        lastTrigger.Clear();
    }
}