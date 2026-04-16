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

        // ── Language & Localization ──
        // YC4: LanguageService defaults to English on first install
        builder.Services.AddSingleton<LanguageService>();
        builder.Services.AddSingleton<LocalizationService>();

        // ── Offline Infrastructure (Yêu cầu 1, 4, 5) ──
        builder.Services.AddSingleton<OfflineDatabase>();
        builder.Services.AddSingleton<OfflineSyncService>();

        // Offline Map Infrastructure
        builder.Services.AddSingleton<OfflineMapService>();

        // ── Audio Pipeline ──
        builder.Services.AddSingleton<AudioListenTracker>();
        builder.Services.AddSingleton<AudioCoordinator>();
        builder.Services.AddSingleton<TtsService>();

        // ── NarrationEngine (Priority Queue + Fallback) ──
        builder.Services.AddSingleton<NarrationEngine>();

        // ── API ──
        builder.Services.AddHttpClient<ApiService>(client =>
        {
            client.BaseAddress = new Uri("http://10.0.2.2:5165/");
            // YC3: Thêm timeout để tránh treo UI
            client.Timeout = TimeSpan.FromSeconds(15);
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

        // ── PoiDetailAudioManager (Offline + TTS fallback) ──
        builder.Services.AddSingleton<PoiDetailAudioManager>();

        // ── Pages ──
        builder.Services.AddSingleton<SettingsPage>();
        builder.Services.AddSingleton<HomePage>();
        builder.Services.AddSingleton<MapPage>();

        // YC2: PoiDetailPage giờ nhận thêm OfflineSyncService + LanguageService
        // để đồng bộ tên POI theo ngôn ngữ giống HomePage
        builder.Services.AddTransient<PoiDetailPage>();

#if DEBUG
        builder.Logging.ClearProviders();
#endif

        return builder.Build();
    }
}
