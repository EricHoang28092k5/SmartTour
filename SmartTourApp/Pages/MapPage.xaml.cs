using Mapsui;
using Mapsui.Tiling;
using SmartTourApp.Services;
using SmartTourApp.ViewModels;

namespace SmartTourApp.Pages;

public partial class MapPage : ContentPage
{
    private readonly TrackingService tracking;
    private readonly PoiRepository repo;

    private readonly MapViewModel vm = new();

    public MapPage(
        TrackingService tracking,
        PoiRepository repo)
    {
        InitializeComponent();

        this.tracking = tracking;
        this.repo = repo;

        InitMap();

        tracking.OnLocationChanged += UpdateLocation;

        Task.Run(tracking.Start);
    }

    private async void InitMap()
    {
        TourMap.Map = new Mapsui.Map();

        TourMap.Map.Layers.Add(
            OpenStreetMap.CreateTileLayer());

        var pois = await repo.GetPois();

        vm.LoadPois(TourMap.Map, pois);

        TourMap.Map.Layers.Add(vm.PoiLayer);
        TourMap.Map.Layers.Add(vm.UserLayer);
    }

    private void UpdateLocation(Location loc)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            vm.UpdateUser(TourMap.Map, loc);
            TourMap.Refresh();
        });
    }
}