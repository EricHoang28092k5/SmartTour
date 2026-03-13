using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;
using SmartTour.Services;
using SmartTourApp.Data;
using SmartTourApp.Pages;
using SmartTourApp.Services;
using ZXing.Net.Maui;

namespace SmartTourApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiMaps()
            .UseSkiaSharp()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton<LocationService>();
        builder.Services.AddSingleton<AudioService>();
        builder.Services.AddSingleton<GeofencingEngine>();
        builder.Services.AddSingleton<NarrationEngine>();
        builder.Services.AddSingleton<PoiRepository>();
        builder.Services.AddSingleton<Database>();
        builder.Services.AddSingleton<LogService>();
        builder.Services.AddSingleton<TtsService>();
        builder.Services.AddSingleton<LocationLogger>();
        builder.Services.AddSingleton<TrackingService>();
        builder.Services.AddSingleton<OfflineService>();
        builder.Services.AddSingleton<LanguageService>();
        builder.Services.AddSingleton<QrScannerPage>();
        builder.Services.AddSingleton<SettingsPage>();
        builder.Services.AddSingleton<ApiService>();
        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}