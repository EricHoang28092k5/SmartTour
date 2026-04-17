using SmartTour.Services;
using SmartTour.Shared.Models;
using SmartTourApp.Services;
using static SmartTour.Services.ApiService;

namespace SmartTourApp.Pages;

public partial class HomePage : ContentPage
{
    private readonly PoiRepository repo;
    private readonly NarrationEngine narration;
    private readonly LocationService locationService;
    private readonly RouteTrackingService routeTracking;
    private readonly LocalizationService loc;
    private readonly LanguageService lang;
    private readonly ApiService api;
    private readonly OfflineSyncService offlineSync;

    private List<Poi> pois = new();
    private Poi? nearest;
    private Location? userLoc;

    // YC1: Cache translated names per poi id — now loaded from SQLite offline cache first
    private readonly Dictionary<int, string> _translatedNames = new();

    private bool isLoaded;
    private bool isPlaying;
    private bool isItemPlaying = false;
    private Poi? currentPlayingPoi;

    // YC5: Track nếu data đã load xong để tránh re-fetch khi quay lại
    private bool _dataReady = false;
    private string _dataLang = "";

    public HomePage(
        PoiRepository repo,
        NarrationEngine narration,
        LocationService locationService,
        RouteTrackingService routeTracking,
        LocalizationService loc,
        LanguageService lang,
        ApiService api,
        OfflineSyncService offlineSync)
    {
        InitializeComponent();
        this.repo = repo;
        this.narration = narration;
        this.locationService = locationService;
        this.routeTracking = routeTracking;
        this.loc = loc;
        this.lang = lang;
        this.api = api;
        this.offlineSync = offlineSync;

        // YC1: Re-fetch names when language changes
        this.lang.OnLanguageChanged += async __ =>
        {
            _translatedNames.Clear();
            _dataReady = false;
            _dataLang = "";

            if (pois.Count > 0)
            {
                LoadTranslatedNamesFromCache(pois);
                ApplyTranslatedNames();
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ApplyLocalization();
                    PoiList.ItemsSource = null;
                    PoiList.ItemsSource = pois;
                    UpdateHeroTitle();
                });

                // Background: update từ API nếu online
                _ = RefreshTranslatedNamesFromApiAsync(pois);
            }
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ApplyLocalization();
        _ = SendPresenceHeartbeatSafeAsync();

        var currentLang = lang.Current;

