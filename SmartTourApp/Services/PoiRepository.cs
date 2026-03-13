using SmartTour.Services;
using SmartTour.Shared.Models;
using SmartTourApp.Data;

namespace SmartTourApp.Services;

public class PoiRepository
{
    private readonly Database db;
    private readonly ApiService api;

    public PoiRepository(Database db, ApiService api)
    {
        this.db = db;
        this.api = api;
    }

    public async Task<List<Poi>> GetPois()
    {
        var local = db.GetPois();

        if (local.Count > 0)
            return local;

        var server = await api.GetPois();

        foreach (var poi in server)
        {
            db.AddPoi(poi);
        }

        return server;
    }
}