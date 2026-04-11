using SmartTour.Shared.Models;
using SmartTourApp.Services;

namespace SmartTourApp.Pages;

/// <summary>
/// PoiDetailPage — Audio player với state machine đầy đủ.
///
/// ╔══════════════════════════════════════════════════════════════════╗
/// ║  STATE MACHINE                                                   ║
/// ║  Idle → Buffering → Playing → Paused → Ended → Replay → ...    ║
/// ║                                                                  ║
/// ║  SLIDER ROOT CAUSE FIX                                          ║
/// ║  • Maximum chỉ set khi OnDuration fire (duration thực tế)       ║
/// ║  • Value đồng bộ OnProgress qua UpdateSliderUI trên MainThread  ║
/// ║  • DragStarted  → PauseSession (chốt log A)                    ║
/// ║  • DragCompleted→ Seek + Resume + StartSession (log B)          ║
/// ║  • Skip: Math.Clamp(target, 0, Maximum)                        ║
/// ║                                                                  ║
/// ║  TIME LABELS                                                     ║
/// ║  • CurrentTimeLabel = Slider.Value (tăng dần theo giây phát)   ║
/// ║  • RemainingTimeLabel = Maximum = tổng thời lượng (cố định)    ║
/// ║                                                                  ║
/// ║  BUFFERING                                                       ║
/// ║  • Spinner xoay, slider disabled cho đến khi OnDuration fire   ║
/// ║                                                                  ║
/// ║  INTERRUPTION / AUDIO FOCUS                                     ║
/// ║  • OnSleep → auto Pause + chốt log                             ║
/// ║  • OnResume → giữ Paused, không tự play                        ║
/// ╚══════════════════════════════════════════════════════════════════╝
/// </summary>
[QueryProperty(nameof(Poi), "poi")]
public partial class PoiDetailPage : ContentPage
{
    // ══════════════════════════════════════════════════════════════════
    // DEPENDENCIES
    // ══════════════════════════════════════════════════════════════════

    private readonly PoiDetailAudioManager audio;
    private readonly LocationService locationService;

    // ══════════════════════════════════════════════════════════════════
    // STATE MACHINE
    // ══════════════════════════════════════════════════════════════════

    private enum PlaybackState { Idle, Buffering, Playing, Paused, Ended }
    private PlaybackState _state = PlaybackState.Idle;

    // ══════════════════════════════════════════════════════════════════
    // FIELDS
    // ══════════════════════════════════════════════════════════════════

    private Poi poi;
    private bool isSeeking = false;

    /// <summary>
    /// Tổng thời lượng audio (giây).
    /// Chỉ được gán khi nhận OnDuration — KHÔNG set mặc định.
    /// </summary>
    private double duration = 0;

    private Location? _cachedUserLocation;
    private string _openedFrom = "";

    // Slider sync loop
    private CancellationTokenSource? _sliderCts;

    // Buffering spinner
    private CancellationTokenSource? _bufferSpinCts;
    private int _spinFrame = 0;
    private readonly string[] _spinFrames = { "◐", "◓", "◑", "◒" };

    private const double SkipSeconds = 10.0;

    // ── Compat fields (không xóa) ──
    private bool hasStarted => _state != PlaybackState.Idle;
    private double _progressTrackWidth = 0;
    private double _seekStartPosition = 0;
    private DateTime? _playStartTime;
    private double _playStartSec;
    private bool _wasPlayingBeforeSleep = false;

    // ══════════════════════════════════════════════════════════════════
    // QUERY PROPERTY
    // ══════════════════════════════════════════════════════════════════

