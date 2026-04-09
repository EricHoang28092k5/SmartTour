using Microsoft.Maui.Controls;
using SmartTourApp.Services;
using SmartTourApp.Pages; // AutoPlayKey

namespace SmartTourApp.Pages;

public partial class LoadingPage : ContentPage
{
    private const string QrGateUntilKey = "qr_gate_until_utc";
    private readonly PoiRepository repo;
    private readonly TrackingService tracking;

    private double progress = 0;

    // 🔥 quản lý animation lifecycle
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

        // 🔥 kill animation cũ
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

        // 🔥 cực quan trọng để tránh UI bug
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

            narration?.Reset();
            tracking.Stop();

            // ── 🔥 Route session crash recovery — kiểm tra session cũ trước khi khởi động ──
            if (routeTracking != null)
            {
                await routeTracking.RecoverSessionOnStartupAsync();
            }

            await UpdateStatusAsync("Đang tải POI...", 0.25);

            var pois = await repo.GetPois();

            await UpdateStatusAsync("Khởi tạo TTS...", 0.5);

            StatusLabel.Text = "Khởi tạo Tracking...";
            await Task.Delay(300);

            await tracking.Start();

            // ─── AUTO PLAY + HEATMAP APP_OPEN (chạy song song) ───
            _ = Task.Run(async () =>
            {
                try
                {
                    if (locationService == null || geo == null || narration == null)
                        return;

                    Location? loc = null;

                    // 🔥 retry GPS
                    for (int i = 0; i < 5; i++)
                    {
                        loc = await locationService.GetLocation();
                        if (loc != null) break;
                        await Task.Delay(1500);
                    }

                    if (loc == null) return;

                    // ─── 🔥 HEATMAP APP_OPEN ───
                    if (heatmap != null)
                    {
                        await heatmap.CheckAppOpenAsync(loc, pois);
                    }

                    // ─── AUTO PLAY narration ───
                    // Yêu cầu 1: Chỉ auto-play nếu cài đặt auto-play đang BẬT
                    bool autoPlayEnabled = Preferences.Default.Get(
                        SettingsPage.AutoPlayKey, true);

                    if (!autoPlayEnabled)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            "⛔ AUTO PLAY DISABLED — bỏ qua auto play khi mở app");
                        return;
                    }

                    var poi = geo.FindBestPoi(loc, pois);
                    if (poi != null)
                    {
                        System.Diagnostics.Debug.WriteLine("🔥 AUTO PLAY: " + poi.Name);
                        // 🔥 Auto-play KHÔNG trigger RouteTracking (truyền null location vào PlayManual)
                        await narration.PlayManual(poi, loc);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("❌ No POI in radius");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Auto play / heatmap error: " + ex.Message);
                }
            });

            await AnimateProgressTo(0.75);
            await UpdateStatusAsync("Hoàn tất!", 1.0);

            await this.FadeToAsync(0, 400);
            // Nếu đã quét QR và còn hạn 7 ngày thì vào thẳng app.
            if (IsQrGateStillValid())
                Application.Current!.MainPage = new AppShell();
            else
                Application.Current!.MainPage = new QrGatePage();
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

    // =========================
    // 🔥 ANIMATION (ANTI BUG)
    // =========================

    private async void StartLogoAnimation(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                LogoBorder.Rotation = 0;

                await Task.WhenAll(
                    LogoBorder.ScaleToAsync(1.0, 600, Easing.SinInOut),
                    LogoBorder.RotateToAsync(360, 2000, Easing.Linear)
                );

                // 🔥 reset transform tránh lệch UI
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
        // 🔥 reset transform tránh lệch sau reset app
        LogoBorder.Rotation = 0;
        LogoBorder.Scale = 1;
        LogoBorder.TranslationX = 0;
        LogoBorder.TranslationY = 0;

        // progress
        progress = 0;
        ProgressBarContainer.WidthRequest = 0;

        // text
        StatusLabel.Text = "Đang khởi động...";
    }

    private static bool IsQrGateStillValid()
    {
        try
        {
            var text = Preferences.Default.Get(QrGateUntilKey, string.Empty);
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (!DateTime.TryParse(text, null, System.Globalization.DateTimeStyles.RoundtripKind, out var untilUtc))
                return false;
            return untilUtc > DateTime.UtcNow;
        }
        catch
        {
            return false;
        }
    }
}
