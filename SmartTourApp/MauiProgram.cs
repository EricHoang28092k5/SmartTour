using Mapsui.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp.Views.Maui.Controls.Hosting;
using SmartTour.Services;
using SmartTourApp.Data;
using SmartTourApp.Pages;
using SmartTourApp.Services;
using SmartTourApp.Services.Offline;
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

        // ── Core Infrastructure ──
        builder.Services.AddSingleton<LocationService>();
        builder.Services.AddSingleton<AudioService>();
        builder.Services.AddSingleton<GeofencingEngine>();

        // ── Offline Infrastructure (Yêu cầu 1, 4, 5) ──
        builder.Services.AddSingleton<OfflineDatabase>();
        builder.Services.AddSingleton<OfflineSyncService>();

        // 🔥 Offline Map Infrastructure
        builder.Services.AddSingleton<OfflineMapService>();

        // ── Audio Pipeline ──
        builder.Services.AddSingleton<AudioListenTracker>();
        builder.Services.AddSingleton<AudioCoordinator>();
        builder.Services.AddSingleton<TtsService>();

        // ── NarrationEngine (đã tích hợp Priority Queue + Fallback) ──
        builder.Services.AddSingleton<NarrationEngine>();

        // ── API ──
        builder.Services.AddHttpClient<ApiService>(client =>
        {
            client.BaseAddress = new Uri("http://10.0.2.2:5165/");
        });

        // ── Data / Repository ──
        builder.Services.AddSingleton<PoiRepository>();
        builder.Services.AddSingleton<Database>();
        builder.Services.AddSingleton<LogService>();
        builder.Services.AddSingleton<LocationLogger>();

        // ── Analytics / Tracking ──
        builder.Services.AddSingleton<HeatmapService>();
        builder.Services.AddSingleton<RouteTrackingService>();
        builder.Services.AddSingleton<TrackingService>();

        // ── Misc ──
        builder.Services.AddSingleton<OfflineService>();
        builder.Services.AddSingleton<LanguageService>();

        // ── PoiDetailAudioManager (tích hợp Offline + TTS fallback) ──
        builder.Services.AddSingleton<PoiDetailAudioManager>();

        // ── Pages ──
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
