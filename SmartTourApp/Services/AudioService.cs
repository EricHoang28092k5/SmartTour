using CommunityToolkit.Maui.Views;
using System.Diagnostics;

namespace SmartTourApp.Services;

public class AudioService
{
    // MediaElement cần được gắn vào một View, nhưng ta có thể dùng nó như một Service ngầm
    private static MediaElement? _mediaElement;

<<<<<<< HEAD
    public void SetMediaElement(MediaElement mediaElement)
    {
        _mediaElement = mediaElement;
=======
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
>>>>>>> e91d1ab27c788503c01afd96e95d2391a9bdc9b0
    }

    public async Task Play(string fileOrUrl)
    {
        if (_mediaElement == null)
        {
            Debug.WriteLine("Chưa khởi tạo MediaElement trong AppShell hoặc Page!");
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                _mediaElement.Stop();

                if (fileOrUrl.StartsWith("http"))
                {
                    _mediaElement.Source = MediaSource.FromUri(fileOrUrl);
                }
                else
                {
                    _mediaElement.Source = MediaSource.FromFile(fileOrUrl);
                }

                _mediaElement.Play();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Lỗi phát nhạc: {ex.Message}");
            }
        });
    }

    public void Stop() => _mediaElement?.Stop();
}