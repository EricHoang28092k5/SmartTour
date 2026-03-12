using Microsoft.Maui.Devices.Sensors;

namespace SmartTourApp.Services;

public class LocationService
{
    public async Task<Location?> GetLocation()
    {
        try
        {
            var request = new GeolocationRequest(
                GeolocationAccuracy.Best,
                TimeSpan.FromSeconds(10));

            var location =
                await Geolocation.Default.GetLocationAsync(request);

            return location;
        }
        catch
        {
            return null;
        }
    }
}