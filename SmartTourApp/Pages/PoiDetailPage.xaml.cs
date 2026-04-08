using SmartTour.Shared.Models;
using SmartTourApp.Services;

namespace SmartTourApp.Pages;

[QueryProperty(nameof(Poi), "poi")]
public partial class PoiDetailPage : ContentPage
{
    private Poi poi;
    private readonly PoiDetailAudioManager audio;
    private readonly LocationService locationService;

    private bool isSeeking = false;
    private double duration = 0;

    // Track whether audio has been started at least once (for resume vs. fresh play)
    private bool hasStarted = false;

    private const double SkipSeconds = 5.0;

    // ── track nguồn mở trang để back đúng chỗ ──
    private string _openedFrom = "";

    // ── CancellationToken để dừng vòng lặp cập nhật slider ──
    private CancellationTokenSource? _sliderCts;

    // ── Width của progress track để tính fill ──
    private double _progressTrackWidth = 0;

    // ── Cached user location (lấy 1 lần khi Play, dùng cho RouteTracking) ──
    private Location? _cachedUserLocation;

    public Poi Poi
    {
        get => poi;
        set
        {
            poi = value;
            BindData();
        }
    }

    public PoiDetailPage(PoiDetailAudioManager audio, LocationService locationService)
    {
        InitializeComponent();
        this.audio = audio;
        this.locationService = locationService;

        // ── Nhận callback OnProgress từ audio engine ──
        audio.OnProgress += sec =>
        {
            if (isSeeking) return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                UpdateSliderUI(sec);
            });
        };

        // ── Nhận tổng thời lượng ──
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

        // ── Audio hoàn thành tự nhiên → reset UI ──
        audio.OnCompleted += () =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                hasStarted = false;
                SetPlayingState(false);
                UpdateSliderUI(0);
            });
        };
    }

    // ── Nhận thông tin nguồn mở từ caller ──
    public void SetOpenedFrom(string source)
    {
        _openedFrom = source;
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

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Reset slider UI mỗi lần trang xuất hiện (tránh hiển thị trạng thái cũ)
        if (!hasStarted)
        {
            UpdateSliderUI(0);
            TotalTimeLabel.Text = "00:00";
        }

        // Lấy width thực của progress track sau khi layout xong
        ProgressFillContainer.SizeChanged += OnProgressContainerSizeChanged;

        // 🔥 Prefetch GPS để sẵn sàng cho RouteTracking khi user bấm Play
        _ = Task.Run(async () =>
        {
            _cachedUserLocation = await locationService.GetLocation();
        });
    }

    private void OnProgressContainerSizeChanged(object? sender, EventArgs e)
    {
        var parent = ProgressFill.Parent as View;
        if (parent != null && parent.Width > 0)
            _progressTrackWidth = parent.Width;
    }

    // ── Nút back thông minh ──
    private async void OnBackTapped(object sender, TappedEventArgs e)
    {
        // Dừng audio khi rời trang
        audio.Stop();
        hasStarted = false;

        // Điều hướng về đúng trang nguồn
        if (_openedFrom == "map")
        {
            await Shell.Current.GoToAsync("//map");
        }
        else
        {
            if (Navigation.NavigationStack.Count > 1)
                await Navigation.PopAsync(animated: true);
            else
                await Shell.Current.GoToAsync("//home");
        }
    }

    // ═══════════════════════════════════════════
    // AUDIO CONTROLS
    // ═══════════════════════════════════════════

    /// <summary>
    /// Unified Play/Pause handler.
    /// - First press → start fresh audio (với location để RouteTracking check radius)
    /// - While playing → pause (resume position kept)
    /// - While paused → resume from current position
    /// </summary>
    private async void OnPlayClicked(object sender, EventArgs e)
    {
        if (poi == null) return;

        if (!hasStarted)
        {
            // 🔥 Lấy GPS mới nhất ngay lúc bấm Play để kiểm tra radius chính xác nhất
            // Dùng cached nếu không lấy được trong 2s (fallback)
            var loc = await GetFreshLocationAsync();

            // First time: start fresh — truyền location cho RouteTracking
            await audio.Play(poi, loc);
            hasStarted = true;
            SetPlayingState(true);
            StartSliderLoop();
        }
        else if (audio.IsPlaying)
        {
            // Currently playing → pause
            audio.Pause();
            SetPlayingState(false);
            StopSliderLoop();
        }
        else
        {
            // Paused → resume from current position (không trigger RouteTracking lại)
            audio.Resume();
            SetPlayingState(true);
            StartSliderLoop();
        }
    }

    /// <summary>
    /// Lấy GPS mới nhất với timeout 2s, fallback sang cached.
    /// </summary>
    private async Task<Location?> GetFreshLocationAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var fresh = await locationService.GetLocation();
            if (fresh != null)
            {
                _cachedUserLocation = fresh;
                return fresh;
            }
        }
        catch { }

        return _cachedUserLocation;
    }

    /// <summary>
    /// Kept for XAML binding compatibility (hidden button).
    /// </summary>
    private void OnPauseClicked(object sender, EventArgs e)
    {
        audio.Pause();
        SetPlayingState(false);
        StopSliderLoop();
    }

    private void OnStopClicked(object sender, EventArgs e)
    {
        audio.Stop();
        hasStarted = false;
        SetPlayingState(false);
        StopSliderLoop();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateSliderUI(0);
        });
    }

    /// <summary>
    /// Toggles the Play button icon between ▶ and ⏸.
    /// </summary>
    private void SetPlayingState(bool isPlaying)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            PlayBtn.Text = isPlaying ? "⏸" : "▶";
            PlayBtn.FontSize = isPlaying ? 20 : 24;

            PlayBtnContainer.IsVisible = true;
            PauseBtnContainer.IsVisible = false;
            PlayBtn.IsEnabled = true;
            PauseBtn.IsEnabled = isPlaying;

            StopBtn.IsVisible = hasStarted;
        });
    }

    // ═══════════════════════════════════════════
    // SLIDER LOOP — Real-time sync
    // ═══════════════════════════════════════════

    private void StartSliderLoop()
    {
        StopSliderLoop();

        _sliderCts = new CancellationTokenSource();
        var token = _sliderCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                if (!isSeeking && audio.IsPlaying)
                {
                    var currentSec = ProgressSlider.Value;

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        SyncProgressFill(currentSec);
                    });
                }

                try
                {
                    await Task.Delay(150, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    private void StopSliderLoop()
    {
        _sliderCts?.Cancel();
        _sliderCts?.Dispose();
        _sliderCts = null;
    }

    // ═══════════════════════════════════════════
    // SEEK HANDLING
    // ═══════════════════════════════════════════

    private void OnSeekStarted(object sender, EventArgs e)
    {
        isSeeking = true;
    }

    private void OnSeekCompleted(object sender, EventArgs e)
    {
        isSeeking = false;
        audio.Seek(ProgressSlider.Value);

        if (audio.IsPlaying)
            StartSliderLoop();
    }

    private void OnSeek(object sender, ValueChangedEventArgs e)
    {
        if (isSeeking)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                CurrentTimeLabel.Text = Format(e.NewValue);
                SyncProgressFill(e.NewValue);
            });
        }
    }

    // ═══════════════════════════════════════════
    // SKIP
    // ═══════════════════════════════════════════

    private void OnSkipBack(object sender, EventArgs e)
    {
        var current = ProgressSlider.Value;
        var target = Math.Max(0, current - SkipSeconds);

        audio.Seek(target);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            ProgressSlider.Value = target;
            UpdateSliderUI(target);
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
            StopSliderLoop();
        }

        audio.Seek(target);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            ProgressSlider.Value = target;
            UpdateSliderUI(target);
        });
    }

    // ═══════════════════════════════════════════
    // UI HELPERS
    // ═══════════════════════════════════════════

    private void UpdateSliderUI(double sec)
    {
        if (ProgressSlider.Maximum > 0)
            ProgressSlider.Value = sec;

        CurrentTimeLabel.Text = Format(sec);

        var remaining = duration > 0 ? Math.Max(0, duration - sec) : 0;
        RemainingTimeLabel.Text = Format(remaining);

        SyncProgressFill(sec);
    }

    private void SyncProgressFill(double sec)
    {
        if (duration <= 0) return;

        double trackWidth = _progressTrackWidth;
        if (trackWidth <= 0)
        {
            var parent = ProgressFill.Parent as View;
            trackWidth = parent?.Width ?? 0;
        }

        if (trackWidth <= 0) return;

        var ratio = Math.Clamp(sec / duration, 0, 1);
        ProgressFill.WidthRequest = trackWidth * ratio;
    }

    // ═══════════════════════════════════════════
    // TAB UX
    // ═══════════════════════════════════════════

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
        MenuTabBorder.Stroke = Color.FromArgb("#E8ECF0");
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
        OverviewTabBorder.Stroke = Color.FromArgb("#E8ECF0");
        OverviewTabBorder.StrokeThickness = 1;
        OverviewTab.TextColor = Color.FromArgb("#9B9BAA");
        OverviewTab.FontAttributes = FontAttributes.None;
    }

    // ═══════════════════════════════════════════
    // LIFECYCLE
    // ═══════════════════════════════════════════

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        StopSliderLoop();

        ProgressFillContainer.SizeChanged -= OnProgressContainerSizeChanged;

        audio.Stop();
        hasStarted = false;
    }

    // ═══════════════════════════════════════════
    // IMAGE VIEWER
    // ═══════════════════════════════════════════

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

    private async void CloseImageViewer(object sender, TappedEventArgs e)
    {
        await Task.WhenAll(
            ImageViewer.FadeTo(0, 150),
            PreviewImage.ScaleTo(0.8, 150)
        );

        ImageViewer.IsVisible = false;
    }

    // ═══════════════════════════════════════════
    // OPEN STATUS
    // ═══════════════════════════════════════════

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

    // ═══════════════════════════════════════════
    // UTILS
    // ═══════════════════════════════════════════

    private string Format(double sec)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, sec));
        return $"{(int)t.TotalMinutes:D2}:{t.Seconds:D2}";
    }
}
