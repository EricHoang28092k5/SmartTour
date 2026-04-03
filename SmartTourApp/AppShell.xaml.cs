using SmartTourApp.Pages;

namespace SmartTourApp;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute(nameof(PoiDetailPage), typeof(PoiDetailPage));
		Routing.RegisterRoute(nameof(MapPage), typeof(MapPage));
	}
}
