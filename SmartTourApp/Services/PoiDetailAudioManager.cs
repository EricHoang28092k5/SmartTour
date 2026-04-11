using SmartTour.Services;
using SmartTour.Shared.Models;
using SmartTourApp.Services;

#if ANDROID
using SmartTourApp.Platforms.Android;
using Android.Speech.Tts;
using AndroidTTS = Android.Speech.Tts.TextToSpeech;
#endif

/// <summary>
/// PoiDetailAudioManager — Quản lý audio cho màn hình chi tiết POI.
///
/// Enhancement (Yêu cầu 2 + 3):
///   - Cloudinary stream → nếu fail nửa chừng → auto-fallback sang TTS
///   - Offline: đọc script từ OfflineSyncService (SQLite)
///   - Tracker chỉ ghi khi thực sự Playing (StartSession/PauseSession ghép đúng)
///   - Seek: PauseSession → engine.Seek → StartSession mới (không tính giây skip)
/// </summary>
public class PoiDetailAudioManager
{
    // ══════════════════════════════════════════════════════════════════
    // DEPENDENCIES
    // ══════════════════════════════════════════════════════════════════

    private readonly ApiService api;
    private readonly LanguageService lang;
    private readonly AudioListenTracker tracker;
    private readonly RouteTrackingService routeTracking;
    private readonly AudioCoordinator coordinator;
    private readonly AudioService audioService;
    private readonly OfflineSyncService offlineSync;
    private readonly TtsService ttsService;

#if ANDROID
    private readonly ExoPlayerService exo = new();
#endif

    private readonly string cacheDir = Path.Combine(FileSystem.AppDataDirectory, "tts_exo");

    // ══════════════════════════════════════════════════════════════════
    // EVENTS
    // ══════════════════════════════════════════════════════════════════

    public event Action<double>? OnProgress;
    public event Action<double>? OnDuration;

    /// <summary>Fired when audio completes naturally — UI should transition to Ended.</summary>
    public event Action? OnCompleted;

    // ══════════════════════════════════════════════════════════════════
    // STATE
    // ══════════════════════════════════════════════════════════════════

    public bool IsPlaying { get; private set; }

    private Poi? currentPoi;
    private double currentDuration;
    private double lastProgressSec;
    private Location? _playStartLocation;
    private bool _isStreamingCloudinary = false;
    private CancellationTokenSource? _streamCts;

    // ══════════════════════════════════════════════════════════════════
    // CONSTRUCTOR
    // ══════════════════════════════════════════════════════════════════

    public PoiDetailAudioManager(
        ApiService api,
        LanguageService lang,
        AudioListenTracker tracker,
        RouteTrackingService routeTracking,
        AudioCoordinator coordinator,
        AudioService audioService,
        OfflineSyncService offlineSync,
        TtsService ttsService)
    {
        this.api = api;
        this.lang = lang;
        this.tracker = tracker;
        this.routeTracking = routeTracking;
        this.coordinator = coordinator;
        this.audioService = audioService;
        this.offlineSync = offlineSync;
        this.ttsService = ttsService;

        if (!Directory.Exists(cacheDir))
            Directory.CreateDirectory(cacheDir);

#if ANDROID
        exo.OnProgress += p =>
        {
            lastProgressSec = p;
            OnProgress?.Invoke(p);

            // Detect natural completion (within 0.5s of end)
            if (currentDuration > 0 && p >= currentDuration - 0.5)
            {
                HandleCompleted();
            }
        };

        exo.OnDuration += d =>
        {
            currentDuration = d;
            OnDuration?.Invoke(d);
        };
#endif
    }

