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
        AudioCoordinator coordinator)
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
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LangPicker.SelectedItem = lang.Current;

        // Yêu cầu 2: Đọc trạng thái auto-play từ Preferences, mặc định = true
        var autoPlay = Preferences.Default.Get(AutoPlayKey, true);
        AutoPlaySwitch.IsToggled = autoPlay;
        UpdateAutoPlayInfo(autoPlay);
    }

    // ─────────────────────────────────────────────────────────────────
    // Yêu cầu 2: Gạt Switch → lưu ngay vào Preferences
    // Yêu cầu 1: Khi BẬT lại → reset GeofencingEngine để trigger ngay khi vào radius
    // ─────────────────────────────────────────────────────────────────
    private void OnAutoPlayToggled(object sender, ToggledEventArgs e)
    {
        var prevValue = Preferences.Default.Get(AutoPlayKey, true);

        Preferences.Default.Set(AutoPlayKey, e.Value);
        UpdateAutoPlayInfo(e.Value);

        // ── Yêu cầu 1: Bật lại auto-play → reset geo state ngay lập tức ──
        // TrackingService cũng detect điều này trong vòng lặp của nó,
        // nhưng reset ở đây để đảm bảo tức thì (không chờ chu kỳ tiếp theo).
        if (e.Value && !prevValue)
        {
            System.Diagnostics.Debug.WriteLine(
                "🔄 [Settings] Auto-play turned ON → Reset GeofencingEngine");
            geo.Reset();
        }
    }

    /// <summary>
    /// Cập nhật nhãn mô tả bên dưới Switch theo trạng thái hiện tại.
    /// </summary>
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

        // 🔥 Flush route session trước khi reset (nếu đang có session dang dở)
        await routeTracking.FlushOnAppClosingAsync();
        routeTracking.Reset();

        Application.Current!.MainPage = new LoadingPage(repo, tracking);
    }
}
