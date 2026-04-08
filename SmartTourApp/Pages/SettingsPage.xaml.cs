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

    // Yêu cầu 2: Key lưu trạng thái auto-play trong Preferences
    public const string AutoPlayKey = "auto_play_enabled";

    public SettingsPage(
        LanguageService lang,
        PoiRepository repo,
        TrackingService tracking,
        NarrationEngine narration,
        GeofencingEngine geo,
        HeatmapService heatmap)
    {
        InitializeComponent();
        this.lang = lang;
        this.repo = repo;
        this.tracking = tracking;
        this.narration = narration;
        this.geo = geo;
        this.heatmap = heatmap;
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
    // TrackingService sẽ đọc lại giá trị này trong mỗi chu kỳ tracking
    // → không cần restart app, cập nhật ngay lập tức
    // ─────────────────────────────────────────────────────────────────
    private void OnAutoPlayToggled(object sender, ToggledEventArgs e)
    {
        // Lưu ngay vào Preferences — TrackingService đọc realtime
        Preferences.Default.Set(AutoPlayKey, e.Value);

        // Cập nhật UI mô tả trạng thái
        UpdateAutoPlayInfo(e.Value);
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

        Application.Current!.MainPage = new LoadingPage(repo, tracking);
    }
}
