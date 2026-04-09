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
    private readonly AudioCoordinator coordinator;

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
        AudioListenTracker tracker,
        AudioCoordinator coordinator)
    {
        this.tts = tts;
        this.audio = audio;
        this.db = db;
        this.api = api;
        this.lang = lang;
        this.tracker = tracker;
        this.coordinator = coordinator;
    }

    public async Task Play(Poi? poi, Location location, bool force = false)
    {
        if (poi == null) return;

        if (!force &&
            history.ContainsKey(poi.Id) &&
            (DateTime.Now - history[poi.Id]).TotalMinutes < COOLDOWN_MINUTES)
            return;

        coordinator.RequestPlay(AudioSource.Auto, () => StopInternal());

        CancellationTokenSource localCts;

        lock (lockObj)
        {
            if (!force && currentPoi != null && currentPoi.Priority > poi.Priority)
                return;

            cts?.Cancel();
            cts = new CancellationTokenSource();
            currentPoi = poi;
            localCts = cts;
        }

        var token = localCts.Token;

        try
        {
            var scripts = await GetScripts(poi);
            var selected = SelectLang(scripts);

            if (selected == null) return;

            if (token.IsCancellationRequested) return;
            if (!coordinator.IsActiveSource(AudioSource.Auto)) return;

            tracker.StartSession(poi.Id, location.Latitude, location.Longitude);

            bool played = false;

            // ── 🔥 Ưu tiên 1: AudioUrl từ Cloudinary (nếu có wifi) ──
            if (!string.IsNullOrWhiteSpace(selected.AudioUrl))
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Narration] ▶ Cloudinary audio: {selected.AudioUrl}");
                    await audio.Play(selected.AudioUrl!, token);
                    played = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Narration] Cloudinary play failed, fallback TTS: {ex.Message}");
                }
            }

            // ── Ưu tiên 2: audioUrl cũ trên POI object (legacy field) ──
            if (!played && !string.IsNullOrWhiteSpace(poi.AudioUrl))
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Narration] ▶ Legacy POI.AudioUrl: {poi.AudioUrl}");
                    await audio.Play(poi.AudioUrl, token);
                    played = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Narration] Legacy audio failed, fallback TTS: {ex.Message}");
                }
            }

            // ── Ưu tiên 3: TtsScript (offline — device TTS) ──
            if (!played && !string.IsNullOrWhiteSpace(selected.TtsScript))
            {
                if (!token.IsCancellationRequested &&
                    coordinator.IsActiveSource(AudioSource.Auto))
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Narration] ▶ TTS fallback: {selected.LanguageCode}");
                    await tts.Speak(selected.TtsScript, selected.LanguageCode, token);
                }
            }

            tracker.StopSession();
            coordinator.NotifyStop(AudioSource.Auto);

            history[poi.Id] = DateTime.Now;

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
            tracker.StopSession();
            coordinator.NotifyStop(AudioSource.Auto);
        }
    }

    public async Task PlayManual(Poi poi, Location location)
    {
        if (poi == null) return;

        coordinator.RequestPlay(AudioSource.HomeManual, () => StopInternal());

        CancellationTokenSource localCts;

        lock (lockObj)
        {
            cts?.Cancel();
            cts = new CancellationTokenSource();
            currentPoi = poi;
            localCts = cts;
        }

        var token = localCts.Token;

        try
        {
            var scripts = await GetScripts(poi);
            var selected = SelectLang(scripts);

            if (selected == null) return;
            if (token.IsCancellationRequested) return;

            tracker.StartSession(poi.Id, location.Latitude, location.Longitude);

            bool played = false;

            // ── 🔥 Ưu tiên 1: AudioUrl Cloudinary ──
            if (!string.IsNullOrWhiteSpace(selected.AudioUrl))
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Narration/Manual] ▶ Cloudinary audio: {selected.AudioUrl}");
                    await audio.Play(selected.AudioUrl!, token);
                    played = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Narration/Manual] Cloudinary failed, fallback: {ex.Message}");
                }
            }

            // ── Ưu tiên 2: Legacy POI.AudioUrl ──
            if (!played && !string.IsNullOrWhiteSpace(poi.AudioUrl))
            {
                try
                {
                    await audio.Play(poi.AudioUrl, token);
                    played = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Narration/Manual] Legacy audio failed: {ex.Message}");
                }
            }

            // ── Ưu tiên 3: TtsScript (offline) ──
            if (!played && !string.IsNullOrWhiteSpace(selected.TtsScript))
            {
                if (!token.IsCancellationRequested)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Narration/Manual] ▶ TTS fallback: {selected.LanguageCode}");
                    await tts.Speak(selected.TtsScript, selected.LanguageCode, token);
                }
            }

            tracker.StopSession();
            coordinator.NotifyStop(AudioSource.HomeManual);

            history[poi.Id] = DateTime.Now;
        }
        catch (OperationCanceledException)
        {
            tracker.StopSession();
            coordinator.NotifyStop(AudioSource.HomeManual);
        }
    }

    public void Stop()
    {
        StopInternal();
        coordinator.NotifyStop(AudioSource.Auto);
        coordinator.NotifyStop(AudioSource.HomeManual);
    }

    private void StopInternal()
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

        audio.Stop();
        tts.Stop();

        coordinator.Reset();
    }

    private async Task<List<TtsDto>> GetScripts(Poi poi)
    {
        if (!ttsCache.ContainsKey(poi.Id))
        {
            try
            {
                // 🔥 GetTtsScripts đã gọi API mới bên trong, trả về TtsDto có AudioUrl
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
