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
        // ✅ cache sớm
        if (cachedPois != null && cachedPois.Count > 0)
            return cachedPois;

        try
        {
            var server = await api.GetPois();

            if (server != null && server.Count > 0)
            {
                // ✅ chạy DB ở background
                _ = Task.Run(() => db.AddPois(server));

                // ✅ KHÔNG copy nữa → dùng trực tiếp
                cachedPois = server;

                return cachedPois;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("API FAIL: " + ex.Message);
        }

        // ✅ load DB async để không block UI
        var local = await Task.Run(() => db.GetPois());

        cachedPois = local ?? new List<Poi>();

        return cachedPois;
    }

    public List<Poi>? GetCachedPois()
    {
        return cachedPois;
    }
}