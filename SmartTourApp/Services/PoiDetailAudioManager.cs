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

#if ANDROID
    private readonly ExoPlayerService exo = new();
#endif

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

    public PoiDetailAudioManager(
        ApiService api,
        LanguageService lang,
        AudioListenTracker tracker,
        RouteTrackingService routeTracking)
    {
        this.api = api;
        this.lang = lang;
        this.tracker = tracker;
        this.routeTracking = routeTracking;

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
    /// </summary>
    /// <param name="poi">POI đang xem.</param>
    /// <param name="userLocation">
    /// Vị trí hiện tại của user — bắt buộc để RouteTracking kiểm tra có trong radius không.
    /// Truyền null nếu không lấy được GPS (sẽ bỏ qua route trigger).
    /// </param>
    public async Task Play(Poi poi, Location? userLocation = null)
    {
        currentPoi = poi;
        currentDuration = 0;
        lastProgressSec = 0;
        _playStartLocation = userLocation;

        var scripts = await api.GetTtsScripts(poi.Id);

        var selected = scripts.FirstOrDefault(x =>
            x.LanguageCode.StartsWith(lang.Current))
            ?? scripts.FirstOrDefault();

        if (selected == null) return;

#if ANDROID
        if (exo.IsPlaying)
            exo.Stop();

        var file = await GenerateAudio(selected.TtsScript, poi.Id);
        exo.Play(file);
#endif

        IsPlaying = true;

        // 🔥 Start listen tracking
        tracker.StartSession(poi.Id, 0, 0);

        // ── 🔥 Route Tracking: ghi nhận manual audio trigger ──
        // Chỉ trigger nếu có vị trí hợp lệ (không phải auto-play)
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

    public void Resume()
    {
#if ANDROID
        exo.Resume();
#endif
        IsPlaying = true;

        // 🔥 Resume tracking
        if (currentPoi != null)
            tracker.StartSession(currentPoi.Id, 0, 0);

        // Route tracking: resume không trigger lại (đã ghi nhận lúc Play ban đầu)
    }

    public void Pause()
    {
#if ANDROID
        exo.Pause();
#endif
        IsPlaying = false;

        // 🔥 Pause tracking — accumulates time
        tracker.PauseSession();
    }

    public void Seek(double sec)
    {
#if ANDROID
        exo.Seek(sec);
#endif
        // 🔥 On seek: pause accumulation (skip counts as pause)
        tracker.OnSkip();

        // If currently playing, restart the tracking clock
        if (IsPlaying && currentPoi != null)
            tracker.StartSession(currentPoi.Id, 0, 0);
    }

    public void Stop()
    {
#if ANDROID
        exo.Stop();
#endif
        IsPlaying = false;

        // 🔥 Flush listen duration
        tracker.StopSession();

        currentPoi = null;
        currentDuration = 0;
        lastProgressSec = 0;
        _playStartLocation = null;
    }

    /// <summary>
    /// Called when ExoPlayer progress reaches end — audio completed naturally.
    /// Resets position to start, fires OnCompleted so UI can reset play button.
    /// </summary>
    private void HandleCompleted()
    {
        IsPlaying = false;

        // 🔥 Flush listen session
        tracker.StopSession();

#if ANDROID
        exo.Stop();
        // Seek to 0 so next Play() starts fresh
        exo.Seek(0);
#endif

        // Reset progress for UI
        OnProgress?.Invoke(0);

        // Notify UI to reset play button
        OnCompleted?.Invoke();

        currentDuration = 0;
        lastProgressSec = 0;
        _playStartLocation = null;
    }

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
