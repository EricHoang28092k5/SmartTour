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

    public async Task<List<TtsDto>> GetTtsScripts(int poiId)
    {
        var res = await http.GetFromJsonAsync<TtsResponse>($"api/pois/{poiId}/tts-all");
        return res?.Data ?? new List<TtsDto>();
    }

    /// <summary>
    /// Post a play log (listening duration) to the backend.
    /// Fire-and-forget friendly — caller should catch exceptions.
    /// </summary>
    public async Task PostPlayLog(PlayLog log)
    {
        await http.PostAsJsonAsync("api/pois/playlog", log);
    }
    public async Task<TourResponse?> GetTours()
    {
        try
        {
            var res = await http.GetFromJsonAsync<TourResponse>("api/tours");
            return res;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("API TOUR ERROR: " + ex.Message);
            throw;
        }
    }

    public class TtsResponse
    {
        public bool Success { get; set; }
        public int PoiId { get; set; }
        public List<TtsDto> Data { get; set; } = new();
    }

    public class TtsDto
    {
        public string LanguageCode { get; set; } = "";
        public string LanguageName { get; set; } = "";
        public string Title { get; set; } = "";
        public string TtsScript { get; set; } = "";
    }
}
