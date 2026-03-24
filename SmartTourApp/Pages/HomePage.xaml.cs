using SmartTourApp.Services;
using SmartTour.Shared.Models;

namespace SmartTourApp.Pages;

public partial class HomePage : ContentPage
{
    private bool isLoaded = false;
    private readonly PoiRepository repo;
    private List<Poi> pois = new();

    public HomePage(PoiRepository repo)
    {
        InitializeComponent();
        this.repo = repo;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            if (!isLoaded)
            {
                if (pois.Count == 0)
                    pois = await repo.GetPois();

                var userLoc = await Geolocation.GetLastKnownLocationAsync();

                await Task.Run(() =>
                {
                    if (userLoc != null)
                    {
                        foreach (var poi in pois)
                        {
                            var distance = Location.CalculateDistance(
                                userLoc,
                                new Location(poi.Lat, poi.Lng),
                                DistanceUnits.Kilometers);

                            double meters = distance * 1000;

                            poi.Description = meters < 1000
                                ? $"{(int)meters} m"
                                : $"{Math.Round(distance, 1)} km";
                        }

                        pois = pois.OrderBy(p =>
                        {
                            var text = p.Description.Replace(" km", "").Replace(" m", "");

                            if (double.TryParse(text, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out var val))
                                return val;

                            return double.MaxValue;
                        }).ToList();
                    }
                });

                PoiList.ItemsSource = pois;
                isLoaded = true;
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async void OpenMap(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//MapPage");
    }

    private async void OpenQR(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//QrScannerPage");
    }

    private async void OpenDetail(object sender, EventArgs e)
    {
        if (sender is Grid grid && grid.BindingContext is Poi poi)
        {
            await DisplayAlertAsync("POI", poi.Name, "OK");
            // sau này push sang detail page
        }
    }
}