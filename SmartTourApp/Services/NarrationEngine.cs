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

    private readonly object queueLock = new(); // 🔥 NEW

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

        lock (queueLock)
        {
            queue.Enqueue(poi);

            if (isPlaying)
                return;

            isPlaying = true;
        }

        _ = ProcessQueue(location); // 🔥 không await
    }

    private async Task ProcessQueue(Location location)
    {
        while (true)
        {
            Poi? poi;

            lock (queueLock)
            {
                if (queue.Count == 0)
                {
                    isPlaying = false;
                    return;
                }

                poi = queue.Dequeue();
            }

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

            var currentLang = lang.Current;

            var selected = scripts
                .FirstOrDefault(x => x.LanguageCode.StartsWith(currentLang))
                ?? scripts.FirstOrDefault(x => x.LanguageCode.StartsWith("en"))
                ?? scripts.FirstOrDefault();

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
    }
}