    // ══════════════════════════════════════════════════════════════════
    // PLAY — Entry point từ PoiDetailPage
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Bắt đầu phát audio cho POI.
    ///
    /// Fallback chain:
    ///   1. Cloudinary URL → ExoPlayer (Android) / AudioService
    ///   2. Mạng ngắt nửa chừng → TTS fallback không gián đoạn
    ///   3. Offline hoàn toàn → SQLite TtsScript → TTS
    /// </summary>
    public async Task Play(Poi poi, Location? userLocation = null)
    {
        currentPoi = poi;
        currentDuration = 0;
        lastProgressSec = 0;
        _playStartLocation = userLocation;
        _isStreamingCloudinary = false;

        // DetailManual luôn preempt Auto và HomeManual
        coordinator.RequestPlay(AudioSource.DetailManual, () =>
        {
            IsPlaying = false;
            tracker.StopSession();
            _streamCts?.Cancel();
            audioService.Stop();
#if ANDROID
            exo.Stop();
#endif
        });

        // Lấy scripts (online → SQLite fallback)
        var scripts = await GetScriptsWithFallbackAsync(poi);

        var selected = scripts.FirstOrDefault(x =>
            x.LanguageCode.StartsWith(lang.Current, StringComparison.OrdinalIgnoreCase))
            ?? scripts.FirstOrDefault();

        if (selected == null)
        {
            // Last resort: dùng description của POI
            var fallbackScript = poi.Description;
            if (!string.IsNullOrWhiteSpace(fallbackScript))
            {
                selected = new ApiService.TtsDto
                {
                    LanguageCode = lang.Current,
                    TtsScript = fallbackScript
                };
            }
            else return;
        }

        if (!coordinator.IsActiveSource(AudioSource.DetailManual)) return;

        // Ưu tiên 1: Cloudinary
        if (!string.IsNullOrWhiteSpace(selected.AudioUrl))
        {
            bool audioOk = await TryPlayCloudinaryAsync(selected.AudioUrl!, poi);
            if (!audioOk)
            {
                System.Diagnostics.Debug.WriteLine("[DetailAudio] Cloudinary failed → TTS fallback");
                await FallbackToTtsAsync(selected, poi);
            }
        }
        else
        {
            // Không có URL → TTS ngay
            await FallbackToTtsAsync(selected, poi);
        }

        // Route Tracking (kiểm tra radius)
        if (userLocation != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await routeTracking.OnManualAudioPlayedAsync(poi, userLocation);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[RouteTracking] error: {ex.Message}");
                }
            });
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // SCRIPT RESOLUTION — Online → SQLite fallback (Yêu cầu 4)
    // ══════════════════════════════════════════════════════════════════

    private async Task<List<ApiService.TtsDto>> GetScriptsWithFallbackAsync(Poi poi)
    {
        if (IsOnline())
        {
            try
            {
                var result = await api.GetTtsScripts(poi.Id);
                if (result != null && result.Count > 0)
                    return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[DetailAudio] API failed → SQLite: {ex.Message}");
            }
        }

        // SQLite offline (Yêu cầu 4)
        var localScripts = offlineSync.GetAllLocalScripts(poi.Id);
        if (localScripts.Count > 0)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[DetailAudio] 📦 SQLite: {localScripts.Count} scripts");

            return localScripts.Select(s => new ApiService.TtsDto
            {
                LanguageCode = s.LanguageCode,
                LanguageName = s.LanguageName,
                Title = s.Title,
                TtsScript = s.TtsScript,
                AudioUrl = s.AudioUrl
            }).ToList();
        }

        return new List<ApiService.TtsDto>();
    }

    // ══════════════════════════════════════════════════════════════════
    // INTERNAL PLAY HELPERS
    // ══════════════════════════════════════════════════════════════════

    private async Task<bool> TryPlayCloudinaryAsync(string url, Poi poi)
    {
        _isStreamingCloudinary = true;

        try
        {
#if ANDROID
            var cachedFile = await DownloadToCacheAsync(url, poi.Id, lang.Current);

            if (!coordinator.IsActiveSource(AudioSource.DetailManual))
            {
                _isStreamingCloudinary = false;
                return true; // preempted — not a failure
            }

            if (exo.IsPlaying) exo.Stop();
            exo.Play(cachedFile);
#else
            _streamCts?.Cancel();
            _streamCts = new CancellationTokenSource();
            await audioService.Play(url, _streamCts.Token);
#endif
            IsPlaying = true;
            // ✅ Chỉ bắt đầu tính log khi audio thực sự đang phát
            tracker.StartSession(poi.Id, 0, 0);
            return true;
        }
        catch (OperationCanceledException)
        {
            _isStreamingCloudinary = false;
            return true; // cancelled by user
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DetailAudio] Cloudinary failed: {ex.Message}");
            _isStreamingCloudinary = false;
            return false;
        }
    }

    private async Task FallbackToTtsAsync(ApiService.TtsDto selected, Poi poi)
    {
        if (!coordinator.IsActiveSource(AudioSource.DetailManual)) return;

        var script = string.IsNullOrWhiteSpace(selected.TtsScript)
            ? poi.Description
            : selected.TtsScript;

        if (string.IsNullOrWhiteSpace(script)) return;

#if ANDROID
        try
        {
            if (exo.IsPlaying) exo.Stop();
            var file = await GenerateAudio(script, poi.Id);

            if (!coordinator.IsActiveSource(AudioSource.DetailManual)) return;

            exo.Play(file);
            IsPlaying = true;
            // ✅ Bắt đầu tính log sau khi TTS generate xong và thực sự phát
            tracker.StartSession(poi.Id, 0, 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DetailAudio] TTS gen failed: {ex.Message}");
            await DeviceTtsSpeakAsync(script, selected.LanguageCode, poi);
        }
#else
        await DeviceTtsSpeakAsync(script, selected.LanguageCode, poi);
#endif
    }

    private async Task DeviceTtsSpeakAsync(string script, string langCode, Poi poi)
    {
        IsPlaying = true;
        // ✅ Tính log bắt đầu khi device TTS speak thực sự phát
        tracker.StartSession(poi.Id, 0, 0);

        var estimatedSec = script.Length / 5.0;
        OnDuration?.Invoke(estimatedSec);

        try
        {
            _streamCts?.Cancel();
            _streamCts = new CancellationTokenSource();
            await ttsService.Speak(script, langCode, _streamCts.Token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsPlaying = false;
            // ✅ Dừng tính log khi TTS kết thúc
            tracker.StopSession();
            coordinator.NotifyStop(AudioSource.DetailManual);
            OnCompleted?.Invoke();
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // PLAYBACK CONTROLS — Tên hàm giữ nguyên để không ảnh hưởng caller
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resume từ vị trí đã dừng.
    /// Mở phiên log mới — chỉ tính từ điểm resume.
    /// </summary>
    public void Resume()
    {
#if ANDROID
        exo.Resume();
#endif
        IsPlaying = true;

        // ✅ StartSession mới từ thời điểm resume (không tính thời gian đã pause)
        if (currentPoi != null)
            tracker.StartSession(currentPoi.Id, 0, 0);
    }

    /// <summary>
    /// Pause — đóng băng log, KHÔNG flush.
    /// </summary>
    public void Pause()
    {
#if ANDROID
        exo.Pause();
#endif
        _streamCts?.Cancel();
        IsPlaying = false;

        // ✅ Đóng băng log — snapshot thời gian tích lũy, KHÔNG ghi API
        tracker.PauseSession();
    }

    /// <summary>
    /// Seek đến vị trí mới.
    ///
    /// Quy trình:
    ///   1. tracker.OnSkip() → PauseSession() (snapshot, không tính giây skip)
    ///   2. engine.Seek(sec)
    ///   3. Caller sẽ Resume() → tracker.StartSession() từ vị trí mới
    /// </summary>
    public void Seek(double sec)
    {
        // ✅ Chốt log đoạn trước — không tính quãng bị skip
        tracker.OnSkip();

#if ANDROID
        exo.Seek(sec);
#endif
        // NOTE: Caller (PoiDetailPage) sẽ gọi Resume() sau Seek nếu cần
        // để StartSession() mới bắt đầu tính từ sec
    }

    /// <summary>
    /// Dừng hoàn toàn — flush log nếu đủ điều kiện.
    /// </summary>
    public void Stop()
    {
        _streamCts?.Cancel();
        audioService.Stop();
#if ANDROID
        exo.Stop();
#endif
        IsPlaying = false;
        _isStreamingCloudinary = false;

        // ✅ Flush log — ghi tổng thời gian nghe thực sự
        tracker.StopSession();
        coordinator.NotifyStop(AudioSource.DetailManual);

        currentPoi = null;
        currentDuration = 0;
        lastProgressSec = 0;
        _playStartLocation = null;
    }

    // ══════════════════════════════════════════════════════════════════
    // COMPLETION HANDLER — audio kết thúc tự nhiên
    // ══════════════════════════════════════════════════════════════════

    private void HandleCompleted()
    {
        IsPlaying = false;
        _isStreamingCloudinary = false;

        // ✅ Flush log đoạn cuối
        tracker.StopSession();
        coordinator.NotifyStop(AudioSource.DetailManual);

#if ANDROID
        exo.Stop();
        exo.Seek(0);
#endif

        OnProgress?.Invoke(0);
        OnCompleted?.Invoke();

        currentDuration = 0;
        lastProgressSec = 0;
        _playStartLocation = null;
    }

    // ══════════════════════════════════════════════════════════════════
    // DOWNLOAD HELPER
    // ══════════════════════════════════════════════════════════════════

    private static readonly HttpClient _httpClient = new();

    private async Task<string> DownloadToCacheAsync(string url, int poiId, string langCode)
    {
        var ext = Path.GetExtension(new Uri(url).AbsolutePath);
        if (string.IsNullOrEmpty(ext)) ext = ".mp3";

        var fileName = $"poi_{poiId}_{langCode}{ext}";
        var filePath = Path.Combine(cacheDir, fileName);

        if (File.Exists(filePath))
        {
            System.Diagnostics.Debug.WriteLine($"[DetailAudio] Cache hit: {fileName}");
            return filePath;
        }

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var bytes = await _httpClient.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(filePath, bytes);
                System.Diagnostics.Debug.WriteLine(
                    $"[DetailAudio] Downloaded: {fileName} ({bytes.Length / 1024}KB)");
                return filePath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[DetailAudio] Download attempt {attempt} failed: {ex.Message}");
                if (attempt < 3) await Task.Delay(500 * attempt);
            }
        }

        throw new Exception($"[DetailAudio] Download failed after 3 attempts: {url}");
    }

    // ══════════════════════════════════════════════════════════════════
    // TTS LOCAL GENERATOR (Android fallback offline)
    // ══════════════════════════════════════════════════════════════════

    private async Task<string> GenerateAudio(string text, int poiId)
    {
        var file = Path.Combine(cacheDir, $"poi_{poiId}_{lang.Current}.wav");
        if (File.Exists(file)) return file;

#if ANDROID
        var tcsInit = new TaskCompletionSource<bool>();
        var tcsDone = new TaskCompletionSource<bool>();

        var tts = new AndroidTTS(global::Android.App.Application.Context,
            new InitListener(status =>
            {
                if (status == OperationResult.Success)
                    tcsInit.TrySetResult(true);
                else
                    tcsInit.TrySetException(new Exception("TTS init failed"));
            }));

        await tcsInit.Task;

        tts.SetLanguage(Java.Util.Locale.ForLanguageTag(lang.Current switch
        {
            "vi" => "vi-VN",
            "en" => "en-US",
            "ja" => "ja-JP",
            "zh" => "zh-CN",
            "ko" => "ko-KR",
            _ => "en-US"
        }));

        tts.SetSpeechRate(0.9f);
        tts.SetPitch(1.0f);

        var javaFile = new Java.IO.File(file);

        tts.SetOnUtteranceProgressListener(new TtsProgressListener(() =>
            tcsDone.TrySetResult(true)));

        var bundle = new Android.OS.Bundle();
        bundle.PutString(AndroidTTS.Engine.KeyParamUtteranceId, "tts_id");
        tts.SynthesizeToFile(text, bundle, javaFile, "tts_id");

        await tcsDone.Task;
        tts.Stop();
        tts.Shutdown();
#endif

        return file;
    }

    // ══════════════════════════════════════════════════════════════════
    // HELPERS
    // ══════════════════════════════════════════════════════════════════

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
    // ANDROID INNER CLASSES
    // ══════════════════════════════════════════════════════════════════

#if ANDROID
    class InitListener : Java.Lang.Object, AndroidTTS.IOnInitListener
    {
        private readonly Action<OperationResult> _callback;
        public InitListener(Action<OperationResult> callback) => _callback = callback;
        public void OnInit(OperationResult status) => _callback(status);
    }

    class TtsProgressListener : UtteranceProgressListener
    {
        private readonly Action _onDone;
        public TtsProgressListener(Action onDone) => _onDone = onDone;
        public override void OnStart(string utteranceId) { }
        public override void OnDone(string utteranceId) => _onDone?.Invoke();
        public override void OnError(string utteranceId) { }
    }
#endif
}
