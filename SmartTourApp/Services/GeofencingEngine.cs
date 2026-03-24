using SmartTour.Shared.Models;

public class GeofencingEngine
{
    private HashSet<int> activeZones = new();

    // Hàm này chỉ để tìm ông nào gần nhất để hiện lên UI
    public Poi? GetNearestPoi(Location user, List<Poi> pois)
    {
<<<<<<< HEAD
        return pois
            .Select(p => new { Poi = p, Distance = Location.CalculateDistance(user, new Location(p.Lat, p.Lng), DistanceUnits.Kilometers) * 1000 })
            .Where(x => x.Distance <= x.Poi.Radius)
            .OrderBy(x => x.Distance)
            .Select(x => x.Poi)
            .FirstOrDefault();
=======
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
>>>>>>> e91d1ab27c788503c01afd96e95d2391a9bdc9b0
    }

    // Hàm này để kiểm tra xem có ông nào "MỚI" vừa bước vào vùng không
    public bool IsNewZone(int poiId)
    {
        if (activeZones.Contains(poiId)) return false;
        activeZones.Add(poiId);
        return true;
    }

    // Xóa khỏi vùng khi đi ra xa
    public void LeaveZone(int poiId) => activeZones.Remove(poiId);
}