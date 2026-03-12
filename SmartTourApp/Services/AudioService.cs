using Plugin.Maui.Audio;

namespace SmartTourApp.Services;

public class AudioService
{
    private readonly IAudioManager audioManager = AudioManager.Current;

    private IAudioPlayer? player;

    public async Task Play(string file)
    {
#if ANDROID
        try
        {
            player?.Stop();
            var stream = await FileSystem.OpenAppPackageFileAsync(file);
            player = audioManager.CreatePlayer(stream);
            player.Play();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Audio error: {ex.Message}");
        }
#else
    await Task.CompletedTask;
#endif
    }

    public void Stop()
    {
        player?.Stop();
    }
}