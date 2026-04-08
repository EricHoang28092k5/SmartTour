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
        // Lấy DI container
        var services = Application.Current?.Handler?.MauiContext?.Services;

        if (services == null)
            throw new Exception("DI container not ready");

        var poiRepo = services.GetService<PoiRepository>();
        var tracking = services.GetService<TrackingService>();

        if (poiRepo == null || tracking == null)
            throw new Exception("Services not registered");

        var loadingPage = new LoadingPage(poiRepo, tracking);

        return new Window(loadingPage);
    }

    protected override void OnSleep()
    {
        base.OnSleep();

        var services = Current?.Handler?.MauiContext?.Services;

        services?.GetService<NarrationEngine>()?.Stop();

        // 🔥 Flush route session khi app đi vào background / bị kill
        // Session sẽ được persist vào Preferences để recovery khi mở lại
        var routeTracking = services?.GetService<RouteTrackingService>();
        if (routeTracking != null)
        {
            // Fire-and-forget (OnSleep không await được)
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
    }
}
