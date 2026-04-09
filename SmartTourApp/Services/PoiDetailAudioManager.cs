using SmartTour.Services;
using SmartTour.Shared.Models;
using SmartTourApp.Services;

#if ANDROID
using SmartTourApp.Platforms.Android;
using Android.Speech.Tts;
// Đặt biệt danh để không bị trùng với Microsoft.Maui.Media.TextToSpeech
using AndroidTTS = Android.Speech.Tts.TextToSpeech;
#endif

public class PoiDetailAudioManager
{
    private readonly ApiService api;
    private readonly LanguageService lang;
    private readonly AudioListenTracker tracker;
    private readonly RouteTrackingService routeTracking;
    private readonly AudioCoordinator coordinator;

#if ANDROID
    private readonly ExoPlayerService exo = new();
#endif

    // 🔥 AudioService để stream Cloudinary URL (dùng chung với NarrationEngine)
    private readonly AudioService audioService;

    private readonly string cacheDir = Path.Combine(FileSystem.AppDataDirectory, "tts_exo");

    public event Action<double>? OnProgress;
    public event Action<double>? OnDuration;

    /// <summary>
    /// Fired when audio completes naturally — UI should reset to beginning.
    /// </summary>
    public event Action? OnCompleted;

    public bool IsPlaying { get; private set; }

    private Poi? currentPoi;
    private double currentDuration;
    private double lastProgressSec;

    // Vị trí user lúc bấm Play (dùng để RouteTracking kiểm tra radius)
    private Location? _playStartLocation;

    // 🔥 Track nguồn đang phát để Stop/Resume đúng engine
    private bool _isStreamingCloudinary = false;
    private CancellationTokenSource? _streamCts;

    public PoiDetailAudioManager(
        ApiService api,
        LanguageService lang,
        AudioListenTracker tracker,
        RouteTrackingService routeTracking,
        AudioCoordinator coordinator,
        AudioService audioService)
    {
        this.api = api;
        this.lang = lang;
        this.tracker = tracker;
        this.routeTracking = routeTracking;
        this.coordinator = coordinator;
        this.audioService = audioService;

        if (!Directory.Exists(cacheDir))
            Directory.CreateDirectory(cacheDir);

#if ANDROID
        exo.OnProgress += p =>
        {
            lastProgressSec = p;
            OnProgress?.Invoke(p);

            // 🔥 Detect natural completion (within 0.5s of duration end)
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

    /// <summary>
    /// Play audio thủ công cho POI.
    /// Logic ưu tiên:
    ///   1. AudioUrl Cloudinary (nếu có wifi) → stream qua ExoPlayer (Android) hoặc AudioService
    ///   2. TtsScript → generate TTS local bằng Android TTS → ExoPlayer
    ///   3. Fallback device TTS speak
    /// </summary>
    /// <param name="poi">POI đang xem.</param>
    /// <param name="userLocation">Vị trí hiện tại — bắt buộc cho RouteTracking.</param>
    public async Task Play(Poi poi, Location? userLocation = null)
    {
        currentPoi = poi;
        currentDuration = 0;
        lastProgressSec = 0;
        _playStartLocation = userLocation;
        _isStreamingCloudinary = false;

        // ── DetailManual luôn thắng Auto và HomeManual ──
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

        // 🔥 Lấy track phù hợp ngôn ngữ từ API mới
        var scripts = await api.GetTtsScripts(poi.Id);

        var selected = scripts.FirstOrDefault(x =>
            x.LanguageCode.StartsWith(lang.Current))
            ?? scripts.FirstOrDefault();

        if (selected == null) return;

        // Kiểm tra lại sau await — có thể bị preempt
        if (!coordinator.IsActiveSource(AudioSource.DetailManual)) return;

        // ── 🔥 Ưu tiên 1: AudioUrl Cloudinary ──
        if (!string.IsNullOrWhiteSpace(selected.AudioUrl))
        {
            System.Diagnostics.Debug.WriteLine(
                $"[DetailAudio] ▶ Cloudinary: {selected.AudioUrl}");

            await PlayCloudinaryAsync(selected.AudioUrl!, poi);
        }
        else
        {
            // ── Ưu tiên 2: TtsScript → local TTS → ExoPlayer ──
            var script = string.IsNullOrWhiteSpace(selected.TtsScript)
                ? poi.Description
                : selected.TtsScript;

            if (string.IsNullOrWhiteSpace(script)) return;

            System.Diagnostics.Debug.WriteLine(
                $"[DetailAudio] ▶ TTS local: {selected.LanguageCode}");

            await PlayTtsLocalAsync(script, poi, selected.LanguageCode);
        }

        // ── Route Tracking ──
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
                    System.Diagnostics.Debug.WriteLine(
                        $"[RouteTracking] OnManualAudioPlayed error: {ex.Message}");
                }
            });
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // INTERNAL PLAY HELPERS
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Stream audio từ Cloudinary URL.
    /// Android: dùng ExoPlayer để có progress + seek + duration.
    /// Các nền tảng khác: dùng AudioService (download + play).
    /// </summary>
    private async Task PlayCloudinaryAsync(string url, Poi poi)
    {
        _isStreamingCloudinary = true;

#if ANDROID
        // ExoPlayer hỗ trợ HTTP stream trực tiếp — không cần download trước
        if (exo.IsPlaying) exo.Stop();

        // ExoPlayer play URL (nếu ExoPlayerService hỗ trợ URL, dùng luôn)
        // Nếu chỉ hỗ trợ file path, download về cache trước
        var cachedFile = await DownloadToCacheAsync(url, poi.Id, lang.Current);

        if (!coordinator.IsActiveSource(AudioSource.DetailManual)) return;

        exo.Play(cachedFile);
#else
        // Non-Android: stream bằng AudioService
        _streamCts?.Cancel();
        _streamCts = new CancellationTokenSource();

        try
        {
            await audioService.Play(url, _streamCts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DetailAudio] Stream failed: {ex.Message}");
            _isStreamingCloudinary = false;
            return;
        }
#endif

        IsPlaying = true;
        tracker.StartSession(poi.Id, 0, 0);
    }

