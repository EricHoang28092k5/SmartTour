using Plugin.Maui.Audio;
using System.Diagnostics;

namespace SmartTourApp.Services;

public class AudioService
{
    private readonly IAudioManager audioManager = AudioManager.Current;
    private IAudioPlayer? player;

    private static readonly HttpClient http = new HttpClient();

    public async Task Play(string url)
    {
        try
        {
            player?.Stop();

            var stream = await http.GetStreamAsync(url);

            player = audioManager.CreatePlayer(stream);
            player.Play();

            while (player.IsPlaying)
            {
                await Task.Delay(300);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Audio error: {ex.Message}");
        }
    }

    public void Stop()
    {
        player?.Stop();
    }
}