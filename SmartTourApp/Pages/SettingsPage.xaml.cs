using SmartTourApp.Services;

namespace SmartTourApp.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly LanguageService lang;
    private readonly PoiRepository repo;
    private readonly TrackingService tracking;
    private readonly NarrationEngine narration;
    private readonly GeofencingEngine geo;
    private readonly HeatmapService heatmap;
    private readonly RouteTrackingService routeTracking;
    private readonly AudioCoordinator coordinator;
    private readonly OfflineMapService offlineMapService;  // 🔥 NEW

    // Yêu cầu 2: Key lưu trạng thái auto-play trong Preferences
    public const string AutoPlayKey = "auto_play_enabled";

    public SettingsPage(
        LanguageService lang,
        PoiRepository repo,
        TrackingService tracking,
        NarrationEngine narration,
        GeofencingEngine geo,
        HeatmapService heatmap,
        RouteTrackingService routeTracking,
        AudioCoordinator coordinator,
        OfflineMapService offlineMapService)   // 🔥 inject
    {
        InitializeComponent();
        this.lang = lang;
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
        LangPicker.SelectedItem = lang.Current;

        // Yêu cầu 2: Đọc trạng thái auto-play từ Preferences, mặc định = true
        var autoPlay = Preferences.Default.Get(AutoPlayKey, true);
        AutoPlaySwitch.IsToggled = autoPlay;
        UpdateAutoPlayInfo(autoPlay);

        // 🔥 Cập nhật thông tin map cache
        UpdateMapCacheInfo();
    }

    // ───────────────────────────────────────────────────────────────────
    // 🔥 MAP CACHE INFO — Refresh stats
    // ───────────────────────────────────────────────────────────────────
    private void UpdateMapCacheInfo()
    {
        try
        {
            int tiles = offlineMapService.GetCachedTileCount();
            long mb = offlineMapService.GetCacheSizeMB();

            // Update UI labels (nếu XAML có những control này — xem SettingsPage.xaml)
            if (MapCacheTileLabel != null)
                MapCacheTileLabel.Text = $"{tiles:N0} tiles (~{mb}MB)";

            if (MapCacheStatusLabel != null)
                MapCacheStatusLabel.Text = tiles > 0
                    ? $"✅ {tiles} tiles đã lưu"
                    : "Chưa có bản đồ offline";
        }
        catch { }
    }

    // ───────────────────────────────────────────────────────────────────
    // 🔥 CLEAR MAP CACHE
    // ───────────────────────────────────────────────────────────────────
    private async void OnClearMapCacheTapped(object sender, TappedEventArgs e)
    {
        int tiles = offlineMapService.GetCachedTileCount();
        if (tiles == 0)
        {
            await DisplayAlert("Thông báo", "Cache bản đồ đang trống.", "OK");
            return;
        }

        bool confirm = await DisplayAlert(
            "Xóa bản đồ offline",
            $"Xóa {tiles} tiles ({offlineMapService.GetCacheSizeMB()}MB)?\n" +
            "Bản đồ sẽ cần tải lại khi có mạng.",
            "Xóa", "Hủy");

        if (!confirm) return;

        offlineMapService.ClearMapCache();
        UpdateMapCacheInfo();
        await DisplayAlert("Thành công", "Đã xóa cache bản đồ.", "OK");
    }

    // ───────────────────────────────────────────────────────────────────
    // Yêu cầu 2: Gạt Switch → lưu ngay vào Preferences
    // Yêu cầu 1: Khi BẬT lại → reset GeofencingEngine để trigger ngay
    // ───────────────────────────────────────────────────────────────────
    private void OnAutoPlayToggled(object sender, ToggledEventArgs e)
    {
        var prevValue = Preferences.Default.Get(AutoPlayKey, true);
        Preferences.Default.Set(AutoPlayKey, e.Value);
        UpdateAutoPlayInfo(e.Value);

        if (e.Value && !prevValue)
        {
            System.Diagnostics.Debug.WriteLine(
                "🔄 [Settings] Auto-play turned ON → Reset GeofencingEngine");
            geo.Reset();
        }
    }

    private void UpdateAutoPlayInfo(bool isEnabled)
    {
        if (isEnabled)
        {
            AutoPlayInfoIcon.Text = "✅";
            AutoPlayInfoLabel.Text = "Thuyết minh sẽ tự động phát khi bạn tiếp cận điểm tham quan.";
            AutoPlayInfoBorder.BackgroundColor = Color.FromArgb("#F0F7FF");
            AutoPlayInfoBorder.Stroke = new SolidColorBrush(Color.FromArgb("#E3F2FD"));
            AutoPlayInfoLabel.TextColor = Color.FromArgb("#1565C0");
        }
        else
        {
            AutoPlayInfoIcon.Text = "🔕";
            AutoPlayInfoLabel.Text = "Tự động phát đã tắt. Heatmap vẫn được ghi nhận.";
            AutoPlayInfoBorder.BackgroundColor = Color.FromArgb("#FFF8F0");
            AutoPlayInfoBorder.Stroke = new SolidColorBrush(Color.FromArgb("#FFE0B2"));
            AutoPlayInfoLabel.TextColor = Color.FromArgb("#E65100");
        }
    }

    private async void Save(object sender, EventArgs e)
    {
        var selected = LangPicker.SelectedItem?.ToString() ?? "vi";
        if (selected == lang.Current)
        {
            await DisplayAlert("Thông báo", "Ngôn ngữ không thay đổi", "OK");
            return;
        }

        lang.Current = selected;
        await DisplayAlert("Thông báo", "Đang tải lại ứng dụng...", "OK");

        // RESET TOÀN BỘ
        repo.ClearCache();
        tracking.Stop();
        narration.Reset();
        geo.Reset();
        heatmap.Reset();
        coordinator.Reset();

        await routeTracking.FlushOnAppClosingAsync();
        routeTracking.Reset();

        Application.Current!.MainPage = new LoadingPage(repo, tracking);
    }
}
