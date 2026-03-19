using SmartTour.Services;
using SmartTour.Shared.Models;
using SmartTourApp.Data;

namespace SmartTourApp.Services;

public class PoiRepository
{
    private readonly Database db;
    private readonly ApiService api;
    private List<Poi>? cachedPois;

    public PoiRepository(Database db, ApiService api)
    {
        this.db = db;
        this.api = api;
    }

    public async Task<List<Poi>> GetPois()
    {
        if (cachedPois != null && cachedPois.Count > 0)
            return cachedPois;

        try
        {
            var server = await api.GetPois();

            if (server != null && server.Count > 0)
            {
                db.AddPois(server); // ✅ bỏ Clear

                cachedPois = server.Select(p => new Poi
                {
                    Id = p.Id,
                    Name = p.Name,
                    Lat = p.Lat,
                    Lng = p.Lng,
                    Radius = p.Radius,
                    ImageUrl = p.ImageUrl,
                    Description = p.Description
                }).ToList();

                return cachedPois;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("API FAIL: " + ex.Message);
        }

        var local = db.GetPois();

        cachedPois = local;

        return local;
    }

    public List<Poi>? GetCachedPois()
    {
        return cachedPois;
    }
}