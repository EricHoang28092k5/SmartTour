using Microsoft.Maui.Media;

namespace SmartTourApp.Services;

public class TtsService
{
    public async Task Speak(string text, string lang = "vi")
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var locales = await TextToSpeech.Default.GetLocalesAsync();

        var locale = locales.FirstOrDefault(x => x.Language.StartsWith(lang));

        var options = new SpeechOptions
        {
            Locale = locale,
            Pitch = 1.0f,
            Volume = 1.0f
        };

        await TextToSpeech.Default.SpeakAsync(text, options);
    }
}