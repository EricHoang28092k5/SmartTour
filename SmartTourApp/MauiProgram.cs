using Mapsui.Logging;
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

        builder.Services.AddSingleton<AudioListenTracker>();

        // AudioCoordinator: singleton điều phối tránh xung đột NarrationEngine vs ExoPlayer
        builder.Services.AddSingleton<AudioCoordinator>();

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

        // HeatmapService: singleton giữ state machine (zones, cooldown)
        builder.Services.AddSingleton<HeatmapService>();

        // RouteTrackingService: singleton giữ state machine tuyến đi
        builder.Services.AddSingleton<RouteTrackingService>();

        builder.Services.AddSingleton<TrackingService>();
        builder.Services.AddSingleton<OfflineService>();
        builder.Services.AddSingleton<LanguageService>();

        // 🔥 PoiDetailAudioManager nhận thêm AudioService để stream Cloudinary URL
        builder.Services.AddSingleton<PoiDetailAudioManager>();

        builder.Services.AddSingleton<SettingsPage>();
        builder.Services.AddSingleton<HomePage>();
        builder.Services.AddSingleton<MapPage>();
        builder.Services.AddSingleton<TourPage>();
        builder.Services.AddTransient<PoiDetailPage>();

#if DEBUG
        builder.Logging.ClearProviders();
#endif

        return builder.Build();
    }
}
