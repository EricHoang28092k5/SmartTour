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
        try
        {
            var server = await api.GetPois();

            if (server != null && server.Count > 0)
            {
                db.ClearPois();
                db.AddPois(server);
                return server;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("API FAIL: " + ex.Message);
        }

        // fallback local
        var local = db.GetPois();

        System.Diagnostics.Debug.WriteLine($"LOCAL POI COUNT: {local.Count}");

        return local;
    }
}