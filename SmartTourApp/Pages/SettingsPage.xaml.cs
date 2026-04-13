using SmartTourApp.Services;

namespace SmartTourApp.Pages;

public partial class SettingsPage : ContentPage
{
    private const string QrGateUntilKey = "qr_gate_until_utc";

    private readonly LanguageService lang;
    private readonly LocalizationService loc;
    private readonly PoiRepository repo;
    private readonly TrackingService tracking;
    private readonly NarrationEngine narration;
    private readonly GeofencingEngine geo;
    private readonly HeatmapService heatmap;
    private readonly RouteTrackingService routeTracking;
    private readonly AudioCoordinator coordinator;
    private readonly OfflineMapService offlineMapService;

    public const string AutoPlayKey = "auto_play_enabled";

    public SettingsPage(
        LanguageService lang,
        LocalizationService loc,
        PoiRepository repo,
        TrackingService tracking,
        NarrationEngine narration,
        GeofencingEngine geo,
        HeatmapService heatmap,
        RouteTrackingService routeTracking,
        AudioCoordinator coordinator,
        OfflineMapService offlineMapService)
    {
        InitializeComponent();
        this.lang = lang;
        this.loc = loc;
        this.repo = repo;
        this.tracking = tracking;
        this.narration = narration;
        this.geo = geo;
        this.heatmap = heatmap;
        this.routeTracking = routeTracking;
        this.coordinator = coordinator;
        this.offlineMapService = offlineMapService;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Apply localization
        ApplyLocalization();

        LangPicker.SelectedItem = lang.Current;

        var autoPlay = Preferences.Default.Get(AutoPlayKey, true);
        AutoPlaySwitch.IsToggled = autoPlay;
        UpdateAutoPlayInfo(autoPlay);

        UpdateMapCacheInfo();
    }

    // ══════════════════════════════════════════════════════════════
    // LOCALIZATION
    // ══════════════════════════════════════════════════════════════

    private void ApplyLocalization()
    {
        LblSettingsSubtitle.Text = loc.SettingsSubtitle;
        LblSettingsTitle.Text = loc.SettingsTitle;
        LblLangSection.Text = loc.Language;
        LblNarrationLanguage.Text = loc.NarrationLanguage;
        LangPicker.Title = loc.ChooseLanguage;
        LblAutoSection.Text = loc.AutoNarration;
        LblAutoPlay.Text = loc.AutoPlay;
        LblAutoPlayDesc.Text = loc.AutoPlayDesc;
        LblStatsSection.Text = loc.StatsHeatmap;
        LblHeatmapData.Text = loc.HeatmapData;
        LblHeatmapDesc.Text = loc.HeatmapDesc;
        LblOfflineMapSection.Text = loc.OfflineMapTitle;
        LblMapSaved.Text = loc.MapSaved;
        LblClearMapCache.Text = loc.ClearMapCache;
        LblClearMapCacheDesc.Text = loc.ClearMapCacheDesc;
        LblMapCacheInfo.Text = loc.MapCacheInfo;
        LblSecuritySection.Text = loc.SecurityAccess;
        LblClearQr.Text = loc.ClearQrSession;
        LblClearQrDesc.Text = loc.ClearQrSessionDesc;
        LblSaveSettings.Text = loc.SaveSettings;
    }

    // ══════════════════════════════════════════════════════════════
    // MAP CACHE
    // ══════════════════════════════════════════════════════════════

    private void UpdateMapCacheInfo()
    {
        try
        {
            int tiles = offlineMapService.GetCachedTileCount();
            long mb = offlineMapService.GetCacheSizeMB();

            MapCacheTileLabel.Text = string.Format(loc.TilesInfo, tiles, mb);
            MapCacheStatusLabel.Text = tiles > 0
                ? string.Format(loc.TilesCached, tiles)
                : loc.NoOfflineMap;
        }
        catch { }
    }

