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

#if ANDROID
    private readonly ExoPlayerService exo = new();
#endif

    private readonly string cacheDir = Path.Combine(FileSystem.AppDataDirectory, "tts_exo");

    public event Action<double>? OnProgress;
    public event Action<double>? OnDuration;
    public bool IsPlaying { get; private set; }

    public PoiDetailAudioManager(ApiService api, LanguageService lang)
    {
        this.api = api;
        this.lang = lang;

        if (!Directory.Exists(cacheDir))
            Directory.CreateDirectory(cacheDir);

#if ANDROID
        exo.OnProgress += p => OnProgress?.Invoke(p);
        exo.OnDuration += d => OnDuration?.Invoke(d);
#endif
    }

    public async Task Play(Poi poi)
    {
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
    }

    public void Resume()
    {
#if ANDROID
        exo.Resume();
#endif
        IsPlaying = true;
    }

    public void Pause()
    {
#if ANDROID
        exo.Pause();
#endif
        IsPlaying = false;
    }

    public void Seek(double sec)
    {
#if ANDROID
        exo.Seek(sec);
#endif
    }

    public void Stop()
    {
#if ANDROID
        exo.Stop();
#endif
        IsPlaying = false;
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