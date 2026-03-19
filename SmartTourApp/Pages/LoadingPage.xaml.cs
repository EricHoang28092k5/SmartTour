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
        StartBackgroundAnimation();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            double step = 0.25;

            // ✅ chạy preload song song (KHÔNG block UI)
            var loadPoiTask = Task.Run(() => repo.GetPois());

            // 1️⃣ Load POI (UI fake nhưng data chạy thật)
            await UpdateStatusAsync("Đang tải POI...", step);

            // ✅ đảm bảo data load xong trước khi qua bước tiếp
            await loadPoiTask;

            // 2️⃣ Preload TTS
            await UpdateStatusAsync("Khởi tạo TTS...", step * 2);

            // 3️⃣ Start Tracking (lúc này POI đã có cache → nhanh)
            StatusLabel.Text = "Khởi tạo Tracking...";
            await Task.Run(() => tracking.Start());
            await AnimateProgressTo(step * 3);

            // 4️⃣ Complete
            await UpdateStatusAsync("Hoàn tất!", 1.0);

            await this.FadeToAsync(0, 400);

            if (Application.Current?.Windows.Count > 0)
                Application.Current.Windows[0].Page = new AppShell();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
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
            progress += 0.01;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ProgressBarContainer.WidthRequest = 300 * progress;
            });
            await Task.Delay(20);
        }
    }

    private async void StartLogoAnimation()
    {
        while (true)
        {
            await Task.WhenAll(
                LogoBorder.ScaleToAsync(1.0, 600, Easing.SinInOut),
                LogoBorder.RotateToAsync(360, 2000, Easing.Linear)
            );

            LogoBorder.Rotation = 0;
            await LogoBorder.ScaleToAsync(0.9, 600, Easing.SinInOut);
        }
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
    private async void StartBackgroundAnimation()
    {
        while (true)
        {
            await ProgressBackground.FadeToAsync(0.6, 800, Easing.SinInOut);
            await ProgressBackground.FadeToAsync(1.0, 800, Easing.SinInOut);
        }
    }
}