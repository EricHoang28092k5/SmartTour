using Microsoft.Maui.Controls;
using SmartTourApp.Services;
using SmartTourApp.Pages;

namespace SmartTourApp.Pages;

public partial class LoadingPage : ContentPage
{
    private readonly PoiRepository repo;
    private readonly TrackingService tracking;

    private double progress = 0;
    private CancellationTokenSource? animationCts;

    public LoadingPage(PoiRepository repo, TrackingService tracking)
    {
        InitializeComponent();
        this.repo = repo;
        this.tracking = tracking;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        animationCts?.Cancel();
        animationCts = new CancellationTokenSource();

        ResetUI();

        StartLogoAnimation(animationCts.Token);
        StartShineAnimation(animationCts.Token);
        StartBackgroundAnimation(animationCts.Token);

        await InitAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        animationCts?.Cancel();
    }

    private async Task InitAsync()
    {
        try
        {
            var services = Application.Current?.Handler?.MauiContext?.Services;

            var narration = services?.GetService<NarrationEngine>();
            var locationService = services?.GetService<LocationService>();
            var geo = services?.GetService<GeofencingEngine>();
            var heatmap = services?.GetService<HeatmapService>();
            var routeTracking = services?.GetService<RouteTrackingService>();
            var coordinator = services?.GetService<AudioCoordinator>();
            var loc = services?.GetService<LocalizationService>();

            narration?.Reset();
            tracking.Stop();

            if (routeTracking != null)
                await routeTracking.RecoverSessionOnStartupAsync();

            await UpdateStatusAsync(loc?.LoadingPoi ?? "Đang tải địa điểm...", 0.25);

            var pois = await repo.GetPois();

            await UpdateStatusAsync(loc?.LoadingTts ?? "Khởi tạo TTS...", 0.5);

            StatusLabel.Text = loc?.LoadingTracking ?? "Khởi tạo Tracking...";
            await Task.Delay(300);

            await tracking.Start();

            _ = Task.Run(async () =>
            {
                try
                {
                    if (locationService == null || geo == null || narration == null) return;

                    Location? userLoc = null;
                    for (int i = 0; i < 5; i++)
                    {
                        userLoc = await locationService.GetLocation();
                        if (userLoc != null) break;
                        await Task.Delay(1500);
                    }

                    if (userLoc == null) return;

                    if (heatmap != null)
                        await heatmap.CheckAppOpenAsync(userLoc, pois);

                    bool autoPlayEnabled = Preferences.Default.Get(SettingsPage.AutoPlayKey, true);
                    if (!autoPlayEnabled)
                    {
                        System.Diagnostics.Debug.WriteLine("⛔ AUTO PLAY DISABLED");
                        return;
                    }

                    var poi = geo.FindBestPoi(userLoc, pois);
                    if (poi != null)
                    {
                        System.Diagnostics.Debug.WriteLine("🔥 AUTO PLAY: " + poi.Name);
                        await narration.PlayManual(poi, userLoc);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Auto play error: " + ex.Message);
                }
            });

            await AnimateProgressTo(0.75);
            await UpdateStatusAsync(loc?.LoadingDone ?? "Hoàn tất!", 1.0);

            await this.FadeToAsync(0, 400);
            Application.Current!.MainPage = new AppShell();
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

    private async void StartLogoAnimation(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                LogoBorder.Rotation = 0;
                await Task.WhenAll(
                    LogoBorder.ScaleToAsync(1.0, 600, Easing.SinInOut),
                    LogoBorder.RotateToAsync(360, 2000, Easing.Linear));
                LogoBorder.Rotation = 0;
                LogoBorder.TranslationX = 0;
                LogoBorder.TranslationY = 0;
                await LogoBorder.ScaleToAsync(0.9, 600, Easing.SinInOut);
            }
        }
        catch (TaskCanceledException) { }
    }

    private void StartShineAnimation(CancellationToken token)
    {
        Dispatcher.StartTimer(TimeSpan.FromMilliseconds(30), () =>
        {
            if (token.IsCancellationRequested) return false;
            double offset = (DateTime.Now.TimeOfDay.TotalMilliseconds % 1500) / 1500.0;
            ShineBox.TranslationX = 300 * offset - 60;
            return true;
        });
    }

    private async void StartBackgroundAnimation(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await ProgressBackground.FadeToAsync(0.6, 800, Easing.SinInOut);
                await ProgressBackground.FadeToAsync(1.0, 800, Easing.SinInOut);
            }
        }
        catch (TaskCanceledException) { }
    }

    private void ResetUI()
    {
        LogoBorder.Rotation = 0;
        LogoBorder.Scale = 1;
        LogoBorder.TranslationX = 0;
        LogoBorder.TranslationY = 0;
        progress = 0;
        ProgressBarContainer.WidthRequest = 0;
        StatusLabel.Text = "Đang khởi động...";
    }
}
