namespace SmartTourApp;

public partial class App : Application
{
    private readonly MainPage _page;

    public App(MainPage page)
    {
        InitializeComponent();
        _page = page;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(_page);
    }
}