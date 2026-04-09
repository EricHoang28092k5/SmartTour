using SmartTour.Services;
using SmartTour.Shared.Models;
using SmartTourApp.Services;
using System.Collections.ObjectModel;
namespace SmartTourApp.Pages;

[QueryProperty(nameof(TargetTourId), "targetTour")]
public partial class TourPage : ContentPage
{
    private readonly ApiService api;
    private readonly NarrationEngine narration;

    private ObservableCollection<TourViewModel> tours = new();
    private string? targetTourId;

    public string? TargetTourId
    {
        get => targetTourId;
        set
        {
            targetTourId = value;
            ApplyTargetTour();
        }
    }

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
            ApplyTargetTour();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error Tour", ex.Message, "OK");
        }
    }

    private void ApplyTargetTour()
    {
        if (string.IsNullOrWhiteSpace(TargetTourId)) return;
        if (!int.TryParse(TargetTourId, out var id)) return;
        if (tours.Count == 0) return;

        foreach (var t in tours)
            t.IsExpanded = t.Id == id;
    }

    private void OpenTour(object sender, EventArgs e)
    {
        TourViewModel? tour = null;

        if (sender is Button btn && btn.CommandParameter is TourViewModel t1)
            tour = t1;
        else if (sender is Label lbl && lbl.BindingContext is TourViewModel t2)
            tour = t2;

        if (tour != null)
            tour.IsExpanded = !tour.IsExpanded;
    }

    private async void StartTour(object sender, EventArgs e)
    {
        TourViewModel? tour = null;

        if (sender is Button btn && btn.CommandParameter is TourViewModel t)
            tour = t;

        if (tour == null) return;

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