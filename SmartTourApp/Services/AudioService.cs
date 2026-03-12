using Plugin.Maui.Audio;
using System.Diagnostics;

namespace SmartTourApp.Services;

public class AudioService
{
    private readonly IAudioManager audioManager = AudioManager.Current;
    private IAudioPlayer? player;

    public async Task Play(string file)
    {
        try
        {
            player?.Stop();

            var stream = await FileSystem.OpenAppPackageFileAsync(file);

            player = audioManager.CreatePlayer(stream);

            player.Play();
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