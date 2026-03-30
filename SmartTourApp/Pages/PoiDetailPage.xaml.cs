using SmartTour.Shared.Models;

namespace SmartTourApp.Pages;

[QueryProperty(nameof(Poi), "poi")]
public partial class PoiDetailPage : ContentPage
{
    private Poi poi;

    public Poi Poi
    {
        set
        {
            poi = value;
            BindData();
        }
    }

    public PoiDetailPage()
    {
        InitializeComponent();
    }

    private void BindData()
    {
        if (poi == null) return;

        PoiName.Text = poi.Name;
        PoiImage.Source = poi.ImageUrl;

        PoiDescription.Text = string.IsNullOrWhiteSpace(poi.Description)
            ? "Chưa có mô tả"
            : poi.Description;

        FoodList.ItemsSource = poi.Foods ?? new List<Food>();
    }

    // ======================
    // TAB SWITCH (NO LAG)
    // ======================

    private void ShowOverview(object sender, EventArgs e)
    {
        OverviewSection.IsVisible = true;
        FoodList.IsVisible = false;

        OverviewTab.BackgroundColor = Color.FromArgb("#1976D2");
        OverviewTab.TextColor = Colors.White;

        MenuTab.BackgroundColor = Color.FromArgb("#EEE");
        MenuTab.TextColor = Colors.Black;
    }

    private void ShowMenu(object sender, EventArgs e)
    {
        OverviewSection.IsVisible = false;
        FoodList.IsVisible = true;

        MenuTab.BackgroundColor = Color.FromArgb("#1976D2");
        MenuTab.TextColor = Colors.White;

        OverviewTab.BackgroundColor = Color.FromArgb("#EEE");
        OverviewTab.TextColor = Colors.Black;
    }

    // ======================
    // 🔥 FULL SCREEN IMAGE
    // ======================

    private async void OnFoodImageTapped(object sender, TappedEventArgs e)
    {
        if (sender is Microsoft.Maui.Controls.Image img && img.BindingContext is Food food)
        {
            if (string.IsNullOrWhiteSpace(food.ImageUrl))
                return;

            PreviewImage.Source = food.ImageUrl;

            ImageViewer.IsVisible = true;

            await Task.WhenAll(
                ImageViewer.FadeToAsync(1, 150),
                PreviewImage.ScaleToAsync(1, 200, Easing.CubicOut)
            );
        }
    }

    private async void CloseImageViewer(object sender, EventArgs e)
    {
        await Task.WhenAll(
            ImageViewer.FadeToAsync(0, 150),
            PreviewImage.ScaleToAsync(0.8, 150)
        );

        ImageViewer.IsVisible = false;
    }
}