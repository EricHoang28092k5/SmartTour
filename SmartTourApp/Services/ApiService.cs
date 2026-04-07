using System.Net.Http.Json;
using SmartTour.Shared.Models;
using SmartTourApp.Services;

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

    // ══════════════════════════════════════════════════════════════════
    // 🔥 HEATMAP — ghi nhận lần user bước vào vùng radius của POI
    // POST /api/heatmap/entry
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Gửi 1 heatmap entry lên server.
    /// triggerType: "app_open" | "zone_enter"
    /// Server có thêm 1 lớp delay 5 phút nữa để chống spam từ nhiều thiết bị.
    /// </summary>
    public async Task PostHeatmapEntry(HeatmapEntryDto dto)
    {
        try
        {
            await http.PostAsJsonAsync("api/heatmap/entry", dto);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("HEATMAP API ERROR: " + ex.Message);
            // Không throw — heatmap là non-critical
        }
    }

    /// <summary>
    /// Lấy toàn bộ heatmap aggregated data (poiId + sum) để render trên map.
    /// GET /api/heatmap
    /// </summary>
    public async Task<HeatmapResponse?> GetHeatmap()
    {
        try
        {
            return await http.GetFromJsonAsync<HeatmapResponse>("api/heatmap");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("HEATMAP GET ERROR: " + ex.Message);
            return null;
        }
    }

    // ─── Response models ───────────────────────────────────────────────

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

    public class HeatmapResponse
    {
        public bool Success { get; set; }
        public int Total { get; set; }
        public List<HeatmapPoiData> Data { get; set; } = new();
    }

    public class HeatmapPoiData
    {
        public int PoiId { get; set; }
        public string PoiName { get; set; } = "";
        public double Lat { get; set; }
        public double Lng { get; set; }

        /// <summary>Tổng số lần user bước vào radius của POI này.</summary>
        public int Sum { get; set; }

        public int AppOpenCount { get; set; }
        public int ZoneEnterCount { get; set; }
        public DateTime LastRecordedAt { get; set; }
    }
}
