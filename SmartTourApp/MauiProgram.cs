using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using SmartTourApp.Services;
using SkiaSharp.Views.Maui.Controls.Hosting;
using SmartTourApp.Data;

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

        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}