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
    private readonly AudioListenTracker tracker;

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
        LanguageService lang,
        AudioListenTracker tracker)
    {
        this.tts = tts;
        this.audio = audio;
        this.db = db;
        this.api = api;
        this.lang = lang;
        this.tracker = tracker;
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

            // 🔥 Start tracking
            tracker.StartSession(poi.Id, location.Latitude, location.Longitude);

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

            // 🔥 Stop tracking — audio completed naturally
            tracker.StopSession();

            history[poi.Id] = DateTime.Now;

            // Legacy log (no duration — tracker handles the full log)
            db.AddLog(new PlayLog
            {
                PoiId = poi.Id,
                Time = DateTime.Now,
                Lat = location.Latitude,
                Lng = location.Longitude
            });
        }
        catch (OperationCanceledException)
        {
            // 🔥 Cancelled — flush whatever was accumulated
            tracker.StopSession();
        }
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

            // 🔥 Start tracking
            tracker.StartSession(poi.Id, location.Latitude, location.Longitude);

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

            // 🔥 Completed naturally
            tracker.StopSession();

            history[poi.Id] = DateTime.Now;
        }
        catch (OperationCanceledException)
        {
            tracker.StopSession();
        }
    }

    public void Stop()
    {
        cts?.Cancel();
        tracker.StopSession();
        audio.Stop();
        tts.Stop();
    }

    public void Reset()
    {
        history.Clear();
        ttsCache.Clear();
        currentPoi = null;

        cts?.Cancel();

        tracker.StopSession();

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
