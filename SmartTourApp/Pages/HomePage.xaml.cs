using SmartTourApp.Services;
using SmartTour.Shared.Models;

namespace SmartTourApp.Pages;

public partial class HomePage : ContentPage
{
    private readonly PoiRepository repo;
    private readonly NarrationEngine narration;
    private readonly LocationService locationService;
    private readonly RouteTrackingService routeTracking;
    private readonly LocalizationService loc;

    private List<Poi> pois = new();
    private Poi? nearest;
    private Location? userLoc;

    private bool isLoaded;
    private bool isPlaying;
    private bool isItemPlaying = false;
    private Poi? currentPlayingPoi;

    public HomePage(
        PoiRepository repo,
        NarrationEngine narration,
        LocationService locationService,
        RouteTrackingService routeTracking,
        LocalizationService loc)
    {
        InitializeComponent();
        this.repo = repo;
        this.narration = narration;
        this.locationService = locationService;
        this.routeTracking = routeTracking;
        this.loc = loc;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        ApplyLocalization();

        if (!isLoaded)
            _ = LoadAsync();
    }

    private void ApplyLocalization()
    {
        AppNameLabel.Text = loc.AppName;
        UpdateGreeting();
        LblNearestBadge.Text = loc.NearestBadge;
        HeroPlayBtn.Text = isPlaying ? loc.NowPlaying : loc.ListenNow;
        LblPlaces.Text = loc.Places;
        LblJourneys.Text = loc.Journeys;
        LblRating.Text = loc.Rating;
        LblNearbyTitle.Text = loc.NearbyPlaces;
        LblNearbySubtitle.Text = loc.ExploreAround;
        LblViewAll.Text = loc.ViewAll;
    }

    private void UpdateGreeting()
    {
        var hour = DateTime.Now.Hour;
        GreetingLabel.Text = hour switch
        {
            < 12 => loc.GreetingMorning,
            < 18 => loc.GreetingAfternoon,
            _ => loc.GreetingEvening
        };
    }

    private async Task LoadAsync()
    {
        isLoaded = true;
        HeroTitle.Text = loc.Loading;

        pois = repo.GetCachedPois() ?? await repo.GetPois();
        PoiList.ItemsSource = pois;

        await LoadLocationAsync();
    }

    private async Task LoadLocationAsync()
    {
        var loc2 = await Geolocation.GetLastKnownLocationAsync();
        if (loc2 == null) return;

        userLoc = loc2;
        FindNearest();
        SortPois();
    }

    private async void FindNearest()
    {
        if (userLoc == null || pois.Count == 0) return;

        nearest = pois
            .OrderBy(p => Location.CalculateDistance(
                userLoc, new Location(p.Lat, p.Lng), DistanceUnits.Kilometers))
            .FirstOrDefault();

        if (nearest == null) return;

        foreach (var p in pois)
            p.IsNearest = p.Id == nearest.Id;

        HeroTitle.Text = nearest.Name;
        HeroImage.Source = nearest.ImageUrl;

        await HeroImage.FadeToAsync(1, 400);

        var dist = Location.CalculateDistance(
            userLoc, new Location(nearest.Lat, nearest.Lng), DistanceUnits.Kilometers);

        HeroDistance.Text = dist < 1
            ? string.Format(loc.DistanceM, (int)(dist * 1000))
            : string.Format(loc.DistanceKmFar, Math.Round(dist, 1));
    }

    private void SortPois()
    {
        if (userLoc == null) return;
        pois = pois.OrderBy(p =>
            Location.CalculateDistance(
                userLoc, new Location(p.Lat, p.Lng), DistanceUnits.Kilometers))
            .ToList();
        PoiList.ItemsSource = pois;
    }

    private async void PlayNearest(object sender, EventArgs e)
    {
        if (nearest == null || userLoc == null) return;

        if (isPlaying)
        {
            narration.Stop();
            isPlaying = false;
            HeroPlayBtn.Text = loc.ListenNow;
            return;
        }

        var freshLoc = await GetFreshLocationAsync();
        var currentLoc = freshLoc ?? userLoc;

        try
        {
            isPlaying = true;
            HeroPlayBtn.Text = loc.NowPlaying;
            await narration.PlayManual(nearest, currentLoc);
            await TryRecordRouteAsync(nearest, currentLoc);
        }
        finally
        {
            isPlaying = false;
            HeroPlayBtn.Text = loc.ListenNow;
        }
    }

    private async void OpenDetailTap(object sender, TappedEventArgs e)
    {
        if (sender is not VisualElement el) return;
        var poi = el.BindingContext as Poi;
        if (poi == null) return;

        await el.ScaleToAsync(0.97, 70);
        await el.ScaleToAsync(1.0, 70);

        var services = Application.Current?.Handler?.MauiContext?.Services;
        var detailPage = services?.GetService<PoiDetailPage>();
        detailPage?.SetOpenedFrom("home");

        await Shell.Current.GoToAsync(nameof(PoiDetailPage), true,
            new Dictionary<string, object> { ["poi"] = poi });
    }

    private async void PlayPoiAudio(object sender, TappedEventArgs e)
    {
        if (sender is not Element el || el.BindingContext is not Poi poi) return;
        if (userLoc == null) return;

        if (isItemPlaying && currentPlayingPoi?.Id == poi.Id)
        {
            narration.Stop();
            isItemPlaying = false;
            currentPlayingPoi = null;
            return;
        }

        var freshLoc = await GetFreshLocationAsync();
        var currentLoc = freshLoc ?? userLoc;

        try
        {
            isItemPlaying = true;
            currentPlayingPoi = poi;
            await narration.PlayManual(poi, currentLoc);
            await TryRecordRouteAsync(poi, currentLoc);
        }
        finally
        {
            isItemPlaying = false;
            currentPlayingPoi = null;
        }
    }

    private async void GoToMapRoute(object sender, TappedEventArgs e)
    {
        if (sender is not Element el || el.BindingContext is not Poi poi) return;
        await Shell.Current.GoToAsync($"//map?targetPoi={poi.Id}");
    }

    private async Task TryRecordRouteAsync(Poi poi, Location currentLoc)
    {
        try { await routeTracking.OnManualAudioPlayedAsync(poi, currentLoc); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HomePage] RouteTracking error: {ex.Message}");
        }
    }

    private async Task<Location?> GetFreshLocationAsync()
    {
        try
        {
            var fresh = await locationService.GetLocation();
            if (fresh != null) { userLoc = fresh; return fresh; }
        }
        catch { }
        return userLoc;
    }
}
