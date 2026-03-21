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
        // Lấy thêm AudioService ở đây
        var audio = services.GetService<AudioService>();

        if (poiRepo == null || tracking == null || audio == null)
            throw new Exception("Services not registered");

        // Truyền audio vào LoadingPage nếu LoadingPage là nơi sẽ gọi AppShell sau này
        var loadingPage = new LoadingPage(poiRepo, tracking, audio);

        return new Window(loadingPage);
    }
}