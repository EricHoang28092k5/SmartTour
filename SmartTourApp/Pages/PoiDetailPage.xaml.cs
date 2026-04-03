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

        UpdateOpenStatus(); // 🔥 NEW
    }

    // ======================
    // 🔥 OPEN STATUS LOGIC
    // ======================

    private void UpdateOpenStatus()
    {
        if (poi == null || poi.OpenTime == null || poi.CloseTime == null)
            return;

        var now = DateTime.Now;

        var open = DateTime.Today.Add(poi.OpenTime.Value);
        var close = DateTime.Today.Add(poi.CloseTime.Value);

        // MỞ CẢ NGÀY
        if (poi.OpenTime == poi.CloseTime)
        {
            OpenStatus.Text = "🔵 Mở cả ngày";
            OpenDetail.Text = "";
            return;
        }

        // CHƯA MỞ
        if (now < open)
        {
            OpenStatus.Text = "🔴 Đã đóng cửa";
            OpenDetail.Text = $"Mở lúc {open:HH:mm}";
            return;
        }

        // ĐANG MỞ
        if (now >= open && now < close)
        {
            // TRƯỚC 1 TIẾNG
            if (now >= close.AddHours(-1))
            {
                OpenStatus.Text = "🟠 Sắp đóng cửa";
                OpenDetail.Text = $"{close:HH:mm}";
            }
            else
            {
                OpenStatus.Text = "🟢 Đang mở cửa";
                OpenDetail.Text = $"Đóng cửa lúc {close:HH:mm}";
            }

            return;
        }

        // SAU GIỜ ĐÓNG → NGÀY MAI
        if (now >= close)
        {
            var tomorrow = DateTime.Today.AddDays(1).Add(poi.OpenTime.Value);

            OpenStatus.Text = "🔴 Đã đóng cửa";
            OpenDetail.Text = $"Mở lúc {tomorrow:HH:mm} {GetVietnameseDay(tomorrow)}";
        }
    }

    private string GetVietnameseDay(DateTime date)
    {
        return date.DayOfWeek switch
        {
            DayOfWeek.Monday => "Thứ 2",
            DayOfWeek.Tuesday => "Thứ 3",
            DayOfWeek.Wednesday => "Thứ 4",
            DayOfWeek.Thursday => "Thứ 5",
            DayOfWeek.Friday => "Thứ 6",
            DayOfWeek.Saturday => "Thứ 7",
            DayOfWeek.Sunday => "Chủ nhật",
            _ => ""
        };
    }

    // ======================
    // TAB SWITCH
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
    // IMAGE VIEWER
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