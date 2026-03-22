using Plugin.Maui.Audio;
using System.Diagnostics;

namespace SmartTourApp.Services;

public class AudioService
{
    private readonly IAudioManager audioManager = AudioManager.Current;
    private IAudioPlayer? player;

    public async Task Play(string url)
    {
        try
        {
            player?.Stop();

            using var http = new HttpClient();
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