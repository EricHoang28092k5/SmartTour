using System.Diagnostics;
using SmartTour.Services;
using SmartTour.Shared.Models;
using SmartTourApp.Data;
using SmartTourApp.Services;
using static SmartTour.Services.ApiService;

/// <summary>
/// NarrationEngine — điều phối thuyết minh: FIFO cho auto (không cắt ngang),
/// cooldown, không trùng queue/current; user manual ưu tiên (hủy + xóa hàng đợi).
/// </summary>
public class NarrationEngine
{
    private readonly TtsService tts;
    private readonly AudioService audio;
    private readonly Database db;
    private readonly ApiService api;
    private readonly LanguageService lang;
    private readonly AudioListenTracker tracker;
    private readonly AudioCoordinator coordinator;
    private readonly OfflineSyncService offlineSync;

    private readonly Dictionary<int, List<TtsDto>> ttsCache = new();
    private readonly Dictionary<int, DateTime> history = new();

    private readonly object lockObj = new();

    private CancellationTokenSource? cts;
    private Poi? currentPoi;
    private int? currentPriority;

    private const int COOLDOWN_MINUTES = 5;

    private readonly Queue<QueuedPoi> _fifoQueue = new();
    private readonly HashSet<int> _queuedPoiIds = new();
    private bool _isQueueProcessing;

    public event EventHandler<NarrationCompletedEventArgs>? NarrationCompleted;

    public NarrationEngine(
        TtsService tts,
        AudioService audio,
        Database db,
        ApiService api,
        LanguageService lang,
        AudioListenTracker tracker,
        AudioCoordinator coordinator,
        OfflineSyncService offlineSync)
    {
        this.tts = tts;
        this.audio = audio;
        this.db = db;
        this.api = api;
        this.lang = lang;
        this.tracker = tracker;
        this.coordinator = coordinator;
        this.offlineSync = offlineSync;
    }

    /// <summary>
    /// Auto (geofence): không cắt ngang bản đang phát — xếp hàng FIFO; cooldown 5 phút;
    /// bỏ qua trùng current/queue; khi bỏ qua do cooldown vẫn báo <see cref="NarrationCompleted"/>.
    /// </summary>
    public async Task Play(Poi? poi, Location location, bool force = false)
    {
        if (poi == null) return;

        if (!force)
        {
            if (history.TryGetValue(poi.Id, out var lastPlayed) &&
                (DateTime.Now - lastPlayed).TotalMinutes < COOLDOWN_MINUTES)
            {
                Debug.WriteLine(
                    $"[Narration] COOLDOWN: POI={poi.Id} ({poi.Name}), " +
                    $"remaining={(COOLDOWN_MINUTES - (DateTime.Now - lastPlayed).TotalMinutes):F1} min");
                RaiseNarrationCompleted(poi.Id, location, NarrationCycleOutcome.SkippedCooldown);
                return;
            }

            lock (lockObj)
            {
                if (currentPoi?.Id == poi.Id)
                {
                    Debug.WriteLine($"[Narration] Skip duplicate (current): POI={poi.Id}");
                    return;
                }

                if (_queuedPoiIds.Contains(poi.Id))
                {
                    Debug.WriteLine($"[Narration] Skip duplicate (queued): POI={poi.Id}");
                    return;
                }

                if (currentPoi != null)
                {
                    _fifoQueue.Enqueue(new QueuedPoi { Poi = poi, Location = location });
                    _queuedPoiIds.Add(poi.Id);
                    Debug.WriteLine(
                        $"[Narration] QUEUED: POI={poi.Id}, queue size={_fifoQueue.Count}");
                    return;
                }
            }
        }

        coordinator.RequestPlay(AudioSource.Auto, () => StopInternal());

        await PlayInternalAsync(poi, location, AudioSource.Auto, force);
        await ProcessQueueAsync();
    }

    /// <summary>
    /// Manual: hủy token, xóa hàng đợi, chờ vòng xử lý cũ dừng (tối đa ~1s), phát ngay POI chọn.
    /// </summary>
    public async Task PlayManual(Poi poi, Location location)
    {
        if (poi == null) return;

        StopInternal();
        coordinator.NotifyStop(AudioSource.Auto);

        lock (lockObj)
        {
            while (_fifoQueue.Count > 0)
            {
                var n = _fifoQueue.Dequeue();
                _queuedPoiIds.Remove(n.Poi.Id);
            }
        }

        await WaitForNarrationIdleAsync(TimeSpan.FromMilliseconds(1000));

        coordinator.RequestPlay(AudioSource.HomeManual, () => StopInternal());

        CancellationTokenSource localCts;
        lock (lockObj)
        {
            cts?.Cancel();
            cts = new CancellationTokenSource();
            currentPoi = poi;
            currentPriority = poi.Priority;
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

            await PlayWithFallbackAsync(selected, poi, location, token,
                AudioSource.HomeManual);

            history[poi.Id] = DateTime.Now;
        }
        catch (OperationCanceledException)
        {
            tracker.StopSession();
            coordinator.NotifyStop(AudioSource.HomeManual);
        }
        finally
        {
            lock (lockObj)
            {
                if (currentPoi?.Id == poi.Id)
                {
                    currentPoi = null;
                    currentPriority = null;
                }
            }
        }
    }

