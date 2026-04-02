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

        var narration = Current?.Handler?.MauiContext?.Services
            .GetService<NarrationEngine>();

        narration?.Stop();
    }
}