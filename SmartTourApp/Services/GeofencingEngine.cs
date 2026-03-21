using SmartTour.Shared.Models;

public class GeofencingEngine
{
    private HashSet<int> activeZones = new();

    // Hàm này chỉ để tìm ông nào gần nhất để hiện lên UI
    public Poi? GetNearestPoi(Location user, List<Poi> pois)
    {
        return pois
            .Select(p => new { Poi = p, Distance = Location.CalculateDistance(user, new Location(p.Lat, p.Lng), DistanceUnits.Kilometers) * 1000 })
            .Where(x => x.Distance <= x.Poi.Radius)
            .OrderBy(x => x.Distance)
            .Select(x => x.Poi)
            .FirstOrDefault();
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