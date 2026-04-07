using SmartTourApp.Services;

namespace SmartTourApp.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly LanguageService lang;
    private readonly PoiRepository repo;
    private readonly TrackingService tracking;
    private readonly NarrationEngine narration;
    private readonly GeofencingEngine geo;
    private readonly HeatmapService heatmap;    // 🔥

    public SettingsPage(
        LanguageService lang,
        PoiRepository repo,
        TrackingService tracking,
        NarrationEngine narration,
        GeofencingEngine geo,
        HeatmapService heatmap)                // 🔥
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
    }

    private async void Save(object sender, EventArgs e)
    {
        var selected = LangPicker.SelectedItem?.ToString() ?? "vi";

        if (selected == lang.Current)
        {
            await DisplayAlertAsync("Thông báo", "Ngôn ngữ không thay đổi", "OK");
            return;
        }

        lang.Current = selected;

        await DisplayAlertAsync("Thông báo", "Đang tải lại ứng dụng...", "OK");

        // 🔥 RESET TOÀN BỘ
        repo.ClearCache();
        tracking.Stop();
        narration.Reset();
        geo.Reset();
        heatmap.Reset();    // 🔥 reset state machine + cooldown để app_open check lại

        Application.Current!.MainPage = new LoadingPage(repo, tracking);
    }
}
