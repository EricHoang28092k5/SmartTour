using Microsoft.Maui.Controls;
using SmartTour.Services;
using SmartTourApp.Services;
using SmartTourApp.Pages;

namespace SmartTourApp.Pages;

public partial class LoadingPage : ContentPage
{
    private readonly PoiRepository repo;
    private readonly TrackingService tracking;

    private double progress = 0;
    private CancellationTokenSource? animationCts;

    // YC3: Shine animation dùng timer thay vì Dispatcher để tránh block UI thread
    private System.Timers.Timer? _shineTimer;

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
        _shineTimer?.Stop();
        _shineTimer?.Dispose();
        _shineTimer = null;
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
            var offlineSync = services?.GetService<OfflineSyncService>();

            narration?.Reset();
            tracking.Stop();

            if (routeTracking != null)
                await routeTracking.RecoverSessionOnStartupAsync();

            await UpdateStatusAsync(loc?.LoadingPoi ?? "Đang tải địa điểm...", 0.25);

            var pois = await repo.GetPois();

            // YC4: Nếu offline, vẫn dùng SQLite cache — không cần báo lỗi
            if (pois.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine(
                    "[LoadingPage] No POIs from API/cache — continuing with empty list");
            }

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

            var api = services?.GetService<ApiService>();
            if (api != null)
            {
                try { await api.PostPresenceHeartbeatAsync(); }
                catch { /* keep startup resilient */ }
            }
            if (Application.Current is App app)
                app.StartPresenceHeartbeatTimer();

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
        // YC3: Tăng step size để giảm số vòng lặp (ít re-render hơn)
        const double step = 0.02;
        const int delayMs = 25;

        while (progress < target)
        {
            progress = Math.Min(progress + step, target);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ProgressBarContainer.WidthRequest = 300 * progress;
            });
            await Task.Delay(delayMs);
        }
    }

    // YC3: Logo animation - giảm từ vòng lặp tight → có delay hợp lý
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

                if (token.IsCancellationRequested) break;

                LogoBorder.Rotation = 0;
                LogoBorder.TranslationX = 0;
                LogoBorder.TranslationY = 0;
                await LogoBorder.ScaleToAsync(0.9, 600, Easing.SinInOut);

                // YC3: Thêm pause nhỏ giữa các vòng
                await Task.Delay(200, token);
            }
        }
        catch (TaskCanceledException) { }
        catch (OperationCanceledException) { }
    }

    // YC3: Shine animation dùng timer độc lập với interval 40ms thay vì 30ms
    private void StartShineAnimation(CancellationToken token)
    {
        _shineTimer?.Stop();
        _shineTimer?.Dispose();

        _shineTimer = new System.Timers.Timer(40); // YC3: 40ms thay vì 30ms
        _shineTimer.Elapsed += (_, _) =>
        {
            if (token.IsCancellationRequested)
            {
                _shineTimer?.Stop();
                return;
            }
            double offset = (DateTime.Now.TimeOfDay.TotalMilliseconds % 1500) / 1500.0;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ShineBox.TranslationX = 300 * offset - 60;
            });
        };
        _shineTimer.Start();
    }

    // YC3: Background animation - giữ nguyên nhưng cleanup đúng cách
    private async void StartBackgroundAnimation(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await ProgressBackground.FadeToAsync(0.6, 800, Easing.SinInOut);
                if (token.IsCancellationRequested) break;
                await ProgressBackground.FadeToAsync(1.0, 800, Easing.SinInOut);
            }
        }
        catch (TaskCanceledException) { }
        catch (OperationCanceledException) { }
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
