using SmartTourApp.Services;

namespace SmartTourApp.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly LanguageService lang;

    public SettingsPage(LanguageService lang)
    {
        InitializeComponent();
        this.lang = lang;
    }

    // ✅ luôn sync khi mở lại page
    protected override void OnAppearing()
    {
        base.OnAppearing();
        LangPicker.SelectedItem = lang.Current;
    }

    private async void Save(object sender, EventArgs e)
    {
        lang.Current = LangPicker.SelectedItem?.ToString() ?? "vi";

        await DisplayAlertAsync("Thông báo", "Đã lưu ngôn ngữ", "OK");
    }
}