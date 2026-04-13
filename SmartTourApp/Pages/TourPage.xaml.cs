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
            TourCountLabel.Text = $"{tours.Count} tour";
        }
        catch (Exception ex)
        {
            await DisplayAlert("Lỗi tải tour", ex.Message, "OK");
        }
    }

    // ── Expand / collapse detail timeline ──
    private void OnExpandTapped(object sender, EventArgs e)
    {
        TourViewModel? tour = null;

        if (sender is Button btn && btn.CommandParameter is TourViewModel t1)
            tour = t1;

        if (tour == null) return;

        tour.IsExpanded = !tour.IsExpanded;

        // Cập nhật text nút
        if (sender is Button b)
            b.Text = tour.IsExpanded ? "Thu gọn ↑" : "Chi tiết ↓";
    }

    // ── Giữ nguyên hàm OpenTour cũ để không break các file khác ──
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

    // ── Play tour trên bản đồ — logic mới ──
    private async void StartTourOnMap(object sender, EventArgs e)
    {
        TourViewModel? tour = null;

        if (sender is Button btn && btn.CommandParameter is TourViewModel t)
            tour = t;

        if (tour == null) return;

        if (tour.Pois == null || tour.Pois.Count < 2)
        {
            await DisplayAlert(
                "Không thể bắt đầu",
                "Tour cần ít nhất 2 điểm dừng để hiển thị trên bản đồ.",
                "OK");
            return;
        }

        // Khởi tạo TourSession — ghi đè tour cũ nếu đang có
        TourSession.StartTour(tour);

        // Chuyển sang Map tab với tham số tourId
        await Shell.Current.GoToAsync($"//map?tourId={tour.Id}");
    }

    // ── Giữ nguyên StartTour cũ (sequential audio play) để không break ──
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