    private async Task PlayWithFallbackAsync(TtsDto selected, Poi poi,
        Location location, CancellationToken token, AudioSource source)
    {
        bool played = false;

        if (!string.IsNullOrWhiteSpace(selected.AudioUrl))
        {
            try
            {
                Debug.WriteLine($"[Narration] ▶ Cloudinary: {selected.AudioUrl}");
                await audio.Play(selected.AudioUrl!, token);
                played = true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[Narration] ⚠️ Cloudinary failed mid-stream: {ex.Message} " +
                    "→ switching to TTS fallback");
            }
        }

        if (!played && !string.IsNullOrWhiteSpace(poi.AudioUrl))
        {
            try
            {
                Debug.WriteLine($"[Narration] ▶ Legacy AudioUrl: {poi.AudioUrl}");
                await audio.Play(poi.AudioUrl, token);
                played = true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[Narration] Legacy audio failed: {ex.Message} → TTS fallback");
            }
        }

        if (!played)
        {
            var script = GetOfflineScript(poi, selected);

            if (!string.IsNullOrWhiteSpace(script) &&
                !token.IsCancellationRequested &&
                coordinator.IsActiveSource(source))
            {
                Debug.WriteLine(
                    $"[Narration] ▶ TTS fallback ({selected.LanguageCode}): " +
                    $"{script[..Math.Min(50, script.Length)]}...");
                await tts.Speak(script, selected.LanguageCode, token);
            }
        }

        if (!token.IsCancellationRequested)
        {
            tracker.StopSession();
            coordinator.NotifyStop(source);

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

    private async Task ProcessQueueAsync()
    {
        while (true)
        {
            lock (lockObj)
            {
                if (_isQueueProcessing || _fifoQueue.Count == 0)
                    return;
                _isQueueProcessing = true;
            }

            try
            {
                while (true)
                {
                    QueuedPoi? next;
                    lock (lockObj)
                    {
                        if (_fifoQueue.Count == 0)
                            break;
                        next = _fifoQueue.Dequeue();
                        _queuedPoiIds.Remove(next.Poi.Id);
                    }

                    if (history.TryGetValue(next.Poi.Id, out var lastPlay) &&
                        (DateTime.Now - lastPlay).TotalMinutes < COOLDOWN_MINUTES)
                    {
                        Debug.WriteLine($"[Narration] QUEUE: Skip cooldown POI={next.Poi.Id}");
                        RaiseNarrationCompleted(
                            next.Poi.Id, next.Location, NarrationCycleOutcome.SkippedCooldown);
                        continue;
                    }

                    Debug.WriteLine(
                        $"[Narration] QUEUE: Playing next POI={next.Poi.Id} ({next.Poi.Name})");

                    coordinator.RequestPlay(AudioSource.Auto, () => StopInternal());
                    await PlayInternalAsync(next.Poi, next.Location, AudioSource.Auto, false);
                }
            }
            finally
            {
                lock (lockObj) { _isQueueProcessing = false; }
            }

            lock (lockObj)
            {
                if (_fifoQueue.Count == 0)
                    return;
            }
        }
    }

    private async Task PlayInternalAsync(Poi poi, Location location,
        AudioSource source, bool force)
    {
        CancellationTokenSource localCts;

        lock (lockObj)
        {
            cts?.Cancel();
            cts = new CancellationTokenSource();
            currentPoi = poi;
            currentPriority = poi.Priority;
            localCts = cts;
        }

        var token = localCts.Token;
        var intentionalCancel = false;
        var skipCompletionEvent = false;

        try
        {
            var scripts = await GetScripts(poi);
            var selected = SelectLang(scripts);
            if (selected == null)
            {
                if (source == AudioSource.Auto)
                    RaiseNarrationCompleted(poi.Id, location, NarrationCycleOutcome.Error);
                skipCompletionEvent = true;
                return;
            }

            if (token.IsCancellationRequested)
            {
                skipCompletionEvent = true;
                return;
            }

            if (!coordinator.IsActiveSource(source))
            {
                skipCompletionEvent = true;
                return;
            }

            tracker.StartSession(poi.Id, location.Latitude, location.Longitude);
            await PlayWithFallbackAsync(selected, poi, location, token, source);
        }
        catch (OperationCanceledException)
        {
            intentionalCancel = true;
            tracker.StopSession();
            coordinator.NotifyStop(source);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Narration] PlayInternalAsync error POI={poi.Id}: {ex.Message}");
            tracker.StopSession();
            coordinator.NotifyStop(source);
            if (source == AudioSource.Auto)
                RaiseNarrationCompleted(poi.Id, location, NarrationCycleOutcome.Error);
            skipCompletionEvent = true;
        }
        finally
        {
            lock (lockObj)
            {
                if (currentPoi?.Id == poi.Id)
                {
                    currentPoi = null;
                    currentPriority = null;
                }
            }
        }

        if (source == AudioSource.Auto && !intentionalCancel && !token.IsCancellationRequested &&
            !skipCompletionEvent)
            RaiseNarrationCompleted(poi.Id, location, NarrationCycleOutcome.Completed);
    }

