using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using SmartTourApp.Services;

namespace SmartTourApp;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "smarttour")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        DeepLinkService.Publish(Intent?.DataString);
    }

    protected override void OnNewIntent(Intent intent)
    {
        base.OnNewIntent(intent);
        DeepLinkService.Publish(intent?.DataString);
    }
}
