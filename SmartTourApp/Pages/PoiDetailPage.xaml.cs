using SmartTour.Services;
using SmartTour.Shared.Models;
using SmartTourApp.Services;

namespace SmartTourApp.Pages;

[QueryProperty(nameof(Poi), "poi")]
public partial class PoiDetailPage : ContentPage
{
    private Poi poi;
    private readonly PoiDetailAudioManager audio;
    private readonly LocationService locationService;
    private readonly LocalizationService loc;
    private readonly ApiService api;

    private bool isSeeking = false;
    private double duration = 0;
    private bool hasStarted = false;
    private const double SkipSeconds = 5.0;

    private string _openedFrom = "";
    private CancellationTokenSource? _sliderCts;
    private double _progressTrackWidth = 0;
    private Location? _cachedUserLocation;

    // YC3: cache description theo ngôn ngữ
    private string? _cachedDescriptionLang = null;
    private string? _cachedFoodLang = null;

    public Poi Poi
    {
        get => poi;
        set
        {
            poi = value;
            BindData();
        }
    }

    public PoiDetailPage(
        PoiDetailAudioManager audio,
        LocationService locationService,
        LocalizationService loc,
        ApiService api)
    {
        InitializeComponent();
        this.audio = audio;
        this.locationService = locationService;
        this.loc = loc;
        this.api = api;

        audio.OnProgress += sec =>
        {
            if (isSeeking) return;
            MainThread.BeginInvokeOnMainThread(() => UpdateSliderUI(sec));
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

    public void SetOpenedFrom(string source) => _openedFrom = source;

    // ══════════════════════════════════════════════════════════════
    // LIFECYCLE
    // ══════════════════════════════════════════════════════════════

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Apply localization
        ApplyLocalization();

        if (!hasStarted)
        {
            UpdateSliderUI(0);
            TotalTimeLabel.Text = "00:00";
        }

        ProgressFillContainer.SizeChanged += OnProgressContainerSizeChanged;

        // YC3: Load description từ TtsScript
        _ = LoadDescriptionFromTtsAsync();
        _ = LoadFoodMenuAsync();

        // Prefetch GPS
        _ = Task.Run(async () => { _cachedUserLocation = await locationService.GetLocation(); });
    }

    private void ApplyLocalization()
    {
        LblIntroduction.Text = loc.Introduction;
        LblAudioGuide.Text = loc.AudioGuide;
        LblNarrationPoint.Text = loc.NarrationPoint;
        LblTapToClose.Text = loc.TapToClose;
        StopBtn.Text = loc.StopPlaying;
        OverviewTab.Text = loc.Overview;
        MenuTab.Text = loc.Menu;
    }

    private void BindData()
    {
        if (poi == null) return;

        if (poi.Foods != null && poi.Foods.Any())
            FoodList.ItemsSource = poi.Foods;
        else
            FoodList.ItemsSource = new List<Food>();

        PoiName.Text = poi.Name;
        PoiImage.Source = poi.ImageUrl;

        // Hiển thị description tạm (từ poi.Description) trong khi load TtsScript
        PoiDescription.Text = string.IsNullOrWhiteSpace(poi.Description)
            ? loc.NoDescription
            : poi.Description;

        UpdateOpenStatus();

        // Reset cache để force reload khi POI thay đổi
        _cachedDescriptionLang = null;
        _cachedFoodLang = null;
    }

    private async Task LoadFoodMenuAsync()
    {
        if (poi == null) return;

        var currentLang = loc.Current;
        if (_cachedFoodLang == currentLang && poi.Foods != null && poi.Foods.Any())
            return;

        try
        {
            var foods = await api.GetFoodsByPoi(poi.Id);
            poi.Foods = foods ?? new List<Food>();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                FoodList.ItemsSource = null;
                FoodList.ItemsSource = poi.Foods;
            });
            _cachedFoodLang = currentLang;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PoiDetail] LoadFoodMenu error: {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════════════════════
    // YC3: LOAD DESCRIPTION FROM TTS SCRIPT
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Tải description từ TtsScript của POI theo ngôn ngữ hiện tại.
    /// Thay thế poi.Description bằng nội dung đa ngữ từ API/SQLite.
    /// </summary>
    private async Task LoadDescriptionFromTtsAsync()
    {
        if (poi == null) return;

        var currentLang = loc.Current;

        // Không reload nếu đang dùng cùng ngôn ngữ
        if (_cachedDescriptionLang == currentLang) return;

        // Show loading
        DescLoadingRow.IsVisible = true;

        try
        {
            var scripts = await api.GetTtsScripts(poi.Id);

            if (scripts == null || scripts.Count == 0)
            {
                // Fallback: dùng poi.Description
                PoiDescription.Text = string.IsNullOrWhiteSpace(poi.Description)
                    ? loc.NoDescription
                    : poi.Description;
                _cachedDescriptionLang = currentLang;
                return;
            }

            // Tìm script theo ngôn ngữ hiện tại
            var selected = scripts.FirstOrDefault(x =>
                x.LanguageCode.StartsWith(currentLang, StringComparison.OrdinalIgnoreCase))
                ?? scripts.FirstOrDefault(x =>
                    x.LanguageCode.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                ?? scripts.FirstOrDefault();

            if (selected != null && !string.IsNullOrWhiteSpace(selected.TtsScript))
            {
                PoiDescription.Text = selected.TtsScript;
            }
            else if (selected != null && !string.IsNullOrWhiteSpace(selected.Title))
            {
                // Fallback: dùng Title + poi.Description
                PoiDescription.Text = string.IsNullOrWhiteSpace(poi.Description)
                    ? selected.Title
                    : poi.Description;
            }
            else
            {
                PoiDescription.Text = string.IsNullOrWhiteSpace(poi.Description)
                    ? loc.NoDescription
                    : poi.Description;
            }

            _cachedDescriptionLang = currentLang;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PoiDetail] LoadDescription error: {ex.Message}");
            // Fallback: dùng description gốc
            if (string.IsNullOrWhiteSpace(PoiDescription.Text) ||
                PoiDescription.Text == loc.NoDescription)
            {
                PoiDescription.Text = string.IsNullOrWhiteSpace(poi.Description)
                    ? loc.NoDescription
                    : poi.Description;
            }
        }
        finally
        {
            DescLoadingRow.IsVisible = false;
        }
    }

    private void OnProgressContainerSizeChanged(object? sender, EventArgs e)
    {
        var parent = ProgressFill.Parent as View;
        if (parent != null && parent.Width > 0)
            _progressTrackWidth = parent.Width;
    }

    // ══════════════════════════════════════════════════════════════
    // BACK
    // ══════════════════════════════════════════════════════════════

    private async void OnBackTapped(object sender, TappedEventArgs e)
    {
        audio.Stop();
        hasStarted = false;

        if (_openedFrom == "map")
            await Shell.Current.GoToAsync("//map");
        else
        {
            if (Navigation.NavigationStack.Count > 1)
                await Navigation.PopAsync(animated: true);
            else
                await Shell.Current.GoToAsync("//home");
        }
    }

    // ══════════════════════════════════════════════════════════════
    // AUDIO CONTROLS
    // ══════════════════════════════════════════════════════════════

    private async void OnPlayClicked(object sender, EventArgs e)
    {
        if (poi == null) return;

        if (!hasStarted)
        {
            var loc2 = await GetFreshLocationAsync();
            await audio.Play(poi, loc2);
            hasStarted = true;
            SetPlayingState(true);
            StartSliderLoop();
        }
        else if (audio.IsPlaying)
        {
            audio.Pause();
            SetPlayingState(false);
            StopSliderLoop();
        }
        else
        {
            audio.Resume();
            SetPlayingState(true);
            StartSliderLoop();
        }
    }

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
        MainThread.BeginInvokeOnMainThread(() => UpdateSliderUI(0));
    }

