using SmartTour.Services;
using SmartTour.Shared.Models;
using SmartTourApp.Data;
using SmartTourApp.Services;
using static SmartTour.Services.ApiService;

public class NarrationEngine
{
    private readonly TtsService tts;
    private readonly AudioService audio;
    private readonly Database db;
    private readonly ApiService api;
    private readonly LanguageService lang;

    private readonly Dictionary<int, List<TtsDto>> ttsCache = new();
    private readonly Dictionary<int, DateTime> history = new();

    private readonly object lockObj = new();

    private CancellationTokenSource? cts;
    private Poi? currentPoi;

    private const int COOLDOWN_MINUTES = 10;

    public NarrationEngine(
        TtsService tts,
        AudioService audio,
        Database db,
        ApiService api,
        LanguageService lang)
    {
        this.tts = tts;
        this.audio = audio;
        this.db = db;
        this.api = api;
        this.lang = lang;
    }

    // 🔥 FIX: thêm force
    public async Task Play(Poi? poi, Location location, bool force = false)
    {
        if (poi == null) return;

        if (!force &&
            history.ContainsKey(poi.Id) &&
            (DateTime.Now - history[poi.Id]).TotalMinutes < COOLDOWN_MINUTES)
            return;

        lock (lockObj)
        {
            if (!force && currentPoi != null && currentPoi.Priority > poi.Priority)
                return;

            cts?.Cancel();
            cts = new CancellationTokenSource();
            currentPoi = poi;
        }

        var token = cts.Token;

        try
        {
            var scripts = await GetScripts(poi);
            var selected = SelectLang(scripts);

            if (selected == null) return;

            bool played = false;

            if (!string.IsNullOrWhiteSpace(poi.AudioUrl))
            {
                try
                {
                    await audio.Play(poi.AudioUrl, token);
                    played = true;
                }
                catch { }
            }

            if (!played && !string.IsNullOrWhiteSpace(selected.TtsScript))
            {
                await tts.Speak(selected.TtsScript, selected.LanguageCode, token);
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
        catch (OperationCanceledException) { }
    }

    public async Task PlayManual(Poi poi, Location location)
    {
        if (poi == null) return;

        cts?.Cancel();
        cts = new CancellationTokenSource();

        var token = cts.Token;

        try
        {
            var scripts = await GetScripts(poi);
            var selected = SelectLang(scripts);

            if (selected == null) return;

            bool played = false;

            if (!string.IsNullOrWhiteSpace(poi.AudioUrl))
            {
                try
                {
                    await audio.Play(poi.AudioUrl, token);
                    played = true;
                }
                catch { }
            }

            if (!played && !string.IsNullOrWhiteSpace(selected.TtsScript))
            {
                await tts.Speak(selected.TtsScript, selected.LanguageCode, token);
            }

            history[poi.Id] = DateTime.Now;
        }
        catch (OperationCanceledException) { }
    }

    public void Stop()
    {
        cts?.Cancel();
        audio.Stop();
        tts.Stop();
    }

    public void Reset()
    {
        history.Clear();
        ttsCache.Clear();
        currentPoi = null;

        cts?.Cancel();

        // 🔥 FIX
        audio.Stop();
        tts.Stop();
    }

    private async Task<List<TtsDto>> GetScripts(Poi poi)
    {
        if (!ttsCache.ContainsKey(poi.Id))
        {
            try
            {
                var scripts = await api.GetTtsScripts(poi.Id);
                ttsCache[poi.Id] = scripts ?? new();
            }
            catch
            {
                ttsCache[poi.Id] = new();
            }
        }

        return ttsCache[poi.Id];
    }

    private TtsDto? SelectLang(List<TtsDto> scripts)
    {
        var currentLang = lang.Current;

        return scripts
            .FirstOrDefault(x => x.LanguageCode.StartsWith(currentLang))
            ?? scripts.FirstOrDefault(x => x.LanguageCode.StartsWith("en"))
            ?? scripts.FirstOrDefault();
    }
}