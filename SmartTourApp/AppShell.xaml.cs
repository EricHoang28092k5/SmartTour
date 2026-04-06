using SmartTourApp.Pages;

namespace SmartTourApp;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute(nameof(PoiDetailPage), typeof(PoiDetailPage));
        Routing.RegisterRoute(nameof(MapPage), typeof(MapPage));

        // 🔥 When user taps a tab, navigate to its root so any pushed pages (like PoiDetail) are popped
        Navigating += OnShellNavigating;
    }

    private async void OnShellNavigating(object? sender, ShellNavigatingEventArgs e)
    {
        // Only handle tab (absolute) navigation
        if (e.Source != ShellNavigationSource.ShellSectionChanged &&
            e.Source != ShellNavigationSource.ShellItemChanged)
            return;

        // Pop to root of the current section before switching
        try
        {
            var current = Current?.CurrentItem?.CurrentItem as ShellSection;
            if (current?.Navigation?.NavigationStack?.Count > 1)
            {
                // Don't block — pop async after navigation begins
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
