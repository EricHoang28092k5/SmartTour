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
    private readonly OfflineSyncService offlineSync;

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
        OfflineMapService offlineMapService,
        OfflineSyncService offlineSync)
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
        this.offlineSync = offlineSync;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

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
    // SAVE — YC4: Hỗ trợ đổi ngôn ngữ khi offline
    // ══════════════════════════════════════════════════════════════

    private async void Save(object sender, EventArgs e)
    {
        var selected = LangPicker.SelectedItem?.ToString() ?? "en";

        if (selected == lang.Current)
        {
            await DisplayAlert(loc.Notice, loc.LanguageUnchanged, loc.OK);
            return;
        }

        // YC4: Kiểm tra online/offline để thông báo phù hợp
        bool isOnline = IsOnline();

        // Cập nhật ngôn ngữ (LanguageService fire OnLanguageChanged)
        lang.Current = selected;

        if (!isOnline)
        {
            // YC4: Offline → thông báo dữ liệu tĩnh đã đổi, dữ liệu động dùng cache SQLite
            await DisplayAlert(
                loc.Notice,
                GetOfflineLangChangeMessage(selected),
                loc.OK);
        }
        else
        {
            await DisplayAlert(loc.Notice, loc.Reloading, loc.OK);
        }

        // RESET TOÀN BỘ — kể cả offline
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

        // YC4: Offline mode → reload với SQLite cache, không cần API
        Application.Current!.MainPage = new LoadingPage(repo, tracking);
    }

    /// <summary>
    /// YC4: Message phù hợp khi đổi ngôn ngữ offline
    /// </summary>
    private static string GetOfflineLangChangeMessage(string langCode)
    {
        // Bản địa hóa thông báo theo ngôn ngữ MỚI được chọn
        return langCode switch
        {
            "en" => "Language changed to English. Using cached data (offline mode). UI will reload.",
            "ja" => "言語を日本語に変更しました。キャッシュデータを使用します（オフラインモード）。",
            "zh" => "语言已更改为中文。使用缓存数据（离线模式）。",
            "ko" => "언어가 한국어로 변경되었습니다. 캐시된 데이터를 사용합니다(오프라인 모드).",
            _ => "Đã đổi ngôn ngữ. Đang dùng dữ liệu đã tải (chế độ offline). UI sẽ tải lại."
        };
    }

    private static bool IsOnline()
    {
        try
        {
            var access = Connectivity.Current.NetworkAccess;
            return access == NetworkAccess.Internet ||
                   access == NetworkAccess.ConstrainedInternet;
        }
        catch { return false; }
    }
}
