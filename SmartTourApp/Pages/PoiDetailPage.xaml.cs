using SmartTour.Shared.Models;

namespace SmartTourApp.Pages;

public partial class PoiDetailPage : ContentPage
{
    private readonly Poi poi;

    public PoiDetailPage(Poi poi)
    {
        InitializeComponent();

        this.poi = poi;

        PoiName.Text = poi.Name;
        PoiDescription.Text = poi.Description;

        if (!string.IsNullOrWhiteSpace(poi.ImageUrl))
            PoiImage.Source = poi.ImageUrl;
    }
}