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

    private async void OnPlayClicked(object sender, EventArgs e)
    {
        if (poi == null) return;

        await audio.Play(poi);

        PlayBtn.IsEnabled = false;
        PauseBtn.IsEnabled = true;
    }

    private void OnPauseClicked(object sender, EventArgs e)
    {
        audio.Pause();

        PlayBtn.IsEnabled = true;
        PauseBtn.IsEnabled = false;
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
            audio.Seek(e.NewValue);
    }

    // ================= TAB UX =================

    private void ShowOverview(object sender, EventArgs e)
    {
        OverviewSection.IsVisible = true;
        FoodList.IsVisible = false;
        AudioBar.IsVisible = true;

        OverviewTab.BackgroundColor = Color.FromArgb("#1976D2");
        OverviewTab.TextColor = Colors.White;
        MenuTab.BackgroundColor = Color.FromArgb("#EEE");
        MenuTab.TextColor = Colors.Black;
    }

    private void ShowMenu(object sender, EventArgs e)
    {
        OverviewSection.IsVisible = false;
        FoodList.IsVisible = true;
        AudioBar.IsVisible = false; // 🔥 UX requirement

        MenuTab.BackgroundColor = Color.FromArgb("#1976D2");
        MenuTab.TextColor = Colors.White;
        OverviewTab.BackgroundColor = Color.FromArgb("#EEE");
        OverviewTab.TextColor = Colors.Black;
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
    }

    // ================= IMAGE =================

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

    private void UpdateOpenStatus()
    {
        if (poi == null || poi.OpenTime == null || poi.CloseTime == null)
            return;

        var now = DateTime.Now.TimeOfDay;
        var open = poi.OpenTime.Value;
        var close = poi.CloseTime.Value;

        if (open == close)
        {
            OpenStatus.Text = "🔵 Mở cả ngày";
            OpenStatus.TextColor = Colors.Blue;
            return;
        }

        if (now >= open && now <= close)
        {
            OpenStatus.Text = "🟢 Đang mở cửa";
            OpenStatus.TextColor = Colors.Green;
            OpenDetail.Text = $"Đóng lúc {close:hh\\:mm}";
        }
        else
        {
            OpenStatus.Text = "🔴 Đã đóng cửa";
            OpenStatus.TextColor = Colors.Red;
            OpenDetail.Text = $"Mở lúc {open:hh\\:mm}";
        }
    }
}