using System.Net.Http.Json;
using SmartTour.Shared.Models;
using SmartTourApp.Services;

namespace SmartTour.Services;

public class ApiService
{
    private readonly HttpClient http;
    private readonly LanguageService languageService;

    public ApiService(HttpClient http, LanguageService languageService)
    {
        this.http = http;
        this.languageService = languageService;
    }

    public async Task<List<Poi>> GetPois()
    {
        try
        {
            var lang = (languageService.Current ?? "en").Trim().ToLowerInvariant();
            var data = await http.GetFromJsonAsync<List<Poi>>($"api/pois?lang={Uri.EscapeDataString(lang)}");
            return data ?? new List<Poi>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("API ERROR: " + ex.Message);
            throw;
        }
    }

    public async Task<List<Food>> GetFoodsByPoi(int poiId)
    {
        try
        {
            var lang = (languageService.Current ?? "en").Trim().ToLowerInvariant();
            var tick = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var res = await http.GetFromJsonAsync<FoodMenuResponse>($"api/foods/menu/{poiId}?lang={Uri.EscapeDataString(lang)}&t={tick}");
            if (res?.Success == true && res.Data != null)
                return res.Data;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FoodAPI] GetFoodsByPoi({poiId}) error: {ex.Message}");
        }

        return new List<Food>();
    }

    // ══════════════════════════════════════════════════════════════════
    // 🔥 AUDIO API MỚI — GET /api/audio/poi/{poiId}
    // Trả về audioUrl (Cloudinary) + ttsScript cho từng ngôn ngữ
    // Logic ưu tiên: audioUrl (wifi/online) → ttsScript (offline fallback)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Lấy danh sách audio tracks đầy đủ cho POI.
    /// Mỗi track có cả <c>AudioUrl</c> (Cloudinary MP3) lẫn <c>TtsScript</c> để fallback.
    /// </summary>
    public async Task<PoiAudioResponse?> GetPoiAudios(int poiId)
    {
        try
        {
            return await http.GetFromJsonAsync<PoiAudioResponse>($"api/audio/poi/{poiId}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioAPI] GetPoiAudios({poiId}) error: {ex.Message}");
            return null;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // BACKWARD-COMPAT — giữ nguyên signature để không ảnh hưởng file khác
    // Nội bộ đã gọi API mới và map sang TtsDto (có thêm AudioUrl)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// [Backward-compat] Giữ nguyên để NarrationEngine / PoiDetailAudioManager
    /// không cần sửa signature. Nội bộ gọi <see cref="GetPoiAudios"/> mới.
    /// TtsDto giờ có thêm <c>AudioUrl</c> — callers có thể dùng hoặc bỏ qua.
    /// </summary>
    public async Task<List<TtsDto>> GetTtsScripts(int poiId)
    {
        try
        {
            var res = await GetPoiAudios(poiId);
            if (res?.Data != null && res.Data.Count > 0)
            {
                return res.Data.Select(d => new TtsDto
                {
                    LanguageCode = d.LanguageCode,
                    LanguageName = d.LanguageName,
                    Title = d.Title,
                    TtsScript = d.TtsScript,
                    AudioUrl = d.AudioUrl
                }).ToList();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioAPI] GetTtsScripts({poiId}) error: {ex.Message}");
        }

        return new List<TtsDto>();
    }

    /// <summary>
    /// Post a play log (listening duration) to the backend.
    /// Fire-and-forget friendly — caller should catch exceptions.
    /// </summary>
    public async Task PostPlayLog(PlayLog log)
    {
        await http.PostAsJsonAsync("api/pois/playlog", log);
    }

    // ══════════════════════════════════════════════════════════════════
    // HEATMAP
    // ══════════════════════════════════════════════════════════════════

    public async Task PostHeatmapEntry(HeatmapEntryDto dto)
    {
        try
        {
            await http.PostAsJsonAsync("api/heatmap/entry", dto);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("HEATMAP API ERROR: " + ex.Message);
        }
    }

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

    // ══════════════════════════════════════════════════════════════════
    // ROUTE TRACKING
    // ══════════════════════════════════════════════════════════════════

    public async Task PostRouteSession(RouteSessionDto dto)
    {
        await http.PostAsJsonAsync("api/routes/session", dto);
    }

    public async Task<RouteAnalyticsResponse?> GetPopularRoutes()
    {
        try
        {
            return await http.GetFromJsonAsync<RouteAnalyticsResponse>("api/routes/popular");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("ROUTE API ERROR: " + ex.Message);
            return null;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // RESPONSE / DTO MODELS
    // ══════════════════════════════════════════════════════════════════

    /// <summary>Response của GET /api/audio/poi/{id}</summary>
    public class PoiAudioResponse
    {
        public bool Success { get; set; }
        public int PoiId { get; set; }
        public int Total { get; set; }
        public int WithAudio { get; set; }
        public List<AudioTrackDto> Data { get; set; } = new();
    }

    /// <summary>Một ngôn ngữ trong response audio mới.</summary>
    public class AudioTrackDto
    {
        public int TranslationId { get; set; }
        public string LanguageCode { get; set; } = "";
        public string LanguageName { get; set; } = "";
        public string Title { get; set; } = "";
        public string TtsScript { get; set; } = "";
        /// <summary>Cloudinary URL — null nếu chưa generate server-side.</summary>
        public string? AudioUrl { get; set; }
    }

    /// <summary>
    /// TtsDto mở rộng — thêm <c>AudioUrl</c> nhưng vẫn backward-compatible.
    /// </summary>
    public class TtsDto
    {
        public string LanguageCode { get; set; } = "";
        public string LanguageName { get; set; } = "";
        public string Title { get; set; } = "";
        public string TtsScript { get; set; } = "";
        /// <summary>🔥 Mới: Cloudinary MP3 URL. Null nếu chưa có.</summary>
        public string? AudioUrl { get; set; }
    }

    public class TtsResponse
    {
        public bool Success { get; set; }
        public int PoiId { get; set; }
        public List<TtsDto> Data { get; set; } = new();
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
        public int Sum { get; set; }
        public int AppOpenCount { get; set; }
        public int ZoneEnterCount { get; set; }
        public DateTime LastRecordedAt { get; set; }
    }

    public class RouteAnalyticsResponse
    {
        public bool Success { get; set; }
        public int Total { get; set; }
        public List<PopularRouteData> Data { get; set; } = new();
    }

    public class FoodMenuResponse
    {
        public bool Success { get; set; }
        public List<Food> Data { get; set; } = new();
        public string? Message { get; set; }
    }

    public class PopularRouteData
    {
        public string PoiSequence { get; set; } = "";
        public int Count { get; set; }
        public double AvgDurationMinutes { get; set; }
    }
}
