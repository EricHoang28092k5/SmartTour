using SmartTour.Services;
using SmartTour.Shared.Models;
using SmartTourApp.Data;
using static SmartTour.Services.ApiService;

namespace SmartTourApp.Services;

public class NarrationEngine
{
    private readonly TtsService tts;
    private readonly Database db;
    private readonly ApiService api;
    private readonly LanguageService lang;
    private readonly Dictionary<int, List<TtsDto>> ttsCache = new();

    private readonly Dictionary<int, DateTime> history = new();

    private readonly Queue<Poi> queue = new();

    private bool isPlaying;

    public NarrationEngine(
        TtsService tts,
        Database db,
        ApiService api,
        LanguageService lang)
    {
        this.tts = tts;
        this.db = db;
        this.api = api;
        this.lang = lang;
    }

    public async Task Play(Poi? poi, Location location)
    {
        if (poi == null)
            return;

        if (history.ContainsKey(poi.Id))
        {
            if ((DateTime.Now - history[poi.Id]).TotalMinutes < 10)
                return;
        }

        queue.Enqueue(poi);

        if (!isPlaying)
            await ProcessQueue(location);
    }

    private async Task ProcessQueue(Location location)
    {
        isPlaying = true;

        while (queue.Count > 0)
        {
            var poi = queue.Dequeue();
            List<TtsDto> scripts;

            if (!ttsCache.ContainsKey(poi.Id))
            {
                try
                {
                    scripts = await api.GetTtsScripts(poi.Id);
                    ttsCache[poi.Id] = scripts ?? new List<TtsDto>();
                }
                catch
                {
                    scripts = new List<TtsDto>();
                    ttsCache[poi.Id] = scripts;
                }
            }
            else
            {
                scripts = ttsCache[poi.Id];
            }
            if (scripts == null || scripts.Count == 0)
                continue;

            // 🔥 chọn ngôn ngữ
            var currentLang = lang.Current;

            var selected = scripts
                .FirstOrDefault(x => x.LanguageCode.StartsWith(currentLang))
                ?? scripts.FirstOrDefault(x => x.LanguageCode.StartsWith("en"))
                ?? scripts.FirstOrDefault();

            // 🔥 phát TTS
            if (selected != null && !string.IsNullOrWhiteSpace(selected.TtsScript))
            {
                await tts.Speak(selected.TtsScript, selected.LanguageCode);
            }

            history[poi.Id] = DateTime.Now;

            db.AddLog(new PlayLog
            {
                PoiId = poi.Id,
                Time = DateTime.Now,
                Lat = location.Latitude,
                Lng = location.Longitude
            });
        }

        isPlaying = false;
    }
}