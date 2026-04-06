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
    private double progressBarTotalWidth = 0;

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

        // 🔥 Realtime progress → update fill bar width
        audio.OnProgress += sec =>
        {
            if (isSeeking) return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                UpdateProgressFill(sec);
                ProgressSlider.Value = sec;
                CurrentTimeLabel.Text = Format(sec);

                if (duration > 0)
                {
                    var remaining = Math.Max(0, duration - sec);
                    RemainingTimeLabel.Text = Format(remaining);
                }
            });
        };

        audio.OnDuration += d =>
        {
            duration = d;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                ProgressSlider.Maximum = d;
                TotalTimeLabel.Text = Format(d);
                RemainingTimeLabel.Text = Format(d);
            });
        };

        // 🔥 Audio completed naturally → reset to start
        audio.OnCompleted += () =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                hasStarted = false;
                SetPlayingState(false);
                ResetProgressUI();
            });
        };
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        // Measure the actual width of the progress background track
        // The track is inside the Border which has Margin="20,0,20,0" so subtract padding
        var trackWidth = width - 40; // 20 left + 20 right padding
        if (trackWidth > 0)
            progressBarTotalWidth = trackWidth;
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

    // ================= BACK NAVIGATION =================

    private async void OnBackTapped(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    // ================= AUDIO =================

    /// <summary>
    /// Unified Play/Pause handler.
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
            // Paused → resume
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
        ResetProgressUI();
    }

    private void SetPlayingState(bool isPlaying)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            PlayBtn.Text = isPlaying ? "⏸" : "▶";
            PlayBtn.FontSize = isPlaying ? 22 : 26;

            PlayBtnContainer.IsVisible = true;
            PauseBtnContainer.IsVisible = false;
            PlayBtn.IsEnabled = true;
            PauseBtn.IsEnabled = isPlaying;

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
        UpdateProgressFill(ProgressSlider.Value);

        // Resume tracking after seek
        if (audio.IsPlaying && poi != null)
        {
            // tracker restart handled inside PoiDetailAudioManager.Seek
        }
    }

    private void OnSeek(object sender, ValueChangedEventArgs e)
    {
        if (isSeeking)
        {
            CurrentTimeLabel.Text = Format(e.NewValue);
            UpdateProgressFill(e.NewValue);

            if (duration > 0)
                RemainingTimeLabel.Text = Format(Math.Max(0, duration - e.NewValue));

            audio.Seek(e.NewValue);
        }
    }

    // ================= SKIP =================

    private void OnSkipBack(object sender, EventArgs e)
    {
        var current = ProgressSlider.Value;
        var target = Math.Max(0, current - SkipSeconds);

        audio.Seek(target);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            ProgressSlider.Value = target;
            CurrentTimeLabel.Text = Format(target);
            UpdateProgressFill(target);
        });
    }

    private void OnSkipForward(object sender, EventArgs e)
    {
        var current = ProgressSlider.Value;
        var target = current + SkipSeconds;

        if (duration > 0 && target > duration)
        {
            target = 0;
            audio.Stop();
            hasStarted = false;
            SetPlayingState(false);
            ResetProgressUI();
            return;
        }

        audio.Seek(target);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            ProgressSlider.Value = target;
            CurrentTimeLabel.Text = Format(target);
            UpdateProgressFill(target);
        });
    }

    // ================= PROGRESS BAR FILL =================

    /// <summary>
    /// 🔥 Updates the animated gradient fill bar width based on current playback position.
    /// Must be called on MainThread.
    /// </summary>
    private void UpdateProgressFill(double sec)
    {
        if (duration <= 0 || progressBarTotalWidth <= 0) return;

        var fraction = Math.Clamp(sec / duration, 0, 1);
        var fillWidth = fraction * progressBarTotalWidth;

        ProgressFill.WidthRequest = fillWidth;
    }

    private void ResetProgressUI()
    {
        ProgressSlider.Value = 0;
        CurrentTimeLabel.Text = "00:00";
        RemainingTimeLabel.Text = duration > 0 ? Format(duration) : "00:00";
        ProgressFill.WidthRequest = 0;
    }

    // ================= TAB UX =================

    private void ShowOverview(object sender, EventArgs e)
    {
        OverviewSection.IsVisible = true;
        FoodList.IsVisible = false;
        AudioBar.IsVisible = true;

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

        MenuTabBorder.Background = new SolidColorBrush(Color.FromArgb("#F5F6FA"));
        MenuTabBorder.StrokeThickness = 1;
        MenuTab.TextColor = Color.FromArgb("#9B9BAA");
        MenuTab.FontAttributes = FontAttributes.None;
    }

    private void ShowMenu(object sender, EventArgs e)
    {
        OverviewSection.IsVisible = false;
        FoodList.IsVisible = true;
        AudioBar.IsVisible = false;

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

        OverviewTabBorder.Background = new SolidColorBrush(Color.FromArgb("#F5F6FA"));
        OverviewTabBorder.StrokeThickness = 1;
        OverviewTab.TextColor = Color.FromArgb("#9B9BAA");
        OverviewTab.FontAttributes = FontAttributes.None;
    }

    // ================= UTIL =================

    private string Format(double sec)
    {
        var t = TimeSpan.FromSeconds(sec);
        return $"{(int)t.TotalMinutes:D2}:{t.Seconds:D2}";
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
