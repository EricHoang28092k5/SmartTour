using SmartTourApp.Pages;
using SmartTourApp.Services;

namespace SmartTourApp;

public partial class AppShell : Shell
{
    // ── Cache để tránh tạo lại LocalizationService mỗi lần navigate ──
    private LocalizationService? _locService;

    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute(nameof(PoiDetailPage), typeof(PoiDetailPage));
        Routing.RegisterRoute(nameof(MapPage), typeof(MapPage));

        // Lazy-get service sau khi handler sẵn sàng
        this.HandlerChanged += OnHandlerChanged;

        Navigating += OnShellNavigating;
    }

    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        // Chỉ cần resolve một lần duy nhất
        _locService = App.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        if (_locService != null)
        {
            _locService.LanguageChanged += () =>
                MainThread.BeginInvokeOnMainThread(ApplyTabLocalization);
        }
        ApplyTabLocalization();
    }

    private void ApplyTabLocalization()
    {
        var loc = _locService
            ?? App.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        if (loc == null) return;

        ContentHome.Title = loc.HomeTab;
        ContentMap.Title = loc.MapTab;
        ContentSettings.Title = loc.SettingsTab;
    }

    /// <summary>
    /// YC1: Tối ưu navigation — PopToRoot async để tránh block main thread.
    /// Chỉ pop khi thực sự cần (stack > 1), không block shell switching.
    /// </summary>
    private void OnShellNavigating(object? sender, ShellNavigatingEventArgs e)
    {
        if (e.Source != ShellNavigationSource.ShellSectionChanged &&
            e.Source != ShellNavigationSource.ShellItemChanged)
            return;

        try
        {
            var current = Current?.CurrentItem?.CurrentItem as ShellSection;
            if (current?.Navigation?.NavigationStack?.Count > 1)
            {
                // Fire-and-forget trên main thread, không await để không block
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        await current.Navigation.PopToRootAsync(animated: false);
                    }
                    catch { /* non-critical */ }
                });
            }
        }
        catch { /* non-critical */ }
    }
}
