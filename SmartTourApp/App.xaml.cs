using SmartTourApp.Pages;
using SmartTourApp.Services;

namespace SmartTourApp;

public partial class App : Application
{
    private const string QrGateUntilKey = "qr_gate_until_utc";

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

        // Khởi động background sync ngay khi app mở
        var offlineSync = services.GetService<OfflineSyncService>();
        offlineSync?.StartBackgroundSync();

        // ── YC1: Kiểm tra QR Gate ──
        // Nếu chưa có phiên hợp lệ → bắt buộc quét QR
        Page startPage = IsQrSessionValid()
            ? (Page)new LoadingPage(poiRepo!, tracking!)
            : new QrGatePage();

        return new Window(startPage);
    }

    // ── YC1: Kiểm tra phiên QR còn hạn không ──
    private static bool IsQrSessionValid()
    {
        try
        {
            var raw = Preferences.Default.Get(QrGateUntilKey, string.Empty);
            if (string.IsNullOrWhiteSpace(raw)) return false;

            if (!DateTime.TryParse(raw, null,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var until))
                return false;

            return DateTime.UtcNow < until;
        }
        catch
        {
            return false;
        }
    }

    protected override void OnSleep()
    {
        base.OnSleep();

        var services = Current?.Handler?.MauiContext?.Services;

        // Dừng audio thuyết minh chung khi app ẩn
        services?.GetService<NarrationEngine>()?.Stop();

        // Flush dữ liệu tracking (nếu có)
        var routeTracking = services?.GetService<RouteTrackingService>();
        if (routeTracking != null)
        {
            _ = Task.Run(async () =>
            {
                try { await routeTracking.FlushOnAppClosingAsync(); } catch { }
            });
        }

        // Force sync dữ liệu offline khi app đi vào background
        var offlineSync = services?.GetService<OfflineSyncService>();
        if (offlineSync != null)
        {
            _ = Task.Run(async () =>
            {
                try { await offlineSync.ForceSyncNowAsync(); } catch { }
            });
        }
    }

    protected override void OnResume()
    {
        base.OnResume();

        var services = Current?.Handler?.MauiContext?.Services;

        // Trigger sync lại ngay khi app quay lại
        var offlineSync = services?.GetService<OfflineSyncService>();
        if (offlineSync != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2000); // Đợi mạng ổn định
                    await offlineSync.ForceSyncNowAsync();
                }
                catch { }
            });
        }
    }
}
