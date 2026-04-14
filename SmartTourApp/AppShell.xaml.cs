using SmartTourApp.Pages;
using SmartTourApp.Services;

namespace SmartTourApp;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute(nameof(PoiDetailPage), typeof(PoiDetailPage));
        Routing.RegisterRoute(nameof(MapPage), typeof(MapPage));

        // 1. Đăng ký nhận thông báo đổi ngôn ngữ
        // Lấy LocalizationService thông qua Handler của App
        var loc = App.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        if (loc != null)
        {
            loc.LanguageChanged += () => MainThread.BeginInvokeOnMainThread(ApplyTabLocalization);
        }

        // 2. Chạy lần đầu khi mở app
        ApplyTabLocalization();

        Navigating += OnShellNavigating;
    }

    private void ApplyTabLocalization()
    {
        // Lấy Service để đọc các chuỗi chữ
        var loc = App.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        if (loc == null) return;

        // Gán chữ cho các ShellContent thông qua x:Name đã đặt bên XAML
        ContentHome.Title = loc.HomeTab;
        ContentMap.Title = loc.MapTab;
        ContentTour.Title = loc.TourTab;
        ContentSettings.Title = loc.SettingsTab;
    }

    private async void OnShellNavigating(object? sender, ShellNavigatingEventArgs e)
    {
        // Giữ nguyên logic PopToRoot cũ của bác
        if (e.Source != ShellNavigationSource.ShellSectionChanged &&
            e.Source != ShellNavigationSource.ShellItemChanged)
            return;

        try
        {
            var current = Current?.CurrentItem?.CurrentItem as ShellSection;
            if (current?.Navigation?.NavigationStack?.Count > 1)
            {
                _ = Task.Run(async () =>
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await current.Navigation.PopToRootAsync(animated: false);
                    });
                });
            }
        }
        catch { /* non-critical */ }
    }
}