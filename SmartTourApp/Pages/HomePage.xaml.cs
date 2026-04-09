using SmartTourApp.Services;
using SmartTour.Shared.Models;

namespace SmartTourApp.Pages;

public partial class HomePage : ContentPage
{
    private readonly PoiRepository repo;
    private readonly NarrationEngine narration;
    private readonly LocationService locationService;
    private readonly RouteTrackingService routeTracking;

    private List<Poi> pois = new();
    private Poi? nearest;
    private Location? userLoc;

    private bool isLoaded;
    private bool isPlaying;

    // ── Track item đang phát trong danh sách ──
    private bool isItemPlaying = false;
    private Poi? currentPlayingPoi;

    public HomePage(
        PoiRepository repo,
        NarrationEngine narration,
        LocationService locationService,
        RouteTrackingService routeTracking)
    {
        InitializeComponent();
        this.repo = repo;
        this.narration = narration;
        this.locationService = locationService;
        this.routeTracking = routeTracking;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        UpdateGreeting();

        if (!isLoaded)
            _ = LoadAsync();
    }

    private void UpdateGreeting()
    {
        var hour = DateTime.Now.Hour;
        GreetingLabel.Text = hour switch
        {
            < 12 => "Chào buổi sáng 👋",
            < 18 => "Chào buổi chiều 👋",
            _ => "Chào buổi tối 👋"
        };
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
            ? $"Cách bạn {(int)(dist * 1000)} m"
            : $"Cách bạn {Math.Round(dist, 1)} km";
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

    // ─────────────────────────────────────────────────────────────────
    // Yêu cầu 2: PlayNearest — chỉ ghi nhận RouteTracking khi trong radius
    // ─────────────────────────────────────────────────────────────────
    private async void PlayNearest(object sender, EventArgs e)
    {
        if (nearest == null || userLoc == null) return;

        if (isPlaying)
        {
            narration.Stop();
            isPlaying = false;
            HeroPlayBtn.Text = "🎧  Nghe ngay";
            return;
        }

        // 🔥 Lấy GPS mới nhất để kiểm tra radius chính xác
        var freshLoc = await GetFreshLocationAsync();
        var loc = freshLoc ?? userLoc;

        try
        {
            isPlaying = true;
            HeroPlayBtn.Text = "🔊  Đang phát...";

            // PlayManual sẽ dừng auto-play qua AudioCoordinator
            await narration.PlayManual(nearest, loc);

            // ── Yêu cầu 2: Ghi nhận RouteTracking nếu user đang trong radius ──
            await TryRecordRouteAsync(nearest, loc);
        }
        finally
        {
            isPlaying = false;
            HeroPlayBtn.Text = "🎧  Nghe ngay";
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Yêu cầu 4: Mở PoiDetailPage từ HomePage
    // → set SetOpenedFrom("home") để nút back trong PoiDetailPage
    //   biết phải quay về HomePage
    // ─────────────────────────────────────────────────────────────────
    private async void OpenDetailTap(object sender, TappedEventArgs e)
    {
        if (sender is not VisualElement el) return;

        var poi = el.BindingContext as Poi;
        if (poi == null) return;

        await el.ScaleToAsync(0.97, 80);
        await el.ScaleToAsync(1, 80);

        // Lấy PoiDetailPage từ DI và set nguồn mở là "home"
        var services = Application.Current?.Handler?.MauiContext?.Services;
        var detailPage = services?.GetService<PoiDetailPage>();
        detailPage?.SetOpenedFrom("home");

        await Shell.Current.GoToAsync(nameof(PoiDetailPage), true,
            new Dictionary<string, object> { ["poi"] = poi });
    }

    // ─────────────────────────────────────────────────────────────────
    // Yêu cầu 2: PlayPoiAudio — chỉ ghi nhận khi user chủ động bấm
    // ─────────────────────────────────────────────────────────────────
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

        // 🔥 Lấy GPS mới nhất để kiểm tra radius
        var freshLoc = await GetFreshLocationAsync();
        var loc = freshLoc ?? userLoc;

        try
        {
            isItemPlaying = true;
            currentPlayingPoi = poi;

            // PlayManual sẽ dừng auto-play qua AudioCoordinator
            await narration.PlayManual(poi, loc);

            // ── Yêu cầu 2: Ghi nhận RouteTracking nếu user đang trong radius ──
            await TryRecordRouteAsync(poi, loc);
        }
        finally
        {
            isItemPlaying = false;
            currentPlayingPoi = null;
        }
    }

    private async void GoToMapRoute(object sender, TappedEventArgs e)
    {
        if (sender is not Element el || el.BindingContext is not Poi poi)
            return;

        await Shell.Current.GoToAsync($"//map?targetPoi={poi.Id}");
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Yêu cầu 2: Chỉ ghi nhận route khi user đang trong radius của poi đó.
    /// Delegate hẳn cho RouteTrackingService.OnManualAudioPlayedAsync
    /// (nó đã có guard IsInRadius bên trong).
    /// </summary>
    private async Task TryRecordRouteAsync(Poi poi, Location loc)
    {
        try
        {
            await routeTracking.OnManualAudioPlayedAsync(poi, loc);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[HomePage] RouteTracking error: {ex.Message}");
        }
    }

    /// <summary>
    /// Lấy GPS mới nhất với timeout 2s, fallback sang cached.
    /// </summary>
    private async Task<Location?> GetFreshLocationAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var fresh = await locationService.GetLocation();
            if (fresh != null)
            {
                userLoc = fresh;
                return fresh;
            }
        }
        catch { }

        return userLoc;
    }
}
