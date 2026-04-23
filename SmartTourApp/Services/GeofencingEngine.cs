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
        var candidates = new List<(Poi poi, double dist, int queryIndex)>();

        for (var i = 0; i < pois.Count; i++)
        {
            var poi = pois[i];
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

                candidates.Add((poi, meters, i));
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

        // 1) Ưu tiên POI Premium
        var premiumCandidates = candidates.Where(x => x.poi.IsPremium).ToList();
        var shortlisted = premiumCandidates.Count > 0 ? premiumCandidates : candidates;

        // 2) Nếu còn nhiều POI thì ưu tiên popularity (heatmap) cao nhất
        Dictionary<int, int> popularitySnapshot;
        lock (poiPopularity)
        {
            popularitySnapshot = new Dictionary<int, int>(poiPopularity);
        }

        var maxPopularity = shortlisted.Max(x => popularitySnapshot.TryGetValue(x.poi.Id, out var sum) ? sum : 0);
        var mostPopular = shortlisted
            .Where(x => (popularitySnapshot.TryGetValue(x.poi.Id, out var sum) ? sum : 0) == maxPopularity)
            .ToList();

        // 3) Nếu popularity bằng nhau thì chọn gần nhất
        var minDist = mostPopular.Min(x => x.dist);
        var closest = mostPopular
            .Where(x => Math.Abs(x.dist - minDist) <= DISTANCE_TIE_TOLERANCE_METERS)
            .ToList();

        // 4) Nếu vẫn bằng nhau thì lấy POI đầu tiên theo thứ tự query
        var best = closest
            .OrderBy(x => x.queryIndex)
            .Select(x => x.poi)
            .First();

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

    // 🔥 RESET giữ nguyên
    public void Reset()
    {
        activeZones.Clear();
        lastTrigger.Clear();
    }
}