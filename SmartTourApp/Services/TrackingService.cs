using Microsoft.Maui.Devices.Sensors;
using SmartTourApp.Services;

namespace SmartTourApp.Services;

public class TrackingService
{
    private readonly LocationService location;
    private readonly GeofencingEngine geo;
    private readonly NarrationEngine narration;
    private readonly PoiRepository repo;
    private readonly LocationLogger logger;

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
        var pois = repo.GetPois();

        while (true)
        {
            var loc = await location.GetLocation();

            if (loc != null)
            {
                logger.Log(loc);

                OnLocationChanged?.Invoke(loc);

                var poi = geo.FindBestPoi(loc, pois);

                await narration.Play(poi, loc);
            }

            await Task.Delay(4000);
        }
    }
}