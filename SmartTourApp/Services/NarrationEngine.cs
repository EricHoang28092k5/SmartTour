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

    // 🔥 FIX: thêm force
    public async Task Play(Poi? poi, Location location, bool force = false)
    {
        if (poi == null) return;

        if (!force &&
            history.ContainsKey(poi.Id) &&
            (DateTime.Now - history[poi.Id]).TotalMinutes < COOLDOWN_MINUTES)
            return;

        // ── Đăng ký với coordinator; nếu có nguồn khác đang phát, nó sẽ bị stop ──
        // Auto-play: đăng ký AudioSource.Auto
        // Callback stop của chính mình để khi bị preempt coordinator gọi được
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

            // Kiểm tra lại sau await — có thể bị preempt bởi manual play trong lúc fetch scripts
            if (token.IsCancellationRequested) return;
            if (!coordinator.IsActiveSource(AudioSource.Auto)) return;

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
                if (!token.IsCancellationRequested &&
                    coordinator.IsActiveSource(AudioSource.Auto))
                {
                    await tts.Speak(selected.TtsScript, selected.LanguageCode, token);
                }
            }

            // 🔥 Stop tracking — audio completed naturally
            tracker.StopSession();
            coordinator.NotifyStop(AudioSource.Auto);

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
            // 🔥 Cancelled (bị preempt hoặc stop thủ công) — flush accumulated time
            tracker.StopSession();
            coordinator.NotifyStop(AudioSource.Auto);
        }
    }

    public async Task PlayManual(Poi poi, Location location)
    {
        if (poi == null) return;

        // ── Manual từ HomePage luôn thắng auto-play ──
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
                if (!token.IsCancellationRequested)
                    await tts.Speak(selected.TtsScript, selected.LanguageCode, token);
            }

            // 🔥 Completed naturally
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

    // Dùng nội bộ để không double-notify coordinator
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

        // 🔥 FIX
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
