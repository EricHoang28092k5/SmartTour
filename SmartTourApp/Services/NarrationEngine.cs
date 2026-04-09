using SmartTour.Services;
using SmartTour.Shared.Models;
using SmartTourApp.Data;
using SmartTourApp.Services;
using static SmartTour.Services.ApiService;

/// <summary>
/// NarrationEngine — Engine điều phối toàn bộ luồng phát thuyết minh.
///
/// ╔══════════════════════════════════════════════════════════════════╗
/// ║  FALLBACK LOGIC (Yêu cầu 2)                                     ║
/// ║  1. Nếu có mạng + AudioUrl → Stream Cloudinary                  ║
/// ║  2. Mất mạng nửa chừng → Auto-fallback sang TTS (không ngắt)   ║
/// ║  3. Offline hoặc không có AudioUrl → SQLite Script + TTS        ║
/// ║                                                                  ║
/// ║  PRIORITY QUEUE (Yêu cầu 3)                                     ║
/// ║  - POI gần hơn HOẶC Priority cao hơn → phát trước              ║
/// ║  - Interrupt: POI cao hơn ngắt POI thấp hơn đang phát          ║
/// ║  - Cooldown: 10 phút sau khi phát xong                         ║
/// ║                                                                  ║
/// ║  OFFLINE (Yêu cầu 4)                                            ║
/// ║  - Đổi ngôn ngữ → KHÔNG gọi API → query SQLite                 ║
/// ╚══════════════════════════════════════════════════════════════════╝
/// </summary>
public class NarrationEngine
{
    // ══════════════════════════════════════════════════════════════════
    // DEPENDENCIES
    // ══════════════════════════════════════════════════════════════════

    private readonly TtsService tts;
    private readonly AudioService audio;
    private readonly Database db;
    private readonly ApiService api;
    private readonly LanguageService lang;
    private readonly AudioListenTracker tracker;
    private readonly AudioCoordinator coordinator;
    private readonly OfflineSyncService offlineSync;

    // ══════════════════════════════════════════════════════════════════
    // STATE
    // ══════════════════════════════════════════════════════════════════

    /// <summary>Cache TTS scripts trong memory để tránh gọi API/SQLite liên tục.</summary>
    private readonly Dictionary<int, List<TtsDto>> ttsCache = new();

    /// <summary>Cooldown history: poiId → thời điểm phát gần nhất.</summary>
    private readonly Dictionary<int, DateTime> history = new();

    private readonly object lockObj = new();

    private CancellationTokenSource? cts;
    private Poi? currentPoi;
    private int? currentPriority;

    private const int COOLDOWN_MINUTES = 10;

    // ── Priority Queue (Yêu cầu 3): hàng đợi POI chờ phát ──
    private readonly PriorityQueue<QueuedPoi, int> _playQueue = new();
    private CancellationTokenSource? _queueProcessorCts;
    private bool _isQueueProcessing = false;

    // ══════════════════════════════════════════════════════════════════
    // CONSTRUCTOR
    // ══════════════════════════════════════════════════════════════════

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

