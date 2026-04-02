using Microsoft.Maui.Media;

namespace SmartTourApp.Services;

public class TtsService
{
    private CancellationTokenSource? internalCts;

    public async Task Speak(string text, string lang = "vi", CancellationToken externalToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        // 🔥 cancel cái đang nói
        internalCts?.Cancel();

        internalCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        var token = internalCts.Token;

        var locales = await TextToSpeech.Default.GetLocalesAsync();

        string targetLang = MapLang(lang);

        var locale = locales.FirstOrDefault(x =>
            x.Language.StartsWith(targetLang, StringComparison.OrdinalIgnoreCase));

        if (locale == null)
        {
            locale = locales.FirstOrDefault(x =>
                x.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        }

        var options = new SpeechOptions
        {
            Locale = locale,
            Pitch = 1.0f,
            Volume = 1.0f
        };

        try
        {
            await TextToSpeech.Default.SpeakAsync(text, options, token);
        }
        catch (OperationCanceledException)
        {
            // bị interrupt → OK
        }
    }

    public void Stop()
    {
        internalCts?.Cancel();
    }

    private string MapLang(string code)
    {
        return code switch
        {
            "vi" => "vi",
            "en" => "en",
            "ja" => "ja",
            "zh" => "zh",
            "ko" => "ko",
            _ => "en"
        };
    }
}