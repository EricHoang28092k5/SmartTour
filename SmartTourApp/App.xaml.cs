using SmartTourApp.Pages;

namespace SmartTourApp;

public partial class App : Application
{
    public App(LoadingPage loadingPage)
    {
        InitializeComponent();

        MainPage = loadingPage;
    }
}