    /// <summary>
    /// Generate audio từ TtsScript rồi phát bằng ExoPlayer (Android)
    /// hoặc device TTS speak (fallback).
    /// </summary>
    private async Task PlayTtsLocalAsync(string script, Poi poi, string langCode)
    {
#if ANDROID
        if (exo.IsPlaying) exo.Stop();

        var file = await GenerateAudio(script, poi.Id);

        if (!coordinator.IsActiveSource(AudioSource.DetailManual)) return;

        exo.Play(file);
        IsPlaying = true;
        tracker.StartSession(poi.Id, 0, 0);
#else
        // Non-Android: device TTS
        IsPlaying = true;
        tracker.StartSession(poi.Id, 0, 0);
        // Không có ExoPlayer — tự báo complete ngay (hoặc dùng TtsService nếu muốn)
        IsPlaying = false;
        tracker.StopSession();
        coordinator.NotifyStop(AudioSource.DetailManual);
        OnCompleted?.Invoke();
#endif
    }

    // ══════════════════════════════════════════════════════════════════
    // PLAYBACK CONTROLS — giữ nguyên tên hàm
    // ══════════════════════════════════════════════════════════════════

    public void Resume()
    {
#if ANDROID
        exo.Resume();
#endif
        IsPlaying = true;

        if (currentPoi != null)
            tracker.StartSession(currentPoi.Id, 0, 0);
    }

    public void Pause()
    {
#if ANDROID
        exo.Pause();
#endif
        _streamCts?.Cancel();   // nếu đang stream non-Android
        IsPlaying = false;
        tracker.PauseSession();
    }

    public void Seek(double sec)
    {
#if ANDROID
        exo.Seek(sec);
#endif
        tracker.OnSkip();

        if (IsPlaying && currentPoi != null)
            tracker.StartSession(currentPoi.Id, 0, 0);
    }

    public void Stop()
    {
        _streamCts?.Cancel();
        audioService.Stop();
#if ANDROID
        exo.Stop();
#endif
        IsPlaying = false;
        _isStreamingCloudinary = false;

        tracker.StopSession();
        coordinator.NotifyStop(AudioSource.DetailManual);

        currentPoi = null;
        currentDuration = 0;
        lastProgressSec = 0;
        _playStartLocation = null;
    }

    // ══════════════════════════════════════════════════════════════════
    // COMPLETION HANDLER
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called when ExoPlayer progress reaches end — audio completed naturally.
    /// </summary>
    private void HandleCompleted()
    {
        IsPlaying = false;
        _isStreamingCloudinary = false;

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
    // DOWNLOAD HELPER (cache Cloudinary MP3)
    // ══════════════════════════════════════════════════════════════════

    private static readonly HttpClient _httpClient = new();

    /// <summary>
    /// Download Cloudinary MP3 về local cache nếu chưa có.
    /// Cache key = poiId + langCode để invalidate đúng khi đổi ngôn ngữ.
    /// </summary>
    private async Task<string> DownloadToCacheAsync(string url, int poiId, string langCode)
    {
        // Dùng extension từ URL (mp3/wav/…)
        var ext = Path.GetExtension(new Uri(url).AbsolutePath);
        if (string.IsNullOrEmpty(ext)) ext = ".mp3";

        var fileName = $"poi_{poiId}_{langCode}{ext}";
        var filePath = Path.Combine(cacheDir, fileName);

        if (File.Exists(filePath))
        {
            System.Diagnostics.Debug.WriteLine(
                $"[DetailAudio] Cache hit: {fileName}");
            return filePath;
        }

        // 🔥 Download với retry
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var bytes = await _httpClient.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(filePath, bytes);

                System.Diagnostics.Debug.WriteLine(
                    $"[DetailAudio] Downloaded: {fileName} ({bytes.Length / 1024} KB)");
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
    // TTS LOCAL GENERATOR (Android — fallback khi offline)
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
        {
            tcsDone.TrySetResult(true);
        }));

        var bundle = new Android.OS.Bundle();
        bundle.PutString(AndroidTTS.Engine.KeyParamUtteranceId, "tts_id");

        tts.SynthesizeToFile(text, bundle, javaFile, "tts_id");

        await tcsDone.Task;

        tts.Stop();
        tts.Shutdown();
#endif

        return file;
    }

#if ANDROID
    class InitListener : Java.Lang.Object, AndroidTTS.IOnInitListener
    {
        private readonly Action<OperationResult> _callback;

        public InitListener(Action<OperationResult> callback)
        {
            _callback = callback;
        }

        public void OnInit(OperationResult status)
        {
            _callback(status);
        }
    }

    class TtsProgressListener : UtteranceProgressListener
    {
        private readonly Action _onDone;

        public TtsProgressListener(Action onDone)
        {
            _onDone = onDone;
        }

        public override void OnStart(string utteranceId) { }

        public override void OnDone(string utteranceId)
        {
            _onDone?.Invoke();
        }

        public override void OnError(string utteranceId) { }
    }
#endif
}
