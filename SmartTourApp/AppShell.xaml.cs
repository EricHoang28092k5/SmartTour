using CommunityToolkit.Maui.Views;
using SmartTourApp.Services;

namespace SmartTourApp;

public partial class AppShell : Shell
{
    // Bạn phải thêm "AudioService audioService" vào trong ngoặc này
    public AppShell(AudioService audioService)
    {
        InitializeComponent();

        // Phải lấy từ Resources bằng Key đã đặt trong XAML
        if (Resources.TryGetValue("HiddenPlayer", out var player))
        {
            var mediaElement = (CommunityToolkit.Maui.Views.MediaElement)player;
            audioService.SetMediaElement(mediaElement);
        }
    }
}