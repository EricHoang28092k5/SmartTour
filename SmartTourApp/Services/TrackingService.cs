#if ANDROID
using Android.Content;
using Android.App;
using SmartTourApp.Platforms.Android;
#endif

using Microsoft.Maui.Devices.Sensors;
using SmartTour.Shared.Models;

namespace SmartTourApp.Services;

public class TrackingService
{
    private readonly LocationService location;
    private readonly GeofencingEngine geo;
    private readonly NarrationEngine narration;
    private readonly PoiRepository repo;
    private readonly LocationLogger logger;

    private bool isRunning = false;
    private List<Poi> pois = new();

    public event Action<Location>? OnLocationChanged;

    public TrackingService(
        LocationService location,
        GeofencingEngine geo,
        NarrationEngine narration,
        PoiRepository repo,
        LocationLogger logger)
    {
        this.location = location;
        this.geo = geo;
        this.narration = narration;
        this.repo = repo;
        this.logger = logger;
    }

    public async Task Start()
    {
        if (isRunning) return;

        isRunning = true;

        pois = await repo.GetPois();

#if ANDROID
        StartForegroundService();
#endif

        _ = Task.Run(async () =>
        {
            var timer = new PeriodicTimer(TimeSpan.FromSeconds(7));

            try
            {
                while (isRunning && await timer.WaitForNextTickAsync())
                {
                    var loc = await location.GetLocation();

                    if (loc == null) continue;

                    logger.Log(loc);

                    OnLocationChanged?.Invoke(loc);

                    var poi = geo.FindBestPoi(loc, pois);

                    if (poi != null)
                        await narration.Play(poi, loc);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Tracking error: " + ex.Message);
            }
        });
    }

#if ANDROID
    private void StartForegroundService()
    {
        try
        {
            var context = Android.App.Application.Context;

            var intent = new Intent(context, typeof(TrackingForegroundService));

            if (OperatingSystem.IsAndroidVersionAtLeast(26))
                context.StartForegroundService(intent);
            else
                context.StartService(intent);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Start service error: " + ex.Message);
        }
    }
#endif

    public void Stop()
    {
        isRunning = false;
    }
}