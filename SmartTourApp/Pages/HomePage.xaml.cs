using SmartTourApp.Services;
using SmartTour.Shared.Models;

namespace SmartTourApp.Pages;

public partial class HomePage : ContentPage
{
    private readonly PoiRepository repo;
    private readonly NarrationEngine narration;

    private List<Poi> pois = new();
    private Poi? nearest;
    private Location? userLoc;

    private bool isLoaded = false;
    private bool isPlaying = false;

    public HomePage(PoiRepository repo, NarrationEngine narration)
    {
        InitializeComponent();
        this.repo = repo;
        this.narration = narration;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // ✅ KHÔNG reload lại nữa
        if (isLoaded)
            return;

        LoadData();
    }

    private async void LoadData()
    {
        // ✅ lấy cache trước (instant)
        pois = repo.GetCachedPois() ?? await repo.GetPois();

        PoiList.ItemsSource = pois;

        isLoaded = true;

        // ✅ load location async (không block UI)
        _ = Task.Run(async () =>
        {
            var loc = await Geolocation.GetLastKnownLocationAsync();

            if (loc != null)
            {
                userLoc = loc;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    FindNearest();
                    SortPois();
                });
            }
        });
    }

    private void FindNearest()
    {
        double min = double.MaxValue;

        foreach (var p in pois)
        {
            var dist = Location.CalculateDistance(
                userLoc,
                new Location(p.Lat, p.Lng),
                DistanceUnits.Kilometers);

            if (dist < min)
            {
                min = dist;
                nearest = p;
            }
        }

        if (nearest != null)
        {
            HeroTitle.Text = nearest.Name;
            HeroImage.Source = nearest.ImageUrl;

            double meters = min * 1000;

            HeroDistance.Text = meters < 1000
                ? $"{(int)meters} m"
                : $"{Math.Round(min, 1)} km";
        }
    }

    private void SortPois()
    {
        pois = pois.OrderBy(p =>
            Location.CalculateDistance(
                userLoc,
                new Location(p.Lat, p.Lng),
                DistanceUnits.Kilometers))
            .ToList();

        // ❗ chỉ update lại source 1 lần duy nhất
        PoiList.ItemsSource = pois;
    }

    // 🎧 HERO
    private async void PlayNearest(object sender, EventArgs e)
    {
        if (nearest == null || userLoc == null || isPlaying) return;

        isPlaying = true;
        HeroPlayBtn.Text = "🔊 Đang phát...";

        await narration.Play(nearest, userLoc);

        HeroPlayBtn.Text = "🎧 Nghe ngay";
        isPlaying = false;
    }

    // 🎧 LIST
    private void PlayPoi(object sender, EventArgs e)
    {
        if (isPlaying || userLoc == null) return;

        if (sender is Button btn && btn.CommandParameter is Poi poi)
        {
            isPlaying = true;

            btn.Text = "🔊 Đang phát...";

            // ❗ không await → không block UI
            _ = Task.Run(async () =>
            {
                await narration.Play(poi, userLoc);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    btn.Text = "🎧 Nghe";
                    isPlaying = false;
                });
            });
        }
    }

    private async void OpenMap(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("//MapPage");

    private async void OpenQR(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("//QrScannerPage");

    private async void OpenSettings(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("//SettingsPage");
}