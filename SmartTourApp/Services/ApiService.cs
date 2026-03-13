using System.Net.Http.Json;
using SmartTour.Shared.Models;

namespace SmartTour.Services;

public class ApiService
{
    private readonly HttpClient http;

    public ApiService()
    {
        http = new HttpClient
        {
            BaseAddress = new Uri("https://10.0.2.2:7139")
        };
    }

    public async Task<List<Poi>> GetPois()
    {
        try
        {
            var data = await http.GetFromJsonAsync<List<Poi>>("/api/pois");
            return data ?? new List<Poi>();
        }
        catch
        {
            return new List<Poi>();
        }
    }
}