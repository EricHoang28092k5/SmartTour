using SmartTour.Services;
using SmartTour.Shared.Models;
using SmartTourApp.Services;

namespace SmartTourApp.Pages;

public partial class TourPage : ContentPage
{
    private readonly ApiService api;
    private readonly PoiRepository repo;
    private readonly OfflineSyncService offlineSync;
    private readonly OfflineDatabase offlineDb;

    private List<TourViewModel> tours = new();
    private bool isLoaded = false;
    private bool isPrefetching = false;

    // ── Track connectivity để cập nhật UI ──
    private CancellationTokenSource? _prefetchCts;

    public TourPage(
        ApiService api,
        PoiRepository repo,
        OfflineSyncService offlineSync,
        OfflineDatabase offlineDb)
    {
        InitializeComponent();
        this.api = api;
        this.repo = repo;
        this.offlineSync = offlineSync;
        this.offlineDb = offlineDb;

        // ── Subscribe connectivity changes ──
        offlineSync.OnConnectivityChanged += OnConnectivityChanged;
        offlineSync.OnProgress += OnSyncProgress;
    }

    // ══════════════════════════════════════════════════════════════════
    // LIFECYCLE
    // ══════════════════════════════════════════════════════════════════

    protected override void OnAppearing()
    {
        base.OnAppearing();

        UpdateConnectivityUI();
        UpdateOfflineStats();

        if (!isLoaded)
            _ = LoadToursAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _prefetchCts?.Cancel();
    }

    // ══════════════════════════════════════════════════════════════════
    // LOAD TOURS
    // ══════════════════════════════════════════════════════════════════

    private async Task LoadToursAsync()
    {
        isLoaded = true;

        try
        {
            var response = await api.GetTours();
            if (response?.Data == null) return;

            tours = response.Data.Select(t => new TourViewModel
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                Pois = t.Pois ?? new List<TourPoiDto>()
            }).ToList();

            TourList.ItemsSource = tours;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TourPage] Load error: {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // PRE-FETCH (Yêu cầu 1 — User bấm "Tải xuống offline")
    // ══════════════════════════════════════════════════════════════════

    private async void OnPrefetchClicked(object sender, EventArgs e)
    {
        if (isPrefetching) return;

        // Lấy danh sách POI từ cache hoặc API
        var pois = repo.GetCachedPois() ?? await repo.GetPois();
        if (pois.Count == 0)
        {
            await DisplayAlert("Thông báo", "Chưa có dữ liệu địa điểm", "OK");
            return;
        }

        // Kiểm tra có mạng không
        var access = Connectivity.Current.NetworkAccess;
        if (access != NetworkAccess.Internet && access != NetworkAccess.ConstrainedInternet)
        {
            await DisplayAlert(
                "Không có mạng",
                "Vui lòng kết nối WiFi hoặc 4G để tải dữ liệu offline.",
                "OK");
            return;
        }

        isPrefetching = true;
        _prefetchCts = new CancellationTokenSource();

        try
        {
            // Hiển thị progress UI
            PrefetchBtn.Text = "⏹  Hủy";
            PrefetchBtn.Clicked -= OnPrefetchClicked;
            PrefetchBtn.Clicked += OnCancelPrefetchClicked;
            PrefetchProgressSection.IsVisible = true;

            await offlineSync.PrefetchPoiDataAsync(pois, _prefetchCts.Token);

            // Done!
            PrefetchBtn.Text = "✅  Đã tải xong";
            PrefetchSubtitle.Text = $"Đã lưu {pois.Count} địa điểm — sẵn sàng offline!";

            UpdateOfflineStats();

            await Task.Delay(2000);
        }
        catch (OperationCanceledException)
        {
            PrefetchBtn.Text = "⬇  Tải xuống offline";
            PrefetchSubtitle.Text = "Tải thuyết minh về máy trước khi tour bắt đầu";
        }
        finally
        {
            isPrefetching = false;
            PrefetchProgressSection.IsVisible = false;
            PrefetchBtn.Text = "⬇  Tải xuống offline";
            PrefetchBtn.Clicked -= OnCancelPrefetchClicked;
            PrefetchBtn.Clicked += OnPrefetchClicked;
        }
    }

    private void OnCancelPrefetchClicked(object? sender, EventArgs e)
    {
        _prefetchCts?.Cancel();
    }

    // ══════════════════════════════════════════════════════════════════
    // SYNC PROGRESS CALLBACK
    // ══════════════════════════════════════════════════════════════════

    private void OnSyncProgress(SyncProgress progress)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            PrefetchProgressLabel.Text = progress.Message;
            PrefetchPercentLabel.Text = $"{progress.Percent * 100:F0}%";

            // Cập nhật progress bar
            var parent = PrefetchProgressFill.Parent as View;
            double trackWidth = parent?.Width ?? 300;
            PrefetchProgressFill.WidthRequest = trackWidth * progress.Percent;
        });
    }

    // ══════════════════════════════════════════════════════════════════
    // CONNECTIVITY UI
    // ══════════════════════════════════════════════════════════════════

    private void OnConnectivityChanged(bool isOnline)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateConnectivityUI(isOnline);

            if (isOnline)
            {
                // Mạng khôi phục → show toast ngắn
                PrefetchSubtitle.Text = "🌐 Kết nối khôi phục — có thể tải offline";
            }
            else
            {
                PrefetchSubtitle.Text = "📦 Đang dùng dữ liệu offline";
            }
        });
    }

    private void UpdateConnectivityUI(bool? isOnline = null)
    {
        isOnline ??= Connectivity.Current.NetworkAccess == NetworkAccess.Internet ||
                     Connectivity.Current.NetworkAccess == NetworkAccess.ConstrainedInternet;

        if (isOnline.Value)
        {
            OfflineStatusIcon.Text = "🌐";
            OfflineStatusLabel.Text = "Online";
            OfflineStatusLabel.TextColor = Color.FromArgb("#1565C0");
            OfflineStatusBadge.BackgroundColor = Color.FromArgb("#F0F7FF");
            OfflineStatusBadge.Stroke = new SolidColorBrush(Color.FromArgb("#BBDEFB"));
        }
        else
        {
            OfflineStatusIcon.Text = "📦";
            OfflineStatusLabel.Text = "Offline";
            OfflineStatusLabel.TextColor = Color.FromArgb("#E65100");
            OfflineStatusBadge.BackgroundColor = Color.FromArgb("#FFF8F0");
            OfflineStatusBadge.Stroke = new SolidColorBrush(Color.FromArgb("#FFE0B2"));
        }
    }

    private void UpdateOfflineStats()
    {
        try
        {
            CachedPoiCountLabel.Text = offlineDb.GetCachedPoiCount().ToString();
            PendingLogCountLabel.Text = offlineDb.GetPendingLogCount().ToString();
        }
        catch { }
    }

    // ══════════════════════════════════════════════════════════════════
    // TOUR INTERACTIONS
    // ══════════════════════════════════════════════════════════════════

    private void OnTourTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not TourViewModel tour) return;

        tour.IsExpanded = !tour.IsExpanded;

        // Refresh để trigger IsExpanded binding
        TourList.ItemsSource = null;
        TourList.ItemsSource = tours;
    }

    private async void GoToMapFromTour(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not int poiId) return;
        await Shell.Current.GoToAsync($"//map?targetPoi={poiId}");
    }
}
