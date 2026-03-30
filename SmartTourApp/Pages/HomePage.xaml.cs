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

    private bool isLoaded;
    private bool isPlaying;

    public HomePage(PoiRepository repo, NarrationEngine narration)
    {
        InitializeComponent();
        this.repo = repo;
        this.narration = narration;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (!isLoaded)
            _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        isLoaded = true;

        HeroTitle.Text = "Đang tải...";

        pois = repo.GetCachedPois() ?? await repo.GetPois();

        PoiList.ItemsSource = pois;

        await LoadLocationAsync();
    }

    private async Task LoadLocationAsync()
    {
        var loc = await Geolocation.GetLastKnownLocationAsync();
        if (loc == null) return;

        userLoc = loc;

        FindNearest();
        SortPois();
    }

    private async void FindNearest()
    {
        if (userLoc == null || pois.Count == 0) return;

        nearest = pois
            .OrderBy(p => Location.CalculateDistance(
                userLoc,
                new Location(p.Lat, p.Lng),
                DistanceUnits.Kilometers))
            .FirstOrDefault();

        if (nearest == null) return;

        foreach (var p in pois)
            p.IsNearest = p.Id == nearest.Id;

        HeroTitle.Text = nearest.Name;
        HeroImage.Source = nearest.ImageUrl;

        await HeroImage.FadeToAsync(1, 400);

        var dist = Location.CalculateDistance(
            userLoc,
            new Location(nearest.Lat, nearest.Lng),
            DistanceUnits.Kilometers);

        HeroDistance.Text = dist < 1
            ? $"{(int)(dist * 1000)} m"
            : $"{Math.Round(dist, 1)} km";
    }

    private void SortPois()
    {
        if (userLoc == null) return;

        pois = pois.OrderBy(p =>
            Location.CalculateDistance(
                userLoc,
                new Location(p.Lat, p.Lng),
                DistanceUnits.Kilometers))
            .ToList();

        PoiList.ItemsSource = pois;
    }

    private async void PlayNearest(object sender, EventArgs e)
    {
        if (nearest == null || userLoc == null || isPlaying) return;

        isPlaying = true;
        HeroPlayBtn.Text = "🔊 Đang phát...";

        await narration.Play(nearest, userLoc);

        HeroPlayBtn.Text = "🎧 Nghe ngay";
        isPlaying = false;
    }

    private async void OpenDetailTap(object sender, TappedEventArgs e)
    {
        if (sender is Border border && border.BindingContext is Poi poi)
        {
            await border.ScaleToAsync(0.97, 80);
            await border.ScaleToAsync(1, 80);

            await Shell.Current.GoToAsync(nameof(PoiDetailPage), true,
                new Dictionary<string, object> { ["poi"] = poi });
        }
    }
}