    private async void OnClearMapCacheTapped(object sender, TappedEventArgs e)
    {
        int tiles = offlineMapService.GetCachedTileCount();
        if (tiles == 0)
        {
            await DisplayAlert(loc.Notice, loc.EmptyCache, loc.OK);
            return;
        }

        bool confirm = await DisplayAlert(
            loc.ClearMapConfirm,
            string.Format(loc.ClearMapConfirmMsg, tiles, offlineMapService.GetCacheSizeMB()),
            loc.Delete, loc.Cancel);

        if (!confirm) return;

        offlineMapService.ClearMapCache();
        UpdateMapCacheInfo();
        await DisplayAlert(loc.Success, loc.MapCacheCleared, loc.OK);
    }

    // ══════════════════════════════════════════════════════════════
    // QR SESSION
    // ══════════════════════════════════════════════════════════════

    private async void OnClearQrSessionTapped(object sender, TappedEventArgs e)
    {
        var confirm = await DisplayAlert(
            loc.ClearQrConfirmTitle,
            loc.ClearQrConfirmMsg,
            loc.Delete, loc.Cancel);

        if (!confirm) return;

        Preferences.Default.Remove(QrGateUntilKey);
        await DisplayAlert(loc.Success, loc.ClearQrSuccess, loc.OK);
    }

    // ══════════════════════════════════════════════════════════════
    // AUTO PLAY TOGGLE
    // ══════════════════════════════════════════════════════════════

    private void OnAutoPlayToggled(object sender, ToggledEventArgs e)
    {
        var prevValue = Preferences.Default.Get(AutoPlayKey, true);
        Preferences.Default.Set(AutoPlayKey, e.Value);
        UpdateAutoPlayInfo(e.Value);

        if (e.Value && !prevValue)
        {
            System.Diagnostics.Debug.WriteLine("🔄 [Settings] Auto-play ON → Reset GeofencingEngine");
            geo.Reset();
        }
    }

    private void UpdateAutoPlayInfo(bool isEnabled)
    {
        if (isEnabled)
        {
            AutoPlayInfoIcon.Text = "✅";
            AutoPlayInfoLabel.Text = loc.AutoPlayOn;
            AutoPlayInfoBorder.BackgroundColor = Color.FromArgb("#F0F7FF");
            AutoPlayInfoBorder.Stroke = new SolidColorBrush(Color.FromArgb("#E3F2FD"));
            AutoPlayInfoLabel.TextColor = Color.FromArgb("#1565C0");
        }
        else
        {
            AutoPlayInfoIcon.Text = "🔕";
            AutoPlayInfoLabel.Text = loc.AutoPlayOff;
            AutoPlayInfoBorder.BackgroundColor = Color.FromArgb("#FFF8F0");
            AutoPlayInfoBorder.Stroke = new SolidColorBrush(Color.FromArgb("#FFE0B2"));
            AutoPlayInfoLabel.TextColor = Color.FromArgb("#E65100");
        }
    }

    // ══════════════════════════════════════════════════════════════
    // SAVE
    // ══════════════════════════════════════════════════════════════

    private async void Save(object sender, EventArgs e)
    {
        var selected = LangPicker.SelectedItem?.ToString() ?? "en";
        if (selected == lang.Current)
        {
            await DisplayAlert(loc.Notice, loc.LanguageUnchanged, loc.OK);
            return;
        }

        // Cập nhật language (LanguageService sẽ fire OnLanguageChanged event)
        lang.Current = selected;

        await DisplayAlert(loc.Notice, loc.Reloading, loc.OK);

        // RESET TOÀN BỘ
        repo.ClearCache();
        tracking.Stop();
        narration.Reset();
        geo.Reset();
        heatmap.Reset();
        coordinator.Reset();

        await routeTracking.FlushOnAppClosingAsync();
        routeTracking.Reset();

        // Notify localization service
        loc.NotifyChanged();

        Application.Current!.MainPage = new LoadingPage(repo, tracking);
    }
}
