using Microsoft.Maui.Controls;
using SmartTourApp.Services;

namespace SmartTourApp.Pages;

public partial class LoadingPage : ContentPage
{
    private readonly PoiRepository repo;
    private readonly TrackingService tracking;
    private double progress = 0;

    public LoadingPage(PoiRepository repo, TrackingService tracking)
    {
        InitializeComponent();
        this.repo = repo;
        this.tracking = tracking;

        StartLogoAnimation();
        StartShineAnimation();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            double step = 0.25;

            // 1️⃣ Load POI
            await UpdateStatusAsync("Đang tải POI...", step);

            // 2️⃣ Preload TTS
            await UpdateStatusAsync("Khởi tạo TTS...", step * 2);

            // 3️⃣ Start Tracking
            StatusLabel.Text = "Khởi tạo Tracking...";
            await tracking.Start();
            await AnimateProgressTo(step * 3);

            // 4️⃣ Complete
            await UpdateStatusAsync("Hoàn tất!", 1.0);

            await this.FadeToAsync(0, 400);
            if (Application.Current?.Windows.Count > 0)
                Application.Current.Windows[0].Page = new AppShell();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }

    private async Task UpdateStatusAsync(string status, double target)
    {
        StatusLabel.Text = status;
        await AnimateProgressTo(target);
        await Task.Delay(300);
    }

    private async Task AnimateProgressTo(double target)
    {
        while (progress < target)
        {
            progress += 0.005;
            ProgressBarContainer.WidthRequest = 300 * progress;
            await Task.Delay(8);
        }
    }

    private void StartLogoAnimation()
    {
        this.Dispatcher.StartTimer(TimeSpan.FromMilliseconds(16), () =>
        {
            double t = DateTime.Now.TimeOfDay.TotalMilliseconds / 1000.0;
            LogoBorder.Scale = 0.9 + 0.05 * Math.Sin(t * 3);
            // Để không bị lỗi thời, có thể dùng RotateToAsync trong một Task riêng
            LogoBorder.Rotation += 0.8;
            return true;
        });
    }

    private void StartShineAnimation()
    {
        this.Dispatcher.StartTimer(TimeSpan.FromMilliseconds(30), () =>
        {
            double offset = (DateTime.Now.TimeOfDay.TotalMilliseconds % 1500) / 1500.0;
            ShineBox.TranslationX = 300 * offset - 60;
            return true;
        });
    }
}