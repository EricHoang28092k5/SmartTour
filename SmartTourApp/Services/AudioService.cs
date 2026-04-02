using Plugin.Maui.Audio;

public class AudioService
{
    private readonly IAudioManager audioManager = AudioManager.Current;
    private IAudioPlayer? player;

    private static readonly HttpClient http = new HttpClient();

    private readonly string cacheDir =
        Path.Combine(FileSystem.AppDataDirectory, "audio_cache");

    public AudioService()
    {
        if (!Directory.Exists(cacheDir))
            Directory.CreateDirectory(cacheDir);
    }

    public async Task Play(string url, CancellationToken token)
    {
        Stop();

        var filePath = await GetOrDownload(url, token);

        using var stream = File.OpenRead(filePath);

        player = audioManager.CreatePlayer(stream);
        player.Play();

        while (player.IsPlaying && !token.IsCancellationRequested)
        {
            await Task.Delay(200, token);
        }
    }

    public void Stop()
    {
        try
        {
            player?.Stop();
            player = null;
        }
        catch { }
    }

    private async Task<string> GetOrDownload(string url, CancellationToken token)
    {
        var fileName = Path.GetFileName(new Uri(url).AbsolutePath);
        var path = Path.Combine(cacheDir, fileName);

        if (File.Exists(path))
            return path;

        // 🔥 retry nhẹ
        for (int i = 0; i < 3; i++)
        {
            try
            {
                var bytes = await http.GetByteArrayAsync(url, token);
                await File.WriteAllBytesAsync(path, bytes, token);
                return path;
            }
            catch
            {
                await Task.Delay(500, token);
            }
        }

        throw new Exception("Download audio failed");
    }

    // 🔥 preload nearest only
    public async Task Preload(List<string> urls)
    {
        foreach (var url in urls.Take(5))
        {
            try
            {
                await GetOrDownload(url, CancellationToken.None);
            }
            catch { }
        }
    }
}