    private async Task<List<TtsDto>> GetScripts(Poi poi)
    {
        if (ttsCache.TryGetValue(poi.Id, out var cached))
            return cached;

        bool isOnline = IsOnline();
        if (isOnline)
        {
            try
            {
                var scripts = await api.GetTtsScripts(poi.Id);
                if (scripts != null && scripts.Count > 0)
                {
                    ttsCache[poi.Id] = scripts;
                    return scripts;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Narration] API failed, using SQLite: {ex.Message}");
            }
        }

        var offlineScripts = offlineSync.GetAllLocalScripts(poi.Id);
        if (offlineScripts.Count > 0)
        {
            Debug.WriteLine(
                $"[Narration] 📦 Using SQLite scripts: {offlineScripts.Count} langs for POI {poi.Id}");

            var dtos = offlineScripts.Select(s => new TtsDto
            {
                LanguageCode = s.LanguageCode,
                LanguageName = s.LanguageName,
                Title = s.Title,
                TtsScript = s.TtsScript,
                AudioUrl = s.AudioUrl
            }).ToList();

            ttsCache[poi.Id] = dtos;
            return dtos;
        }

        ttsCache[poi.Id] = new List<TtsDto>();
        return ttsCache[poi.Id];
    }

    private string GetOfflineScript(Poi poi, TtsDto selected)
    {
        if (!string.IsNullOrWhiteSpace(selected.TtsScript))
            return selected.TtsScript;

        var local = offlineSync.GetLocalScript(poi.Id, lang.Current);
        if (local != null && !string.IsNullOrWhiteSpace(local.TtsScript))
            return local.TtsScript;

        return poi.Description ?? "";
    }

    public void Stop()
    {
        lock (lockObj)
        {
            while (_fifoQueue.Count > 0)
            {
                var n = _fifoQueue.Dequeue();
                _queuedPoiIds.Remove(n.Poi.Id);
            }
        }

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
        lock (lockObj)
        {
            while (_fifoQueue.Count > 0)
            {
                var n = _fifoQueue.Dequeue();
                _queuedPoiIds.Remove(n.Poi.Id);
            }

            currentPoi = null;
            currentPriority = null;
        }

        cts?.Cancel();
        tracker.StopSession();
        audio.Stop();
        tts.Stop();
        coordinator.Reset();
    }

    private TtsDto? SelectLang(List<TtsDto> scripts)
    {
        if (scripts.Count == 0) return null;

        return scripts
            .FirstOrDefault(x => x.LanguageCode.StartsWith(lang.Current,
                StringComparison.OrdinalIgnoreCase))
            ?? scripts.FirstOrDefault(x => x.LanguageCode.StartsWith("en",
                StringComparison.OrdinalIgnoreCase))
            ?? scripts.FirstOrDefault();
    }

    private static bool IsOnline()
    {
        try
        {
            var access = Connectivity.Current.NetworkAccess;
            return access == NetworkAccess.Internet ||
                   access == NetworkAccess.ConstrainedInternet;
        }
        catch { return false; }
    }

    private void RaiseNarrationCompleted(int poiId, Location location, NarrationCycleOutcome outcome)
    {
        var args = new NarrationCompletedEventArgs(poiId, location.Latitude, location.Longitude, outcome);
        try
        {
            NarrationCompleted?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Narration] NarrationCompleted handler error: {ex.Message}");
        }

        try
        {
            NarrationTelemetryBus.Publish(args);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Narration] NarrationTelemetryBus error: {ex.Message}");
        }
    }

    private async Task WaitForNarrationIdleAsync(TimeSpan maxWait)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < maxWait)
        {
            lock (lockObj)
            {
                if (!_isQueueProcessing && currentPoi == null)
                    return;
            }

            await Task.Delay(50);
        }
    }

    private class QueuedPoi
    {
        public Poi Poi { get; set; } = null!;
        public Location Location { get; set; } = null!;
    }
}
