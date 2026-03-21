using CommunityToolkit.Maui.Views;
using System.Diagnostics;

namespace SmartTourApp.Services;

public class AudioService
{
    // MediaElement cần được gắn vào một View, nhưng ta có thể dùng nó như một Service ngầm
    private static MediaElement? _mediaElement;

    public void SetMediaElement(MediaElement mediaElement)
    {
        _mediaElement = mediaElement;
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