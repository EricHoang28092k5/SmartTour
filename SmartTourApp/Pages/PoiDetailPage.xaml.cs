using SmartTour.Shared.Models;
using SmartTourApp.Services;
namespace SmartTourApp.Pages;

[QueryProperty(nameof(Poi), "poi")]
public partial class PoiDetailPage : ContentPage
{
    private Poi poi;
    private readonly PoiDetailAudioManager audio;

    private bool isSeeking = false;
    private double duration = 0;

    // Track whether audio has been started at least once (for resume vs. fresh play)
    private bool hasStarted = false;

    private const double SkipSeconds = 5.0;

    public Poi Poi
    {
        get => poi;
        set
        {
            poi = value;
            BindData();
        }
    }

    public PoiDetailPage(PoiDetailAudioManager audio)
    {
        InitializeComponent();
        this.audio = audio;

        audio.OnProgress += sec =>
        {
            if (isSeeking) return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                ProgressSlider.Value = sec;
                CurrentTimeLabel.Text = Format(sec);
            });
        };

        audio.OnDuration += d =>
        {
            duration = d;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                ProgressSlider.Maximum = d;
                TotalTimeLabel.Text = Format(d);
            });
        };
    }

    private void BindData()
    {
        if (poi == null) return;

        if (poi.Foods != null && poi.Foods.Any())
            FoodList.ItemsSource = poi.Foods;

        PoiName.Text = poi.Name;
        PoiImage.Source = poi.ImageUrl;
        PoiDescription.Text = string.IsNullOrWhiteSpace(poi.Description)
            ? "Chưa có mô tả"
            : poi.Description;

        UpdateOpenStatus();
    }

    // ================= AUDIO =================

    /// <summary>
    /// Unified Play/Pause handler.
    /// - First press → start fresh audio
    /// - While playing → pause (resume position kept)
    /// - While paused → resume from current position
    /// </summary>
    private async void OnPlayClicked(object sender, EventArgs e)
    {
        if (poi == null) return;

        if (!hasStarted)
        {
            // First time: start fresh
            await audio.Play(poi);
            hasStarted = true;
            SetPlayingState(true);
        }
        else if (audio.IsPlaying)
        {
            // Currently playing → pause
            audio.Pause();
            SetPlayingState(false);
        }
        else
        {
            // Paused → resume from current position (ExoPlayer's Play() continues from where it left off)
            audio.Resume();
            SetPlayingState(true);
        }
    }

    /// <summary>
    /// Kept for XAML binding compatibility (hidden button).
    /// </summary>
    private void OnPauseClicked(object sender, EventArgs e)
    {
        audio.Pause();
        SetPlayingState(false);
    }

    private void OnStopClicked(object sender, EventArgs e)
    {
        audio.Stop();
        hasStarted = false;
        SetPlayingState(false);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            ProgressSlider.Value = 0;
            CurrentTimeLabel.Text = "00:00";
        });
    }

    /// <summary>
    /// Toggles the Play button icon between ▶ and ⏸.
    /// PauseBtnContainer kept invisible for layout compatibility.
    /// </summary>
    private void SetPlayingState(bool isPlaying)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            PlayBtn.Text = isPlaying ? "⏸" : "▶";
            PlayBtn.FontSize = isPlaying ? 22 : 26;

            // Keep hidden elements in sync for code compatibility
            PlayBtnContainer.IsVisible = true;   // always visible — unified button
            PauseBtnContainer.IsVisible = false;
            PlayBtn.IsEnabled = true;
            PauseBtn.IsEnabled = isPlaying;

            // Show/hide stop text button
            StopBtn.IsVisible = hasStarted;
        });
    }

    // ================= SEEK =================

    private void OnSeekStarted(object sender, EventArgs e)
    {
        isSeeking = true;
    }

    private void OnSeekCompleted(object sender, EventArgs e)
    {
        isSeeking = false;
        audio.Seek(ProgressSlider.Value);
    }

    private void OnSeek(object sender, ValueChangedEventArgs e)
    {
        if (isSeeking)
        {
            CurrentTimeLabel.Text = Format(e.NewValue);
            audio.Seek(e.NewValue);
        }
    }

    // ================= SKIP =================

    /// <summary>
    /// Skip back 5 seconds. If result < 0, seek to 0.
    /// </summary>
    private void OnSkipBack(object sender, EventArgs e)
    {
        var current = ProgressSlider.Value;
        var target = current - SkipSeconds;

        // Clamp to start
        if (target < 0) target = 0;

        audio.Seek(target);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            ProgressSlider.Value = target;
            CurrentTimeLabel.Text = Format(target);
        });
    }

    /// <summary>
    /// Skip forward 5 seconds. If result > duration, wrap back to 0.
    /// </summary>
    private void OnSkipForward(object sender, EventArgs e)
    {
        var current = ProgressSlider.Value;
        var target = current + SkipSeconds;

        // Wrap to beginning if past duration
        if (duration > 0 && target > duration)
        {
            target = 0;
            // Stop playback and reset so user can replay
            audio.Stop();
            hasStarted = false;
            SetPlayingState(false);
        }

        audio.Seek(target);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            ProgressSlider.Value = target;
            CurrentTimeLabel.Text = Format(target);
        });
    }

    // ================= TAB UX =================

    private void ShowOverview(object sender, EventArgs e)
    {
        OverviewSection.IsVisible = true;
        FoodList.IsVisible = false;
        AudioBar.IsVisible = true;

        // Overview tab: gradient active style
        OverviewTabBorder.Background = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(Color.FromArgb("#1976D2"), 0f),
                new GradientStop(Color.FromArgb("#5B8CFF"), 1f)
            },
            new Point(0, 0), new Point(1, 1));
        OverviewTabBorder.StrokeThickness = 0;
        OverviewTab.TextColor = Colors.White;
        OverviewTab.FontAttributes = FontAttributes.Bold;

        // Menu tab: inactive style
        MenuTabBorder.Background = new SolidColorBrush(Color.FromArgb("#1A1E2E"));
        MenuTabBorder.StrokeThickness = 1;
        MenuTab.TextColor = Color.FromArgb("#888888");
        MenuTab.FontAttributes = FontAttributes.None;
    }

    private void ShowMenu(object sender, EventArgs e)
    {
        OverviewSection.IsVisible = false;
        FoodList.IsVisible = true;
        AudioBar.IsVisible = false; // 🔥 UX requirement: audio bar hidden on menu tab

        // Menu tab: active
        MenuTabBorder.Background = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(Color.FromArgb("#1976D2"), 0f),
                new GradientStop(Color.FromArgb("#5B8CFF"), 1f)
            },
            new Point(0, 0), new Point(1, 1));
        MenuTabBorder.StrokeThickness = 0;
        MenuTab.TextColor = Colors.White;
        MenuTab.FontAttributes = FontAttributes.Bold;

        // Overview tab: inactive
        OverviewTabBorder.Background = new SolidColorBrush(Color.FromArgb("#1A1E2E"));
        OverviewTabBorder.StrokeThickness = 1;
        OverviewTab.TextColor = Color.FromArgb("#888888");
        OverviewTab.FontAttributes = FontAttributes.None;
    }

    // ================= UTIL =================

    private string Format(double sec)
    {
        var t = TimeSpan.FromSeconds(sec);
        return $"{t.Minutes:D2}:{t.Seconds:D2}";
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        audio.Stop();
        hasStarted = false;
    }

    // ================= IMAGE VIEWER =================

    private async void OnFoodImageTapped(object sender, TappedEventArgs e)
    {
        if (sender is Microsoft.Maui.Controls.Image img && img.BindingContext is Food food)
        {
            PreviewImage.Source = food.ImageUrl;
            ImageViewer.IsVisible = true;

            await Task.WhenAll(
                ImageViewer.FadeTo(1, 150),
                PreviewImage.ScaleTo(1, 200)
            );
        }
    }

    private async void CloseImageViewer(object sender, EventArgs e)
    {
        await Task.WhenAll(
            ImageViewer.FadeTo(0, 150),
            PreviewImage.ScaleTo(0.8, 150)
        );

        ImageViewer.IsVisible = false;
    }

    // ================= OPEN STATUS =================

    private void UpdateOpenStatus()
    {
        if (poi == null || poi.OpenTime == null || poi.CloseTime == null)
            return;

        var now = DateTime.Now.TimeOfDay;
        var open = poi.OpenTime.Value;
        var close = poi.CloseTime.Value;

        if (open == close)
        {
            OpenStatus.Text = "Mở cả ngày";
            OpenStatus.TextColor = Color.FromArgb("#00AAFF");
            OpenStatusBadge.BackgroundColor = Color.FromArgb("#1A00AAFF");
            OpenStatusBadge.Stroke = new SolidColorBrush(Color.FromArgb("#4400AAFF"));
            return;
        }

        if (now >= open && now <= close)
        {
            OpenStatus.Text = "Đang mở cửa";
            OpenStatus.TextColor = Color.FromArgb("#00FF88");
            OpenStatusBadge.BackgroundColor = Color.FromArgb("#1A00FF88");
            OpenStatusBadge.Stroke = new SolidColorBrush(Color.FromArgb("#4400FF88"));
            OpenDetail.Text = $"Đóng lúc {close:hh\\:mm}";
        }
        else
        {
            OpenStatus.Text = "Đã đóng cửa";
            OpenStatus.TextColor = Color.FromArgb("#FF6B6B");
            OpenStatusBadge.BackgroundColor = Color.FromArgb("#1AFF6B6B");
            OpenStatusBadge.Stroke = new SolidColorBrush(Color.FromArgb("#44FF6B6B"));
            OpenDetail.Text = $"Mở lúc {open:hh\\:mm}";
        }
    }
}