    public Poi Poi
    {
        get => poi;
        set
        {
            poi = value;
            BindData();
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // CONSTRUCTOR
    // ══════════════════════════════════════════════════════════════════

    public PoiDetailPage(PoiDetailAudioManager audio, LocationService locationService)
    {
        InitializeComponent();
        this.audio = audio;
        this.locationService = locationService;

        WireAudioCallbacks();
    }

    // ══════════════════════════════════════════════════════════════════
    // AUDIO CALLBACKS
    // ══════════════════════════════════════════════════════════════════

    private void WireAudioCallbacks()
    {
        // ── OnProgress: cập nhật slider + CurrentTimeLabel mỗi frame ──
        audio.OnProgress += sec =>
        {
            if (isSeeking) return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_state != PlaybackState.Playing) return;

                // ✅ Chỉ update khi Maximum đã được set thực tế (> 1)
                if (ProgressSlider.Maximum > 1)
                {
                    ProgressSlider.Value = Math.Clamp(sec, 0, ProgressSlider.Maximum);
                }

                // ✅ CurrentTimeLabel = giây đang phát
                CurrentTimeLabel.Text = Format(ProgressSlider.Value);
            });
        };

        // ── OnDuration: KEY EVENT — set Maximum thực tế, enable slider ──
        // Đây là nơi DUY NHẤT set Slider.Maximum = duration thực tế.
        // Trước khi nhận event này, Maximum = placeholder 1 (không có ý nghĩa).
        audio.OnDuration += d =>
        {
            duration = d;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (d <= 0) return;

                // ✅ Root cause fix: Maximum = duration thực tế
                ProgressSlider.Maximum = d;
                ProgressSlider.Value = 0;
                ProgressSlider.IsEnabled = true;

                // CurrentTimeLabel = 00:00, RemainingTimeLabel = tổng thời lượng (cố định)
                CurrentTimeLabel.Text = "00:00";
                RemainingTimeLabel.Text = Format(d);
                TotalTimeLabel.Text = Format(d);

                // Fade out opacity → restore
                RestoreOpacity();

                // Thoát Buffering → Playing
                if (_state == PlaybackState.Buffering)
                {
                    StopBufferingSpinner();
                    BufferingBadge.IsVisible = false;
                    PlayerStatusMicroLabel.Text = "ĐANG PHÁT";
                    _state = PlaybackState.Playing;
                    ApplyPlayingUI();
                }
            });
        };

        // ── OnCompleted: file hết tự nhiên (không phải user dừng) ──
        audio.OnCompleted += () =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                TransitionTo(PlaybackState.Ended);
            });
        };
    }

    // ══════════════════════════════════════════════════════════════════
    // STATE MACHINE TRANSITIONS
    // ══════════════════════════════════════════════════════════════════

    private void TransitionTo(PlaybackState newState)
    {
        _state = newState;

        switch (newState)
        {
            case PlaybackState.Idle:
                SetPlayIcon(PlayIcon.Play);
                StopSliderLoop();
                StopBufferingSpinner();
                ResetSliderUI();
                StopBtn.IsVisible = false;
                BufferingBadge.IsVisible = false;
                PlayerStatusMicroLabel.Text = "NHẤN ▶ ĐỂ PHÁT";
                RestoreOpacity();
                break;

            case PlaybackState.Buffering:
                PlayBtn.IsEnabled = false;
                ProgressSlider.IsEnabled = false;
                StopBtn.IsVisible = true;
                BufferingBadge.IsVisible = true;
                PlayerStatusMicroLabel.Text = "ĐANG TẢI...";
                FadeOutSlider();
                StartBufferingSpinner();
                _ = AnimateBufferingPulse();
                break;

            case PlaybackState.Playing:
                StopBufferingSpinner();
                BufferingBadge.IsVisible = false;
                PlayerStatusMicroLabel.Text = "ĐANG PHÁT";
                RestoreOpacity();
                ApplyPlayingUI();
                break;

            case PlaybackState.Paused:
                SetPlayIcon(PlayIcon.Play);
                StopSliderLoop();
                StopBufferingSpinner();
                BufferingBadge.IsVisible = false;
                PlayerStatusMicroLabel.Text = "TẠM DỪNG";
                ProgressSlider.IsEnabled = true;
                PlayBtn.IsEnabled = true;
                StopBtn.IsVisible = true;
                RestoreOpacity();
                break;

            case PlaybackState.Ended:
                SetPlayIcon(PlayIcon.Replay);
                StopSliderLoop();
                StopBufferingSpinner();
                BufferingBadge.IsVisible = false;
                PlayerStatusMicroLabel.Text = "ĐÃ PHÁT XONG";
                ProgressSlider.IsEnabled = true;
                PlayBtn.IsEnabled = true;
                StopBtn.IsVisible = false;
                RestoreOpacity();

                // Slider về cuối — CurrentTime = RemainingTime = tổng thời lượng
                if (duration > 0 && ProgressSlider.Maximum > 1)
                {
                    ProgressSlider.Value = ProgressSlider.Maximum;
                    CurrentTimeLabel.Text = Format(duration);
                    RemainingTimeLabel.Text = Format(duration);
                }
                break;
        }
    }

    private void ApplyPlayingUI()
    {
        SetPlayIcon(PlayIcon.Pause);
        StartSliderLoop();
        ProgressSlider.IsEnabled = true;
        PlayBtn.IsEnabled = true;
        StopBtn.IsVisible = true;
    }

    // ══════════════════════════════════════════════════════════════════
    // BUFFERING SPINNER & VISUAL
    // ══════════════════════════════════════════════════════════════════

    private void StartBufferingSpinner()
    {
        StopBufferingSpinner();
        _bufferSpinCts = new CancellationTokenSource();
        var token = _bufferSpinCts.Token;
        _spinFrame = 0;

        Dispatcher.StartTimer(TimeSpan.FromMilliseconds(120), () =>
        {
            if (token.IsCancellationRequested) return false;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (token.IsCancellationRequested) return;
                PlayBtn.Text = _spinFrames[_spinFrame % _spinFrames.Length];
                PlayBtn.FontSize = 20;
                _spinFrame++;
            });
            return !token.IsCancellationRequested;
        });
    }

    private void StopBufferingSpinner()
    {
        _bufferSpinCts?.Cancel();
        _bufferSpinCts?.Dispose();
        _bufferSpinCts = null;
    }

    private void FadeOutSlider()
    {
        ProgressSlider.Opacity = 0.35;
        CurrentTimeLabel.Opacity = 0.35;
        RemainingTimeLabel.Opacity = 0.35;
        TotalTimeLabel.Opacity = 0.35;
    }

    private void RestoreOpacity()
    {
        ProgressSlider.Opacity = 1.0;
        CurrentTimeLabel.Opacity = 1.0;
        RemainingTimeLabel.Opacity = 1.0;
        TotalTimeLabel.Opacity = 1.0;
        PlayBtnContainer.Opacity = 1.0;
    }

    private async Task AnimateBufferingPulse()
    {
        while (_state == PlaybackState.Buffering)
        {
            await PlayBtnContainer.FadeTo(0.55, 380, Easing.SinInOut);
            if (_state != PlaybackState.Buffering) break;
            await PlayBtnContainer.FadeTo(1.0, 380, Easing.SinInOut);
        }
        PlayBtnContainer.Opacity = 1.0;
    }

    // ══════════════════════════════════════════════════════════════════
    // PLAY ICON
    // ══════════════════════════════════════════════════════════════════

    private enum PlayIcon { Play, Pause, Replay }

    private void SetPlayIcon(PlayIcon icon)
    {
        switch (icon)
        {
            case PlayIcon.Play:
                PlayBtn.Text = "▶";
                PlayBtn.FontSize = 22;
                break;
            case PlayIcon.Pause:
                PlayBtn.Text = "⏸";
                PlayBtn.FontSize = 20;
                break;
            case PlayIcon.Replay:
                PlayBtn.Text = "↺";
                PlayBtn.FontSize = 22;
                break;
        }

        PlayBtn.IsEnabled = true;
        PlayBtnContainer.IsVisible = true;
        PauseBtnContainer.IsVisible = false;
        PauseBtn.IsEnabled = _state == PlaybackState.Playing;
    }

    // ══════════════════════════════════════════════════════════════════
    // MAIN PLAY BUTTON
    // ══════════════════════════════════════════════════════════════════

    private async void OnPlayClicked(object sender, EventArgs e)
    {
        if (poi == null) return;
        if (_state == PlaybackState.Buffering) return; // chỉ Stop mới được

        switch (_state)
        {
            case PlaybackState.Idle:
                await StartFreshPlayback();
                break;
            case PlaybackState.Playing:
                DoPause();
                break;
            case PlaybackState.Paused:
                DoResume();
                break;
            case PlaybackState.Ended:
                await DoReplay();
                break;
        }
    }

    // ── Idle → Buffering → (OnDuration) → Playing ──
    private async Task StartFreshPlayback()
    {
        // Reset về placeholder trước — Maximum sẽ được set bởi OnDuration
        ResetSliderUI();
        TransitionTo(PlaybackState.Buffering);

        if (PlayerPoiNameLabel != null && poi != null)
            PlayerPoiNameLabel.Text = poi.Name ?? "Thuyết minh";

        var loc = await GetFreshLocationAsync();

        // User có thể đã bấm Stop trong lúc buffering
        if (_state == PlaybackState.Idle) return;

        await audio.Play(poi, loc);

        // TTS fallback: OnDuration không fire → transition thủ công
        if (_state == PlaybackState.Buffering)
        {
            await Task.Delay(600);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_state == PlaybackState.Buffering)
                    TransitionTo(PlaybackState.Playing);
            });
        }
    }

    // ── Playing → Paused ──
    private void DoPause()
    {
        audio.Pause();  // chốt log session A bên trong AudioListenTracker
        TransitionTo(PlaybackState.Paused);
    }

    // ── Paused → Playing ──
    private void DoResume()
    {
        audio.Resume(); // mở log session mới từ giây hiện tại
        TransitionTo(PlaybackState.Playing);
    }

    // ── Ended → Buffering → Playing (Replay từ đầu) ──
    private async Task DoReplay()
    {
        // Reset slider về 0, giữ Maximum (duration không đổi)
        ProgressSlider.Value = 0;
        CurrentTimeLabel.Text = "00:00";
        // RemainingTimeLabel giữ nguyên = total duration

        duration = 0;
        ProgressSlider.IsEnabled = false;
        TransitionTo(PlaybackState.Buffering);

        if (PlayerPoiNameLabel != null && poi != null)
            PlayerPoiNameLabel.Text = poi.Name ?? "Thuyết minh";

        var loc = await GetFreshLocationAsync();
        if (_state == PlaybackState.Idle) return;

        await audio.Play(poi, loc);

        if (_state == PlaybackState.Buffering)
        {
            await Task.Delay(600);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_state == PlaybackState.Buffering)
                    TransitionTo(PlaybackState.Playing);
            });
        }
    }

    // ── Stop ──
    private void OnStopTap(object sender, TappedEventArgs e) => DoStop();
    private void OnStopClicked(object sender, EventArgs e) => DoStop(); // compat

    private void DoStop()
    {
        audio.Stop();
        duration = 0;
        TransitionTo(PlaybackState.Idle);
    }

    private void OnPauseClicked(object sender, EventArgs e) // compat
    {
        if (_state == PlaybackState.Playing) DoPause();
    }

    // ══════════════════════════════════════════════════════════════════
    // SLIDER SYNC LOOP (dự phòng khi ExoPlayer OnProgress chậm)
    // ══════════════════════════════════════════════════════════════════

    private void StartSliderLoop()
    {
        StopSliderLoop();
        _playStartTime = DateTime.Now;
        _playStartSec = ProgressSlider.Value;
        _sliderCts = new CancellationTokenSource();
        var token = _sliderCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try { await Task.Delay(150, token); }
                catch (OperationCanceledException) { break; }
                // ExoPlayer OnProgress đã handle — loop này giữ CTS để cancel khi cần
            }
        }, token);
    }

    private void StopSliderLoop()
    {
        _sliderCts?.Cancel();
        _sliderCts?.Dispose();
        _sliderCts = null;
        _playStartTime = null;
    }

    // ══════════════════════════════════════════════════════════════════
    // SEEK — Log Session A/B precision
    // ══════════════════════════════════════════════════════════════════

    private void OnSeekStarted(object sender, EventArgs e)
    {
        isSeeking = true;
        _seekStartPosition = ProgressSlider.Value;

        // ── Chốt Log Session A ngay khi chạm Thumb ──
        if (_state == PlaybackState.Playing)
        {
            audio.Pause();      // PauseSession() bên trong tracker
            StopSliderLoop();
            // Giữ _state = Playing để OnSeekCompleted biết phải Resume
        }
    }

    private void OnSeekCompleted(object sender, EventArgs e)
    {
        isSeeking = false;
        var targetSec = ProgressSlider.Value;

        audio.Seek(targetSec);

        // ✅ Đồng bộ labels ngay sau seek
        MainThread.BeginInvokeOnMainThread(() => UpdateSliderUI(targetSec));

        // ── Khởi tạo Log Session B ngay khi buông tay ──
        if (_state == PlaybackState.Playing || _state == PlaybackState.Paused)
        {
            audio.Resume();     // StartSession() bên trong tracker từ targetSec
            TransitionTo(PlaybackState.Playing);
        }
    }

    private void OnSeek(object sender, ValueChangedEventArgs e)
    {
        // Chỉ update label khi user đang kéo (không phải code set)
        if (isSeeking)
        {
            MainThread.BeginInvokeOnMainThread(() => UpdateSliderUI(e.NewValue));
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // SKIP BUTTONS — clamp chặt
    // ══════════════════════════════════════════════════════════════════

    private void OnSkipBackTap(object sender, TappedEventArgs e) => PerformSkipBack();
    private void OnSkipFwdTap(object sender, TappedEventArgs e) => PerformSkipForward();
    private void OnSkipBack(object sender, EventArgs e) => PerformSkipBack();       // compat
    private void OnSkipForward(object sender, EventArgs e) => PerformSkipForward(); // compat

    private void PerformSkipBack()
    {
        if (_state == PlaybackState.Idle || _state == PlaybackState.Buffering) return;

        // Clamp về 0 nếu âm (VD: 00:04 - 10s = 00:00, không phải âm)
        var target = Math.Max(0.0, ProgressSlider.Value - SkipSeconds);
        PerformSeekTo(target);
    }

    private void PerformSkipForward()
    {
        if (_state == PlaybackState.Idle || _state == PlaybackState.Buffering) return;

        var target = ProgressSlider.Value + SkipSeconds;

        // Nếu vượt duration → kết thúc file luôn
        if (duration > 0 && target >= duration)
        {
            audio.Stop();
            TransitionTo(PlaybackState.Ended);
            return;
        }

        // Clamp về Maximum (an toàn)
        var max = ProgressSlider.Maximum > 1 ? ProgressSlider.Maximum : target;
        PerformSeekTo(Math.Min(target, max));
    }

    /// <summary>
    /// Programmatic seek — chốt log A, seek engine, cập nhật UI ngay, mở log B.
    /// CurrentTimeLabel phản ánh đúng vị trí mới. RemainingTimeLabel không đổi.
    /// </summary>
    private void PerformSeekTo(double sec)
    {
        bool wasPlaying = _state == PlaybackState.Playing;

        if (wasPlaying)
        {
            audio.Pause();  // chốt log session A
            StopSliderLoop();
        }

        // Clamp chặt vào [0, Maximum]
        double max = ProgressSlider.Maximum > 1
            ? ProgressSlider.Maximum
            : (duration > 0 ? duration : Math.Max(sec, 1));
        double clamped = Math.Clamp(sec, 0, max);

        audio.Seek(clamped);

        // ✅ Update slider + labels trên MainThread ngay lập tức
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (ProgressSlider.Maximum > 1)
                ProgressSlider.Value = clamped;
            UpdateSliderUI(clamped);
        });

        if (wasPlaying)
        {
            audio.Resume(); // mở log session B từ clamped
            TransitionTo(PlaybackState.Playing);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // APP LIFECYCLE — Interruption
    // ══════════════════════════════════════════════════════════════════

    public void HandleAppSleep()
    {
        _wasPlayingBeforeSleep = _state == PlaybackState.Playing;
        if (_state == PlaybackState.Playing)
        {
            DoPause();
            System.Diagnostics.Debug.WriteLine("[PoiDetail] Sleep → auto-paused + log chốt");
        }
    }

    public void HandleAppResume()
    {
        System.Diagnostics.Debug.WriteLine(
            $"[PoiDetail] Resume → state={_state} (giữ nguyên, không tự play)");
        // KHÔNG gọi DoResume() — user phải chủ động bấm ▶
    }

    // ══════════════════════════════════════════════════════════════════
    // AUDIO FOCUS LOST
    // ══════════════════════════════════════════════════════════════════

    public void HandleAudioFocusLost()
    {
        if (_state == PlaybackState.Playing || _state == PlaybackState.Buffering)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                StopSliderLoop();
                StopBufferingSpinner();
                _state = PlaybackState.Paused;
                SetPlayIcon(PlayIcon.Play);
                BufferingBadge.IsVisible = false;
                PlayerStatusMicroLabel.Text = "TẠM DỪNG";
                ProgressSlider.IsEnabled = true;
                PlayBtn.IsEnabled = true;
                StopBtn.IsVisible = true;
                RestoreOpacity();
                System.Diagnostics.Debug.WriteLine("[PoiDetail] Audio focus lost → Paused UI");
            });
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // UI HELPERS — UpdateSliderUI là trọng tâm
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Cập nhật Slider.Value và CurrentTimeLabel đồng bộ.
    /// PHẢI gọi trên MainThread.
    ///
    /// RemainingTimeLabel = tổng thời lượng (cố định, không đếm lùi).
    /// </summary>
    private void UpdateSliderUI(double sec)
    {
        double max = ProgressSlider.Maximum;
        double safe = max > 1 ? Math.Clamp(sec, 0, max) : Math.Max(0, sec);

        // ✅ Đồng bộ Slider.Value
        if (max > 1)
            ProgressSlider.Value = safe;

        // ✅ CurrentTimeLabel = giây đang ở
        CurrentTimeLabel.Text = Format(safe);

        // ✅ RemainingTimeLabel = tổng thời lượng (KHÔNG thay đổi theo seek)
        if (duration > 0)
            RemainingTimeLabel.Text = Format(duration);
    }

    private void ResetSliderUI()
    {
        // Placeholder Maximum = 1 (sẽ bị override bởi OnDuration)
        ProgressSlider.Maximum = 1;
        ProgressSlider.Value = 0;
        ProgressSlider.IsEnabled = false;

        CurrentTimeLabel.Text = "00:00";
        RemainingTimeLabel.Text = "00:00";
        TotalTimeLabel.Text = "--:--";

        RestoreOpacity();
    }

    // Compat: chỉ update labels, không update Slider.Value
    private void UpdateTimeLabels(double sec)
    {
        CurrentTimeLabel.Text = Format(sec);
        if (duration > 0)
            RemainingTimeLabel.Text = Format(duration);
    }

    // ══════════════════════════════════════════════════════════════════
    // COMPAT METHODS (không xóa — các file khác có thể dùng)
    // ══════════════════════════════════════════════════════════════════

    private void SetPlayingState(bool isPlaying)
    {
        if (isPlaying) TransitionTo(PlaybackState.Playing);
        else TransitionTo(PlaybackState.Paused);
    }

    private void SyncProgressFill(double sec) { /* no-op */ }

    private void UpdateDurationLabel(double d)
    {
        TotalTimeLabel.Text = Format(d);
        RemainingTimeLabel.Text = Format(d);
    }

    private void OnProgressContainerSizeChanged(object? sender, EventArgs e) { }

    // ══════════════════════════════════════════════════════════════════
    // LIFECYCLE
    // ══════════════════════════════════════════════════════════════════

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (_state == PlaybackState.Idle)
        {
            ResetSliderUI();
            SetPlayIcon(PlayIcon.Play);
            StopBtn.IsVisible = false;
        }

        if (poi != null && PlayerPoiNameLabel != null)
            PlayerPoiNameLabel.Text = poi.Name ?? "Thuyết minh";

        _ = Task.Run(async () =>
        {
            _cachedUserLocation = await locationService.GetLocation();
        });
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        StopSliderLoop();
        StopBufferingSpinner();
        audio.Stop();
        duration = 0;
        _state = PlaybackState.Idle;
    }

    // ══════════════════════════════════════════════════════════════════
    // NAVIGATION
    // ══════════════════════════════════════════════════════════════════

    private async void OnBackTapped(object sender, TappedEventArgs e)
    {
        audio.Stop();
        _state = PlaybackState.Idle;

        if (_openedFrom == "map")
            await Shell.Current.GoToAsync("//map");
        else if (Navigation.NavigationStack.Count > 1)
            await Navigation.PopAsync(animated: true);
        else
            await Shell.Current.GoToAsync("//home");
    }

    public void SetOpenedFrom(string source) => _openedFrom = source;

    // ══════════════════════════════════════════════════════════════════
    // LOCATION
    // ══════════════════════════════════════════════════════════════════

    private async Task<Location?> GetFreshLocationAsync()
    {
        try
        {
            var fresh = await locationService.GetLocation();
            if (fresh != null) { _cachedUserLocation = fresh; return fresh; }
        }
        catch { }
        return _cachedUserLocation;
    }

    // ══════════════════════════════════════════════════════════════════
    // DATA BINDING
    // ══════════════════════════════════════════════════════════════════

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

        if (PlayerPoiNameLabel != null)
            PlayerPoiNameLabel.Text = poi.Name ?? "Thuyết minh";

        UpdateOpenStatus();
    }

    // ══════════════════════════════════════════════════════════════════
    // TABS
    // ══════════════════════════════════════════════════════════════════

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

    // ══════════════════════════════════════════════════════════════════
    // IMAGE LIGHTBOX
    // ══════════════════════════════════════════════════════════════════

    private async void OnFoodImageTapped(object sender, TappedEventArgs e)
    {
        if (sender is Microsoft.Maui.Controls.Image img && img.BindingContext is Food food)
        {
            PreviewImage.Source = food.ImageUrl;
            ImageViewer.IsVisible = true;
            await Task.WhenAll(ImageViewer.FadeTo(1, 150), PreviewImage.ScaleTo(1, 200));
        }
    }

    private async void CloseImageViewer(object sender, TappedEventArgs e)
    {
        await Task.WhenAll(ImageViewer.FadeTo(0, 150), PreviewImage.ScaleTo(0.8, 150));
        ImageViewer.IsVisible = false;
    }

    // ══════════════════════════════════════════════════════════════════
    // OPEN STATUS
    // ══════════════════════════════════════════════════════════════════

    private void UpdateOpenStatus()
    {
        if (poi == null || poi.OpenTime == null || poi.CloseTime == null) return;

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

    // ══════════════════════════════════════════════════════════════════
    // FORMAT UTILS
    // ══════════════════════════════════════════════════════════════════

    private static string Format(double sec)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, sec));
        return $"{(int)t.TotalMinutes:D2}:{t.Seconds:D2}";
    }
}
