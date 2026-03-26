using Microsoft.Maui.Media;

namespace SmartTourApp.Services;

public class TtsService
{
    public async Task Speak(string text, string lang = "vi")
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var locales = await TextToSpeech.Default.GetLocalesAsync();

        string targetLang = MapLang(lang);

        var locale = locales.FirstOrDefault(x =>
            x.Language.StartsWith(targetLang, StringComparison.OrdinalIgnoreCase));

        // 🔥 fallback nếu không có locale
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

        await TextToSpeech.Default.SpeakAsync(text, options);
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