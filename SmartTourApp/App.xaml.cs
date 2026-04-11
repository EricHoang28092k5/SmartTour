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

        // ── Interruption handling (Yêu cầu 2 - Audio) ──
        // Thông báo PoiDetailPage để tự Pause + chốt log session
        NotifyPoiDetailPageSleep(services);

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

        // ── Yêu cầu 5: Force sync khi app đi vào background ──
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

        var services = Current?.Handler?.MauiContext?.Services;

        // ── Interruption resume: giữ Paused state, KHÔNG tự play lại ──
        NotifyPoiDetailPageResume(services);

        // ── Trigger sync ngay khi app được resume ──
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

    // ══════════════════════════════════════════════════════════════════
    // INTERRUPTION HELPERS — tìm PoiDetailPage hiện tại và notify
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tìm PoiDetailPage đang active trong navigation stack và gọi HandleAppSleep().
    /// Đảm bảo audio Pause + log session được chốt ngay khi có cuộc gọi / thoát app.
    /// </summary>
    private static void NotifyPoiDetailPageSleep(IServiceProvider? services)
    {
        try
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var shell = Shell.Current;
                if (shell == null) return;

                // Tìm trong navigation stack của ShellSection hiện tại
                var currentSection = shell.CurrentItem?.CurrentItem as ShellSection;
                var navStack = currentSection?.Navigation?.NavigationStack;

                if (navStack == null) return;

                foreach (var page in navStack)
                {
                    if (page is PoiDetailPage poiPage)
                    {
                        poiPage.HandleAppSleep();
                        System.Diagnostics.Debug.WriteLine(
                            "[App] OnSleep → PoiDetailPage.HandleAppSleep() called");
                        break;
                    }
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[App] NotifyPoiDetailPageSleep error: {ex.Message}");
        }
    }

    /// <summary>
    /// Tìm PoiDetailPage đang active và gọi HandleAppResume().
    /// Giữ Paused state — không tự play lại.
    /// </summary>
    private static void NotifyPoiDetailPageResume(IServiceProvider? services)
    {
        try
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var shell = Shell.Current;
                if (shell == null) return;

                var currentSection = shell.CurrentItem?.CurrentItem as ShellSection;
                var navStack = currentSection?.Navigation?.NavigationStack;

                if (navStack == null) return;

                foreach (var page in navStack)
                {
                    if (page is PoiDetailPage poiPage)
                    {
                        poiPage.HandleAppResume();
                        System.Diagnostics.Debug.WriteLine(
                            "[App] OnResume → PoiDetailPage.HandleAppResume() called");
                        break;
                    }
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[App] NotifyPoiDetailPageResume error: {ex.Message}");
        }
    }
}
