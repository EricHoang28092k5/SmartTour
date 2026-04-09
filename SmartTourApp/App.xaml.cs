using SmartTourApp.Pages;
using SmartTourApp.Services;

namespace SmartTourApp;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var services = Application.Current?.Handler?.MauiContext?.Services;

        if (services == null)
            throw new Exception("DI container not ready");

        var poiRepo = services.GetService<PoiRepository>();
        var tracking = services.GetService<TrackingService>();

        if (poiRepo == null || tracking == null)
            throw new Exception("Services not registered");

        // ── Yêu cầu 5: Khởi động background sync ngay khi app mở ──
        var offlineSync = services.GetService<OfflineSyncService>();
        offlineSync?.StartBackgroundSync();

        var loadingPage = new LoadingPage(poiRepo, tracking);
        return new Window(loadingPage);
    }

    protected override void OnSleep()
    {
        base.OnSleep();

        var services = Current?.Handler?.MauiContext?.Services;
        services?.GetService<NarrationEngine>()?.Stop();

        var routeTracking = services?.GetService<RouteTrackingService>();
        if (routeTracking != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await routeTracking.FlushOnAppClosingAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[RouteTracking] OnSleep flush error: {ex.Message}");
                }
            });
        }

        // ── Yêu cầu 5: Force sync khi app đi vào background (tận dụng mạng) ──
        var offlineSync = services?.GetService<OfflineSyncService>();
        if (offlineSync != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await offlineSync.ForceSyncNowAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[OfflineSync] OnSleep sync error: {ex.Message}");
                }
            });
        }
    }

    protected override void OnResume()
    {
        base.OnResume();

        // ── Trigger sync ngay khi app được resume (mạng có thể vừa khôi phục) ──
        var services = Current?.Handler?.MauiContext?.Services;
        var offlineSync = services?.GetService<OfflineSyncService>();
        if (offlineSync != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2000); // đợi mạng ổn định
                    await offlineSync.ForceSyncNowAsync();
                }
                catch { }
            });
        }
    }
}
