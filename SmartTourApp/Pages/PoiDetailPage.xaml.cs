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
    private readonly OfflineSyncService offlineSync;
    private readonly LanguageService langService;

    private bool isSeeking = false;
    private double duration = 0;
    private bool hasStarted = false;
    private const double SkipSeconds = 5.0;

    private string _openedFrom = "";
    private CancellationTokenSource? _sliderCts;
    private double _progressTrackWidth = 0;
    private Location? _cachedUserLocation;

    // YC2 + YC4: Cache description và tên POI theo ngôn ngữ
    private string? _cachedDescriptionLang = null;
    private string? _cachedNameLang = null;

    // YC3: Debounce slider update để giảm re-render
    private double _lastSliderValue = -1;
    private const double SliderUpdateThreshold = 0.2; // chỉ update UI khi thay đổi > 0.2s

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
        ApiService api,
        OfflineSyncService offlineSync,
        LanguageService langService)
    {
        InitializeComponent();
        this.audio = audio;
        this.locationService = locationService;
        this.loc = loc;
        this.api = api;
        this.offlineSync = offlineSync;
        this.langService = langService;

        audio.OnProgress += sec =>
        {
            if (isSeeking) return;
            // YC3: Chỉ update UI khi thay đổi đủ lớn
            if (Math.Abs(sec - _lastSliderValue) < SliderUpdateThreshold &&
                sec > 0 && _lastSliderValue > 0) return;
            _lastSliderValue = sec;
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
                _lastSliderValue = -1;
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

        ApplyLocalization();

        if (!hasStarted)
        {
            _lastSliderValue = -1;
            UpdateSliderUI(0);
            TotalTimeLabel.Text = "00:00";
        }

        ProgressFillContainer.SizeChanged += OnProgressContainerSizeChanged;

        // YC2 + YC4: Load tên POI và mô tả theo ngôn ngữ hiện tại
        _ = LoadLocalizedPoiDataAsync();

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

        // Hiển thị tên ban đầu (DisplayName nếu đã có, fallback Name)
        PoiName.Text = string.IsNullOrWhiteSpace(poi.DisplayName) ? poi.Name : poi.DisplayName;
        PoiImage.Source = poi.ImageUrl;

        // Hiển thị description tạm trong khi load
        PoiDescription.Text = string.IsNullOrWhiteSpace(poi.Description)
            ? loc.NoDescription
            : poi.Description;

        UpdateOpenStatus();

        // Reset cache để force reload khi POI thay đổi
        _cachedDescriptionLang = null;
        _cachedNameLang = null;
    }

    // ══════════════════════════════════════════════════════════════
    // YC2 + YC4: LOAD LOCALIZED POI NAME + DESCRIPTION
    // Đồng bộ ngôn ngữ tên POI giống HomePage
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Tải tên POI và description theo ngôn ngữ hiện tại.
    /// Logic: Online → API → cache SQLite | Offline → SQLite → TtsScript/Description
    /// </summary>
    private async Task LoadLocalizedPoiDataAsync()
    {
        if (poi == null) return;

        var currentLang = loc.Current;

        // Skip nếu đã load ngôn ngữ này rồi
        if (_cachedDescriptionLang == currentLang && _cachedNameLang == currentLang) return;

        DescLoadingRow.IsVisible = true;

        try
        {
            List<ApiService.TtsDto>? scripts = null;

            // YC4: Online thì gọi API, offline thì dùng SQLite
            bool isOnline = IsOnline();

            if (isOnline)
            {
                try
                {
                    scripts = await api.GetTtsScripts(poi.Id);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[PoiDetail] API failed, falling back to SQLite: {ex.Message}");
                    scripts = null;
                }
            }

            // YC4: Fallback sang SQLite nếu offline hoặc API thất bại
            if (scripts == null || scripts.Count == 0)
            {
                var localScripts = offlineSync.GetAllLocalScripts(poi.Id);
                if (localScripts.Count > 0)
                {
                    scripts = localScripts.Select(s => new ApiService.TtsDto
                    {
                        LanguageCode = s.LanguageCode,
                        LanguageName = s.LanguageName,
                        Title = s.Title,
                        TtsScript = s.TtsScript,
                        AudioUrl = s.AudioUrl
                    }).ToList();

                    System.Diagnostics.Debug.WriteLine(
                        $"[PoiDetail] Using {scripts.Count} SQLite scripts for POI {poi.Id}");
                }
            }

            if (scripts == null || scripts.Count == 0)
            {
                // Không có gì → dùng dữ liệu gốc
                ApplyFallbackData();
                _cachedDescriptionLang = currentLang;
                _cachedNameLang = currentLang;
                return;
            }

            // YC2: Chọn script theo ngôn ngữ (giống logic HomePage)
            var selected = SelectByLanguage(scripts, currentLang);

            if (selected != null)
            {
                // YC2: Cập nhật tên POI theo ngôn ngữ
                if (!string.IsNullOrWhiteSpace(selected.Title))
                {
                    PoiName.Text = selected.Title;
                    // Cập nhật DisplayName để consistency
                    poi.DisplayName = selected.Title;
                }
                else
                {
                    PoiName.Text = string.IsNullOrWhiteSpace(poi.DisplayName)
                        ? poi.Name
                        : poi.DisplayName;
                }

                // Cập nhật description từ TtsScript
                if (!string.IsNullOrWhiteSpace(selected.TtsScript))
                    PoiDescription.Text = selected.TtsScript;
                else if (!string.IsNullOrWhiteSpace(selected.Title))
                    PoiDescription.Text = string.IsNullOrWhiteSpace(poi.Description)
                        ? selected.Title
                        : poi.Description;
                else
                    ApplyFallbackData();
            }
            else
            {
                ApplyFallbackData();
            }

            _cachedDescriptionLang = currentLang;
            _cachedNameLang = currentLang;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PoiDetail] LoadLocalized error: {ex.Message}");
            ApplyFallbackData();
        }
        finally
        {
            DescLoadingRow.IsVisible = false;
        }
    }

    /// <summary>
    /// YC2: Logic chọn ngôn ngữ giống HomePage - match → en → vi → first
    /// </summary>
    private static ApiService.TtsDto? SelectByLanguage(List<ApiService.TtsDto> scripts, string lang)
    {
        return scripts.FirstOrDefault(x =>
                x.LanguageCode.StartsWith(lang, StringComparison.OrdinalIgnoreCase))
            ?? scripts.FirstOrDefault(x =>
                x.LanguageCode.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            ?? scripts.FirstOrDefault(x =>
                x.LanguageCode.StartsWith("vi", StringComparison.OrdinalIgnoreCase))
            ?? scripts.FirstOrDefault();
    }

    private void ApplyFallbackData()
    {
        // Giữ tên hiện tại (DisplayName hoặc Name)
        if (string.IsNullOrWhiteSpace(PoiName.Text))
            PoiName.Text = string.IsNullOrWhiteSpace(poi.DisplayName) ? poi.Name : poi.DisplayName;

        // Fallback description
        if (string.IsNullOrWhiteSpace(PoiDescription.Text) ||
            PoiDescription.Text == loc.NoDescription)
        {
            PoiDescription.Text = string.IsNullOrWhiteSpace(poi.Description)
                ? loc.NoDescription
                : poi.Description;
        }
    }

    // Giữ nguyên tên cũ để backward compat
    private async Task LoadDescriptionFromTtsAsync() => await LoadLocalizedPoiDataAsync();

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
            _lastSliderValue = -1;
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
        _lastSliderValue = -1;
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
    // SLIDER LOOP — YC3: Tối ưu interval từ liên tục → 150ms
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
                    // YC3: Chỉ update nếu thay đổi đáng kể
                    if (Math.Abs(currentSec - _lastSliderValue) >= SliderUpdateThreshold || _lastSliderValue < 0)
                    {
                        _lastSliderValue = currentSec;
                        MainThread.BeginInvokeOnMainThread(() => SyncProgressFill(currentSec));
                    }
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
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ProgressSlider.Value = target;
            _lastSliderValue = target;
            UpdateSliderUI(target);
        });
    }

    private void OnSkipForward(object sender, EventArgs e)
    {
        var target = ProgressSlider.Value + SkipSeconds;
        if (duration > 0 && target > duration)
        {
            target = 0;
            audio.Stop();
            hasStarted = false;
            _lastSliderValue = -1;
            SetPlayingState(false);
            StopSliderLoop();
        }
        audio.Seek(target);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ProgressSlider.Value = target;
            _lastSliderValue = target;
            UpdateSliderUI(target);
        });
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

        // YC2 + YC4: reload dữ liệu ngôn ngữ khi quay về overview
        _ = LoadLocalizedPoiDataAsync();
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
        _lastSliderValue = -1;
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
    // HELPERS
    // ══════════════════════════════════════════════════════════════

    private static bool IsOnline()
    {
        try
        {
            var access = Connectivity.Current.NetworkAccess;
            return access == NetworkAccess.Internet ||
                   access == NetworkAccess.ConstrainedInternet;
        }
        catch { return false; }
    }

    private string Format(double sec)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, sec));
        return $"{(int)t.TotalMinutes:D2}:{t.Seconds:D2}";
    }
}
