using System.Net.Http.Json;
using SmartTour.Shared.Models;

namespace SmartTour.Services;

public class ApiService
{
    private readonly HttpClient http;

    public ApiService(HttpClient http)
    {
        this.http = http;
    }

    public async Task<List<Poi>> GetPois()
    {
        try
        {
            var data = await http.GetFromJsonAsync<List<Poi>>("api/pois");
            return data ?? new List<Poi>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("API ERROR: " + ex.Message);
            throw;
        }
    }
}