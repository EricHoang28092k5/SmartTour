using Mapsui.Logging;
using Mapsui.Widgets;
using Mapsui.Widgets.InfoWidgets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp.Views.Maui.Controls.Hosting;
using SmartTour.Services;
using SmartTourApp.Data;
using SmartTourApp.Pages;
using SmartTourApp.Services;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace SmartTourApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        Mapsui.Logging.Logger.LogDelegate = null;


        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseBarcodeReader()
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
        builder.Services.AddHttpClient<ApiService>(client =>
        {
            client.BaseAddress = new Uri("http://10.0.2.2:5165/");
        });
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
        builder.Services.AddSingleton<HomePage>();
        builder.Services.AddSingleton<MapPage>();

#if DEBUG
        builder.Logging.ClearProviders();
#endif

        return builder.Build();
    }
}