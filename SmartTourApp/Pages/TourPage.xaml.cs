using SmartTour.Services;
using SmartTour.Shared.Models;
using SmartTourApp.Services;
using System.Collections.ObjectModel;

namespace SmartTourApp.Pages;

public partial class TourPage : ContentPage
{
    private readonly ApiService api;
    private readonly NarrationEngine narration;

    private ObservableCollection<TourViewModel> tours = new();

    public TourPage(ApiService api, NarrationEngine narration)
    {
        InitializeComponent();
        this.api = api;
        this.narration = narration;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (tours.Count == 0)
            await LoadTours();
    }

    private async Task LoadTours()
    {
        try
        {
            var res = await api.GetTours();

            if (res?.Data == null) return;

            var mapped = res.Data.Select(t => new TourViewModel
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                Pois = t.Pois?
                    .OrderBy(p => p.OrderIndex)
                    .ToList() ?? new List<TourPoiDto>()
            });

            foreach (var t in mapped)
                tours.Add(t);

            TourList.ItemsSource = tours;
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error Tour", ex.Message, "OK");
        }
    }

    private void OpenTour(object sender, EventArgs e)
    {
        if (sender is Button btn &&
            btn.CommandParameter is TourViewModel tour)
        {
            tour.IsExpanded = !tour.IsExpanded;
        }
    }

    private async void StartTour(object sender, EventArgs e)
    {
        if (sender is Button btn &&
            btn.CommandParameter is TourViewModel tour)
        {
            foreach (var p in tour.Pois)
            {
                var fakeLocation = new Location(p.Lat, p.Lng);

                await narration.PlayManual(new Poi
                {
                    Id = p.PoiId,
                    Name = p.Name,
                    Lat = p.Lat,
                    Lng = p.Lng
                }, fakeLocation);

                await Task.Delay(1000);
            }
        }
    }
}