    // ══════════════════════════════════════════════════════════════════
    // PUBLIC: AUTO PLAY (geofencing trigger)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Được gọi bởi TrackingService khi geofencing phát hiện POI mới.
    ///
    /// Logic ưu tiên (Yêu cầu 3):
    ///   - Nếu không có gì đang phát → phát ngay.
    ///   - Nếu POI mới có priority CAO HƠN → interrupt POI cũ, phát ngay.
    ///   - Nếu priority BẰNG hoặc THẤP HƠN → enqueue (chờ).
    ///   - Cooldown: nếu POI này mới phát < 10 phút → bỏ qua hoàn toàn.
    /// </summary>
    public async Task Play(Poi? poi, Location location, bool force = false)
    {
        if (poi == null) return;

        // ── Cooldown check (Yêu cầu 3) ──
        if (!force &&
            history.TryGetValue(poi.Id, out var lastPlayed) &&
            (DateTime.Now - lastPlayed).TotalMinutes < COOLDOWN_MINUTES)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Narration] COOLDOWN: POI={poi.Id} ({poi.Name}), " +
                $"remaining={(COOLDOWN_MINUTES - (DateTime.Now - lastPlayed).TotalMinutes):F1} min");
            return;
        }

        int newPriority = -(poi.Priority); // PriorityQueue dùng min-heap, nên negate

        lock (lockObj)
        {
            bool currentlyPlaying = currentPoi != null;
            int existingPriority = currentPriority ?? int.MaxValue;

            // ── Interrupt logic (Yêu cầu 3) ──
            // POI mới có priority CAO HƠN (số nhỏ hơn) → interrupt
            if (currentlyPlaying && poi.Priority < (currentPriority ?? int.MaxValue))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Narration] ⚡ INTERRUPT: New POI {poi.Id} " +
                    $"(priority={poi.Priority}) > current {currentPoi?.Id} " +
                    $"(priority={currentPriority})");

                // Force stop current
                cts?.Cancel();
            }
            else if (currentlyPlaying)
            {
                // Enqueue — phát sau khi POI hiện tại xong
                _playQueue.Enqueue(
                    new QueuedPoi { Poi = poi, Location = location },
                    poi.Priority);

                System.Diagnostics.Debug.WriteLine(
                    $"[Narration] 📋 QUEUED: POI={poi.Id} (priority={poi.Priority}), " +
                    $"queue size={_playQueue.Count}");
                return;
            }
        }

        coordinator.RequestPlay(AudioSource.Auto, () => StopInternal());

        await PlayInternalAsync(poi, location, AudioSource.Auto, force);

        // Sau khi play xong → xử lý queue
        await ProcessQueueAsync();
    }

    // ══════════════════════════════════════════════════════════════════
    // PUBLIC: MANUAL PLAY (User bấm từ HomePage)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Manual play từ HomePage — luôn thắng Auto, xóa queue.
    /// </summary>
    public async Task PlayManual(Poi poi, Location location)
    {
        if (poi == null) return;

        // Clear queue khi manual — user đã chọn POI cụ thể
        lock (lockObj)
        {
            _playQueue.Clear();
        }

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

    // ══════════════════════════════════════════════════════════════════
    // CORE: PLAY WITH FALLBACK (Yêu cầu 2)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Fallback chain:
    ///   1. Cloudinary Audio (nếu có mạng + AudioUrl)
    ///   2. Legacy POI.AudioUrl
    ///   3. SQLite Script + TTS (offline fallback)
    ///
    /// Xử lý lỗi mạng nửa chừng: nếu audio stream fail → switch sang TTS ngay.
    /// </summary>
    private async Task PlayWithFallbackAsync(TtsDto selected, Poi poi,
        Location location, CancellationToken token, AudioSource source)
    {
        bool played = false;

        // ── Ưu tiên 1: AudioUrl Cloudinary ──
        if (!string.IsNullOrWhiteSpace(selected.AudioUrl))
        {
            try
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Narration] ▶ Cloudinary: {selected.AudioUrl}");
                await audio.Play(selected.AudioUrl!, token);
                played = true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // Mạng mất nửa chừng → fallback sang TTS (Yêu cầu 2)
                System.Diagnostics.Debug.WriteLine(
                    $"[Narration] ⚠️ Cloudinary failed mid-stream: {ex.Message} " +
                    "→ switching to TTS fallback");
            }
        }

        // ── Ưu tiên 2: Legacy POI.AudioUrl ──
        if (!played && !string.IsNullOrWhiteSpace(poi.AudioUrl))
        {
            try
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Narration] ▶ Legacy AudioUrl: {poi.AudioUrl}");
                await audio.Play(poi.AudioUrl, token);
                played = true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Narration] Legacy audio failed: {ex.Message} → TTS fallback");
            }
        }

        // ── Ưu tiên 3: SQLite Script + TTS (Yêu cầu 2 & 4) ──
        if (!played)
        {
            var script = GetOfflineScript(poi, selected);

            if (!string.IsNullOrWhiteSpace(script) &&
                !token.IsCancellationRequested &&
                coordinator.IsActiveSource(source))
            {
                System.Diagnostics.Debug.WriteLine(
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

    // ══════════════════════════════════════════════════════════════════
    // PRIORITY QUEUE PROCESSOR (Yêu cầu 3)
    // ══════════════════════════════════════════════════════════════════

    private async Task ProcessQueueAsync()
    {
        lock (lockObj)
        {
            if (_isQueueProcessing || _playQueue.Count == 0) return;
            _isQueueProcessing = true;
        }

        try
        {
            while (true)
            {
                QueuedPoi? next;
                lock (lockObj)
                {
                    if (_playQueue.Count == 0)
                    {
                        _isQueueProcessing = false;
                        return;
                    }
                    next = _playQueue.Dequeue();
                }

                if (next == null) continue;

                // Cooldown check cho queued item
                if (history.TryGetValue(next.Poi.Id, out var lastPlay) &&
                    (DateTime.Now - lastPlay).TotalMinutes < COOLDOWN_MINUTES)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Narration] QUEUE: Skip cooldown POI={next.Poi.Id}");
                    continue;
                }

                System.Diagnostics.Debug.WriteLine(
                    $"[Narration] QUEUE: Playing next POI={next.Poi.Id} ({next.Poi.Name})");

                coordinator.RequestPlay(AudioSource.Auto, () => StopInternal());
                await PlayInternalAsync(next.Poi, next.Location, AudioSource.Auto, false);
            }
        }
        finally
        {
            lock (lockObj) { _isQueueProcessing = false; }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // INTERNAL PLAY
    // ══════════════════════════════════════════════════════════════════

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

        try
        {
            var scripts = await GetScripts(poi);
            var selected = SelectLang(scripts);
            if (selected == null) return;

            if (token.IsCancellationRequested) return;
            if (!coordinator.IsActiveSource(source)) return;

            tracker.StartSession(poi.Id, location.Latitude, location.Longitude);
            await PlayWithFallbackAsync(selected, poi, location, token, source);
        }
        catch (OperationCanceledException)
        {
            tracker.StopSession();
            coordinator.NotifyStop(source);
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

    // ══════════════════════════════════════════════════════════════════
    // SCRIPT RESOLUTION (Yêu cầu 2 & 4)
    // Ưu tiên: Memory cache → API (nếu có mạng) → SQLite (offline)
    // ══════════════════════════════════════════════════════════════════

    private async Task<List<TtsDto>> GetScripts(Poi poi)
    {
        // 1. Memory cache
        if (ttsCache.TryGetValue(poi.Id, out var cached))
            return cached;

        // 2. Thử API (nếu có mạng)
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
                System.Diagnostics.Debug.WriteLine(
                    $"[Narration] API failed, using SQLite: {ex.Message}");
            }
        }

        // 3. Fallback: SQLite offline (Yêu cầu 4 — Offline Translation Logic)
        var offlineScripts = offlineSync.GetAllLocalScripts(poi.Id);
        if (offlineScripts.Count > 0)
        {
            System.Diagnostics.Debug.WriteLine(
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

        // 4. Nothing found — return empty
        ttsCache[poi.Id] = new List<TtsDto>();
        return ttsCache[poi.Id];
    }

    /// <summary>
    /// Lấy script text để phát TTS — ưu tiên SQLite khi offline.
    /// </summary>
    private string GetOfflineScript(Poi poi, TtsDto selected)
    {
        // Nếu TtsDto đã có script → dùng luôn
        if (!string.IsNullOrWhiteSpace(selected.TtsScript))
            return selected.TtsScript;

        // Query SQLite (Yêu cầu 4)
        var local = offlineSync.GetLocalScript(poi.Id, lang.Current);
        if (local != null && !string.IsNullOrWhiteSpace(local.TtsScript))
            return local.TtsScript;

        // Fallback sang Description của POI
        return poi.Description ?? "";
    }

    // ══════════════════════════════════════════════════════════════════
    // PUBLIC CONTROLS
    // ══════════════════════════════════════════════════════════════════

    public void Stop()
    {
        lock (lockObj) { _playQueue.Clear(); }
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
            _playQueue.Clear();
            currentPoi = null;
            currentPriority = null;
        }

        cts?.Cancel();
        tracker.StopSession();
        audio.Stop();
        tts.Stop();
        coordinator.Reset();
    }

    // ══════════════════════════════════════════════════════════════════
    // HELPERS
    // ══════════════════════════════════════════════════════════════════

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

    // ══════════════════════════════════════════════════════════════════
    // INNER TYPES (Yêu cầu 3 — Priority Queue)
    // ══════════════════════════════════════════════════════════════════

    private class QueuedPoi
    {
        public Poi Poi { get; set; } = null!;
        public Location Location { get; set; } = null!;
    }
}