    private async Task<Location?> GetFreshLocationAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var fresh = await locationService.GetLocation();
            if (fresh != null) { _cachedUserLocation = fresh; return fresh; }
        }
        catch { }
        return _cachedUserLocation;
    }

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
            StopBtn.Text = loc.StopPlaying;
        });
    }

    // ══════════════════════════════════════════════════════════════
    // SLIDER LOOP
    // ══════════════════════════════════════════════════════════════

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
                    MainThread.BeginInvokeOnMainThread(() => SyncProgressFill(currentSec));
                }
                try { await Task.Delay(150, token); }
                catch (OperationCanceledException) { break; }
            }
        }, token);
    }

    private void StopSliderLoop()
    {
        _sliderCts?.Cancel();
        _sliderCts?.Dispose();
        _sliderCts = null;
    }

    // ══════════════════════════════════════════════════════════════
    // SEEK
    // ══════════════════════════════════════════════════════════════

    private void OnSeekStarted(object sender, EventArgs e) => isSeeking = true;

    private void OnSeekCompleted(object sender, EventArgs e)
    {
        isSeeking = false;
        audio.Seek(ProgressSlider.Value);
        if (audio.IsPlaying) StartSliderLoop();
    }

    private void OnSeek(object sender, ValueChangedEventArgs e)
    {
        if (isSeeking)
            MainThread.BeginInvokeOnMainThread(() =>
            {
                CurrentTimeLabel.Text = Format(e.NewValue);
                SyncProgressFill(e.NewValue);
            });
    }

    private void OnSkipBack(object sender, EventArgs e)
    {
        var target = Math.Max(0, ProgressSlider.Value - SkipSeconds);
        audio.Seek(target);
        MainThread.BeginInvokeOnMainThread(() => { ProgressSlider.Value = target; UpdateSliderUI(target); });
    }

    private void OnSkipForward(object sender, EventArgs e)
    {
        var target = ProgressSlider.Value + SkipSeconds;
        if (duration > 0 && target > duration)
        {
            target = 0;
            audio.Stop();
            hasStarted = false;
            SetPlayingState(false);
            StopSliderLoop();
        }
        audio.Seek(target);
        MainThread.BeginInvokeOnMainThread(() => { ProgressSlider.Value = target; UpdateSliderUI(target); });
    }

    // ══════════════════════════════════════════════════════════════
    // UI HELPERS
    // ══════════════════════════════════════════════════════════════

    private void UpdateSliderUI(double sec)
    {
        if (ProgressSlider.Maximum > 0) ProgressSlider.Value = sec;
        CurrentTimeLabel.Text = Format(sec);
        var remaining = duration > 0 ? Math.Max(0, duration - sec) : 0;
        RemainingTimeLabel.Text = Format(remaining);
        SyncProgressFill(sec);
    }

    private void SyncProgressFill(double sec)
    {
        if (duration <= 0) return;
        double trackWidth = _progressTrackWidth;
        if (trackWidth <= 0) { var p = ProgressFill.Parent as View; trackWidth = p?.Width ?? 0; }
        if (trackWidth <= 0) return;
        ProgressFill.WidthRequest = trackWidth * Math.Clamp(sec / duration, 0, 1);
    }

    // ══════════════════════════════════════════════════════════════
    // TABS
    // ══════════════════════════════════════════════════════════════

    private void ShowOverview(object sender, EventArgs e)
    {
        OverviewSection.IsVisible = true;
        FoodList.IsVisible = false;
        AudioBar.IsVisible = true;

        OverviewTabBorder.Background = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(Color.FromArgb("#3461FF"), 0f),
                new GradientStop(Color.FromArgb("#7C9FFF"), 1f)
            }, new Point(0, 0), new Point(1, 1));
        OverviewTabBorder.StrokeThickness = 0;
        OverviewTab.TextColor = Colors.White;
        OverviewTab.FontAttributes = FontAttributes.Bold;
        OverviewTab.Text = loc.Overview;

        MenuTabBorder.Background = new SolidColorBrush(Color.FromArgb("#F5F6FA"));
        MenuTabBorder.Stroke = Color.FromArgb("#E8ECF0");
        MenuTabBorder.StrokeThickness = 1;
        MenuTab.TextColor = Color.FromArgb("#9B9BAA");
        MenuTab.FontAttributes = FontAttributes.None;
        MenuTab.Text = loc.Menu;

        // YC3: reload description khi quay về overview
        _ = LoadDescriptionFromTtsAsync();
    }

    private void ShowMenu(object sender, EventArgs e)
    {
        OverviewSection.IsVisible = false;
        FoodList.IsVisible = true;
        AudioBar.IsVisible = false;

        MenuTabBorder.Background = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(Color.FromArgb("#3461FF"), 0f),
                new GradientStop(Color.FromArgb("#7C9FFF"), 1f)
            }, new Point(0, 0), new Point(1, 1));
        MenuTabBorder.StrokeThickness = 0;
        MenuTab.TextColor = Colors.White;
        MenuTab.FontAttributes = FontAttributes.Bold;
        MenuTab.Text = loc.Menu;

        OverviewTabBorder.Background = new SolidColorBrush(Color.FromArgb("#F5F6FA"));
        OverviewTabBorder.Stroke = Color.FromArgb("#E8ECF0");
        OverviewTabBorder.StrokeThickness = 1;
        OverviewTab.TextColor = Color.FromArgb("#9B9BAA");
        OverviewTab.FontAttributes = FontAttributes.None;
        OverviewTab.Text = loc.Overview;

        _ = LoadFoodMenuAsync();
    }

    // ══════════════════════════════════════════════════════════════
    // LIFECYCLE
    // ══════════════════════════════════════════════════════════════

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        StopSliderLoop();
        ProgressFillContainer.SizeChanged -= OnProgressContainerSizeChanged;
        audio.Stop();
        hasStarted = false;
    }

    // ══════════════════════════════════════════════════════════════
    // IMAGE VIEWER
    // ══════════════════════════════════════════════════════════════

    private async void OnFoodImageTapped(object sender, TappedEventArgs e)
    {
        if (sender is Microsoft.Maui.Controls.Image img && img.BindingContext is Food food)
        {
            PreviewImage.Source = food.ImageUrl;
            ImageViewer.IsVisible = true;
            await Task.WhenAll(
                ImageViewer.FadeTo(1, 150),
                PreviewImage.ScaleTo(1, 200));
        }
    }

    private async void CloseImageViewer(object sender, TappedEventArgs e)
    {
        await Task.WhenAll(
            ImageViewer.FadeTo(0, 150),
            PreviewImage.ScaleTo(0.8, 150));
        ImageViewer.IsVisible = false;
    }

    // ══════════════════════════════════════════════════════════════
    // OPEN STATUS
    // ══════════════════════════════════════════════════════════════

    private void UpdateOpenStatus()
    {
        if (poi == null || poi.OpenTime == null || poi.CloseTime == null) return;

        var now = DateTime.Now.TimeOfDay;
        var open = poi.OpenTime.Value;
        var close = poi.CloseTime.Value;

        if (open == close)
        {
            OpenStatus.Text = loc.OpenAllDay;
            OpenStatus.TextColor = Color.FromArgb("#00AAFF");
            OpenStatusBadge.BackgroundColor = Color.FromArgb("#1A00AAFF");
            OpenStatusBadge.Stroke = new SolidColorBrush(Color.FromArgb("#4400AAFF"));
            return;
        }

        if (now >= open && now <= close)
        {
            OpenStatus.Text = loc.IsOpen;
            OpenStatus.TextColor = Color.FromArgb("#00FF88");
            OpenStatusBadge.BackgroundColor = Color.FromArgb("#1A00FF88");
            OpenStatusBadge.Stroke = new SolidColorBrush(Color.FromArgb("#4400FF88"));
            OpenDetail.Text = string.Format(loc.ClosesAt, close.ToString(@"hh\:mm"));
        }
        else
        {
            OpenStatus.Text = loc.IsClosed;
            OpenStatus.TextColor = Color.FromArgb("#FF6B6B");
            OpenStatusBadge.BackgroundColor = Color.FromArgb("#1AFF6B6B");
            OpenStatusBadge.Stroke = new SolidColorBrush(Color.FromArgb("#44FF6B6B"));
            OpenDetail.Text = string.Format(loc.OpensAt, open.ToString(@"hh\:mm"));
        }
    }

    // ══════════════════════════════════════════════════════════════
    // UTILS
    // ══════════════════════════════════════════════════════════════

    private string Format(double sec)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, sec));
        return $"{(int)t.TotalMinutes:D2}:{t.Seconds:D2}";
    }
}