        if (!isLoaded)
        {
            _ = LoadAsync();
        }
        else if (!_dataReady || !string.Equals(_dataLang, currentLang, StringComparison.OrdinalIgnoreCase))
        {
            // YC5: Ngôn ngữ đổi, cần refresh names — dùng cache trước, không re-fetch toàn bộ
            LoadTranslatedNamesFromCache(pois);
            ApplyTranslatedNames();
            PoiList.ItemsSource = null;
            PoiList.ItemsSource = pois;
            UpdateHeroTitle();
            _dataLang = currentLang;
            _dataReady = true;

            // Background refresh
            _ = RefreshTranslatedNamesFromApiAsync(pois);
        }
        else
        {
            // YC5: Data đã ready, chỉ update UI tối thiểu — không delay
            UpdateGreeting();
            UpdateHeroPlayBtn();
        }
    }

    private async Task SendPresenceHeartbeatSafeAsync()
    {
        try
        {
            if (!IsOnline()) return;
            await api.PostPresenceHeartbeatAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HomePage] Presence heartbeat error: {ex.Message}");
        }
    }

    private void ApplyLocalization()
    {
        AppNameLabel.Text = loc.AppName;
        UpdateGreeting();
        LblNearestBadge.Text = loc.NearestBadge;
        UpdateHeroPlayBtn();
        LblPlaces.Text = loc.Places;
        LblJourneys.Text = loc.Journeys;
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

    private void UpdateHeroPlayBtn()
    {
        HeroPlayBtn.Text = isPlaying ? loc.NowPlaying : loc.ListenNow;
    }

    private void UpdateHeroTitle()
    {
        if (nearest == null) return;
        if (_translatedNames.TryGetValue(nearest.Id, out var tName))
            HeroTitle.Text = tName;
        else
            HeroTitle.Text = nearest.Name;
    }

    private async Task LoadAsync()
    {
        isLoaded = true;
        HeroTitle.Text = loc.Loading;

        // YC5: Dùng cached pois nếu có để hiện nhanh
        var cachedPois = repo.GetCachedPois();
        if (cachedPois != null && cachedPois.Count > 0)
        {
            pois = cachedPois;

            // YC1: Load tên từ SQLite offline cache ngay lập tức (không cần network)
            LoadTranslatedNamesFromCache(pois);
            ApplyTranslatedNames();
            PoiList.ItemsSource = pois;

            // Load location song song
            _ = LoadLocationAsync();

            // Background: lấy POIs mới từ API
            _ = RefreshPoisFromApiAsync();
        }
        else
        {
            // Lần đầu — phải fetch từ API
            pois = await repo.GetPois();
            LoadTranslatedNamesFromCache(pois);
            ApplyTranslatedNames();
            PoiList.ItemsSource = pois;
            await LoadLocationAsync();
        }

        _dataLang = lang.Current;
        _dataReady = true;

        // Background: refresh titles từ API nếu online
        _ = RefreshTranslatedNamesFromApiAsync(pois);
    }

    // YC5: Background refresh POIs sau khi đã show cache
    private async Task RefreshPoisFromApiAsync()
    {
        try
        {
            var fresh = await repo.GetPois();
            if (fresh != null && fresh.Count > 0 && fresh.Count != pois.Count)
            {
                pois = fresh;
                LoadTranslatedNamesFromCache(pois);
                ApplyTranslatedNames();
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    PoiList.ItemsSource = null;
                    PoiList.ItemsSource = pois;
                    SortPoisInline();
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HomePage] RefreshPois error: {ex.Message}");
        }
    }

    // YC1: Load tên đã dịch từ SQLite offline cache (không cần network)
    private void LoadTranslatedNamesFromCache(List<Poi> poiList)
    {
        var currentLang = lang.Current;
        foreach (var poi in poiList)
        {
            var localTitle = offlineSync.GetLocalTitle(poi.Id, currentLang);
            if (!string.IsNullOrWhiteSpace(localTitle))
                _translatedNames[poi.Id] = localTitle;
        }
    }

    // YC1: Background refresh từ API (chỉ gọi khi online)
    private async Task RefreshTranslatedNamesFromApiAsync(List<Poi> poiList)
    {
        if (!IsOnline()) return;

        var currentLang = lang.Current;
        var tasks = poiList.Select(async poi =>
        {
            try
            {
                // Kiểm tra cache trước, chỉ gọi API nếu chưa có hoặc muốn refresh
                var localScripts = offlineSync.GetAllLocalScripts(poi.Id);
                var selected = localScripts
                    .FirstOrDefault(s => s.LanguageCode.StartsWith(currentLang, StringComparison.OrdinalIgnoreCase))
                    ?? localScripts.FirstOrDefault(s => s.LanguageCode.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                    ?? localScripts.FirstOrDefault();

                if (selected != null && !string.IsNullOrWhiteSpace(selected.Title))
                {
                    _translatedNames[poi.Id] = selected.Title;
                    return;
                }

                // Nếu không có cache thì gọi API
                var scripts = await api.GetTtsScripts(poi.Id);
                if (scripts == null || scripts.Count == 0) return;

                var apiSelected = SelectTitle(scripts, currentLang);
                if (apiSelected != null && !string.IsNullOrWhiteSpace(apiSelected.Title))
                    _translatedNames[poi.Id] = apiSelected.Title;
            }
            catch { /* fallback to poi.Name */ }
        });

        await Task.WhenAll(tasks);

        // Update UI chỉ nếu ngôn ngữ chưa đổi
        if (string.Equals(lang.Current, currentLang, StringComparison.OrdinalIgnoreCase))
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ApplyTranslatedNames();
                PoiList.ItemsSource = null;
                PoiList.ItemsSource = pois;
                UpdateHeroTitle();
            });
        }
    }

    // YC1: Apply translated names to poi.DisplayName
    private void ApplyTranslatedNames()
    {
        foreach (var poi in pois)
        {
            if (_translatedNames.TryGetValue(poi.Id, out var name))
                poi.DisplayName = name;
            else
                poi.DisplayName = poi.Name;
        }
    }

    private TtsDto? SelectTitle(List<TtsDto> scripts, string currentLang)
    {
        return scripts.FirstOrDefault(x =>
                x.LanguageCode.StartsWith(currentLang, StringComparison.OrdinalIgnoreCase))
            ?? scripts.FirstOrDefault(x =>
                x.LanguageCode.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            ?? scripts.FirstOrDefault(x =>
                x.LanguageCode.StartsWith("vi", StringComparison.OrdinalIgnoreCase))
            ?? scripts.FirstOrDefault();
    }

    private async Task LoadLocationAsync()
    {
        try
        {
            // YC5: Dùng last known trước (nhanh hơn), update với fresh location sau
            var lastKnown = await Geolocation.GetLastKnownLocationAsync();
            if (lastKnown != null)
            {
                userLoc = lastKnown;
                FindNearest();
                SortPoisInline();
            }

            // Sau đó lấy fresh location
            var fresh = await locationService.GetLocation();
            if (fresh != null && fresh != lastKnown)
            {
                userLoc = fresh;
                FindNearest();
                SortPoisInline();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HomePage] LoadLocation error: {ex.Message}");
        }
    }

    private void FindNearest()
    {
        if (userLoc == null || pois.Count == 0) return;

        nearest = pois
            .OrderBy(p => Location.CalculateDistance(
                userLoc, new Location(p.Lat, p.Lng), DistanceUnits.Kilometers))
            .FirstOrDefault();

        if (nearest == null) return;

        foreach (var p in pois)
            p.IsNearest = p.Id == nearest.Id;

        UpdateHeroTitle();

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            HeroImage.Source = nearest.ImageUrl;
            await HeroImage.FadeToAsync(1, 400);

            var dist = Location.CalculateDistance(
                userLoc, new Location(nearest.Lat, nearest.Lng), DistanceUnits.Kilometers);

            HeroDistance.Text = dist < 1
                ? string.Format(loc.DistanceM, (int)(dist * 1000))
                : string.Format(loc.DistanceKmFar, Math.Round(dist, 1));
        });
    }

    // YC5: SortPois inline, không tạo list mới không cần thiết
    private void SortPoisInline()
    {
        if (userLoc == null) return;
        pois = pois.OrderBy(p =>
            Location.CalculateDistance(
                userLoc, new Location(p.Lat, p.Lng), DistanceUnits.Kilometers))
            .ToList();
        ApplyTranslatedNames();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            PoiList.ItemsSource = null;
            PoiList.ItemsSource = pois;
        });
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

    private static bool IsOnline()
    {
        try
        {
            var access = Connectivity.Current.NetworkAccess;
            return access == NetworkAccess.Internet || access == NetworkAccess.ConstrainedInternet;
        }
        catch { return false; }
    }
}
