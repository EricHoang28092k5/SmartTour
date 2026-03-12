using SmartTourApp.Services;

namespace SmartTourApp.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly LanguageService lang;

    public SettingsPage(LanguageService lang)
    {
        InitializeComponent();

        this.lang = lang;

        LangPicker.SelectedItem = lang.Current;
    }

    private void Save(object sender, EventArgs e)
    {
        lang.Current = LangPicker.SelectedItem?.ToString() ?? "vi";
    }
}