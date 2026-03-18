using SmartTourApp.Services;

namespace SmartTourApp.Pages;

public partial class LoadingPage : ContentPage
{
    private readonly PoiRepository repo;
    private readonly TrackingService tracking;
    private readonly LanguageService lang;
    private readonly TtsService tts;

    private int currentStep = 0;
    private const int totalSteps = 5;

    public LoadingPage(
        PoiRepository repo,
        TrackingService tracking,
        LanguageService lang,
        TtsService tts)
    {
        InitializeComponent();

        this.repo = repo;
        this.tracking = tracking;
        this.lang = lang;
        this.tts = tts;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await RunAnimation();
        await InitApp();
    }

    // =============================
    // ANIMATION
    // =============================
    private async Task RunAnimation()
    {
        await Task.WhenAll(
            Logo.FadeTo(1, 600),
            Logo.ScaleTo(1, 600, Easing.CubicOut)
        );
    }

    // =============================
    // INIT APP
    // =============================
    private async Task InitApp()
    {
        try
        {
            await Step("Xin quyền GPS...", async () =>
            {
                var status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();

                if (status != PermissionStatus.Granted)
                    throw new Exception("Cần quyền GPS để chạy app");
            });

            await Step("Đang tải địa điểm thú vị...", async () =>
            {
                await repo.GetPois();
                await Task.Delay(300);
            });

            await Step("Đang tải cài đặt...", async () =>
            {
                _ = lang.Current;
                await Task.Delay(200);
            });

            await Step("Chuẩn bị giọng nói...", async () =>
            {
                await tts.Speak(" ", "vi"); // warmup
                await Task.Delay(200);
            });

            await Step("Bắt đầu tracking...", async () =>
            {
                Task.Run(async () =>
                {
                    try
                    {
                        await tracking.Start();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(ex.Message);
                    }
                });
            });

            await Task.Delay(500);

            // fade out
            await this.FadeTo(0, 400);

            Application.Current.MainPage = new AppShell();

            Application.Current.MainPage.Opacity = 0;
            await Application.Current.MainPage.FadeTo(1, 400);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Lỗi", ex.Message, "OK");
        }
    }

    // =============================
    // STEP
    // =============================
    private async Task Step(string text, Func<Task> action)
    {
        UpdateStatus(text);

        await action();

        await IncreaseProgress();
    }

    // =============================
    // PROGRESS
    // =============================
    private async Task IncreaseProgress()
    {
        currentStep++;

        double progress = (double)currentStep / totalSteps;

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await ProgressBar.ProgressTo(progress, 300, Easing.Linear);
            PercentLabel.Text = $"{(int)(progress * 100)}%";
        });
    }

    // =============================
    // STATUS
    // =============================
    private void UpdateStatus(string text)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StatusLabel.Text = text;
        });
    }
}