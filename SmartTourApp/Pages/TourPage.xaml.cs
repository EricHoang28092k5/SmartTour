using SmartTour.Services;
using SmartTour.Shared.Models;
using SmartTourApp.Services;
using System.Collections.ObjectModel;

namespace SmartTourApp.Pages;

public partial class TourPage : ContentPage
{
    private readonly ApiService _api;
    private readonly NarrationEngine _narration;
    private readonly TourOfflineManager _offlineManager;
    private readonly LocalizationService _loc;

    private ObservableCollection<TourViewModel> _tours = new();

    // Track progress bars per tourId (view ref)
    private readonly Dictionary<int, (Border bar, Label label, Label percent, Border fill)> _progressViews = new();

    public TourPage(
        ApiService api,
        NarrationEngine narration,
        TourOfflineManager offlineManager,
        LocalizationService loc)
    {
        InitializeComponent();
        _api = api;
        _narration = narration;
        _offlineManager = offlineManager;
        _loc = loc;

        // Subscribe offline events
        _offlineManager.OnStatusChanged += OnTourOfflineStatusChanged;
        _offlineManager.OnProgress += OnTourDownloadProgress;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Apply localization
        ApplyLocalization();

        if (_tours.Count == 0)
            await LoadTours();
    }

    private void ApplyLocalization()
    {
        LblSubtitle.Text = _loc.TourPageSubtitle;
        LblTitle.Text = _loc.TourPageTitle;
    }

    private async Task LoadTours()
    {
        try
        {
            var res = await _api.GetTours();
            if (res?.Data == null) return;

            var mapped = res.Data.Select(t => new TourViewModel
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                Pois = t.Pois?
                    .OrderBy(p => p.OrderIndex)
                    .ToList() ?? new List<TourPoiDto>()
            });

            foreach (var t in mapped)
                _tours.Add(t);

            TourList.ItemsSource = _tours;
            TourCountLabel.Text = string.Format(_loc.TourCount, _tours.Count);

            // Auto-check stale offline data
            await CheckOfflineStatusAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(_loc.TourLoadError, ex.Message, _loc.OK);
        }
    }

    private async Task CheckOfflineStatusAsync()
    {
        foreach (var tour in _tours)
        {
            var status = _offlineManager.GetStatus(tour.Id, tour.Pois);
            tour.OfflineStatus = status;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // OFFLINE DOWNLOAD
    // ══════════════════════════════════════════════════════════════

    private async void OnDownloadOfflineTapped(object sender, EventArgs e)
    {
        TourViewModel? tour = null;
        if (sender is Button btn && btn.CommandParameter is TourViewModel t)
            tour = t;
        if (tour == null) return;

        // Đã offline ready → hỏi re-download
        if (tour.OfflineStatus == TourOfflineStatus.Ready)
        {
            bool confirm = await DisplayAlert(
                _loc.TourOfflineTitle,
                $"Tour \"{tour.Name}\" {_loc.TourOfflineReady.ToLower()}.\nTải lại dữ liệu mới nhất?",
                _loc.OK, _loc.Cancel);
            if (!confirm) return;
        }

        // Đang download → hủy
        if (tour.OfflineStatus == TourOfflineStatus.Downloading)
        {
            _offlineManager.CancelDownload(tour.Id);
            return;
        }

        // Kiểm tra mạng
        if (!OfflineMapService.IsConnected())
        {
            await DisplayAlert(_loc.NoNetwork, _loc.NoNetworkMsg, _loc.OK);
            return;
        }

        // Bắt đầu download
        tour.OfflineStatus = TourOfflineStatus.Downloading;
        _ = _offlineManager.DownloadTourAsync(tour.Id, tour.Pois);
    }

    private void OnTourOfflineStatusChanged(int tourId, TourOfflineStatus status)
    {
        var tour = _tours.FirstOrDefault(t => t.Id == tourId);
        if (tour == null) return;

        tour.OfflineStatus = status;

        // Refresh CollectionView item (MAUI limitation)
        TourList.ItemsSource = null;
        TourList.ItemsSource = _tours;
    }

    private void OnTourDownloadProgress(int tourId, TourDownloadProgress progress)
    {
        // Progress sẽ được reflect qua TourViewModel.OfflineStatus
        // Detailed progress: hiển thị qua toast/snackbar nếu muốn
        System.Diagnostics.Debug.WriteLine(
            $"[TourPage] Tour {tourId}: {progress.Message} ({(int)(progress.Percent * 100)}%)");
    }

    // ══════════════════════════════════════════════════════════════
    // EXPAND / COLLAPSE
    // ══════════════════════════════════════════════════════════════

    private void OnExpandTapped(object sender, EventArgs e)
    {
        TourViewModel? tour = null;
        if (sender is Button btn && btn.CommandParameter is TourViewModel t1)
            tour = t1;
        if (tour == null) return;

        tour.IsExpanded = !tour.IsExpanded;
        if (sender is Button b)
            b.Text = tour.IsExpanded ? _loc.Collapse : _loc.Details;
    }

    // Giữ nguyên tên hàm cũ — không xóa
    private void OpenTour(object sender, EventArgs e)
    {
        TourViewModel? tour = null;
        if (sender is Button btn && btn.CommandParameter is TourViewModel t1)
            tour = t1;
        else if (sender is Label lbl && lbl.BindingContext is TourViewModel t2)
            tour = t2;

        if (tour != null)
            tour.IsExpanded = !tour.IsExpanded;
    }

    // ══════════════════════════════════════════════════════════════
    // MAP
    // ══════════════════════════════════════════════════════════════

    private async void StartTourOnMap(object sender, EventArgs e)
    {
        TourViewModel? tour = null;
        if (sender is Button btn && btn.CommandParameter is TourViewModel t)
            tour = t;
        if (tour == null) return;

        if (tour.Pois == null || tour.Pois.Count < 2)
        {
            await DisplayAlert(_loc.TourNeedMinPoi, _loc.TourNeedMinPoiMsg, _loc.OK);
            return;
        }

        TourSession.StartTour(tour);
        await Shell.Current.GoToAsync($"//map?tourId={tour.Id}");
    }

    // Giữ nguyên StartTour cũ
    private async void StartTour(object sender, EventArgs e)
    {
        TourViewModel? tour = null;
        if (sender is Button btn && btn.CommandParameter is TourViewModel t)
            tour = t;
        if (tour == null) return;

        foreach (var p in tour.Pois)
        {
            var fakeLocation = new Location(p.Lat, p.Lng);
            await _narration.PlayManual(new Poi
            {
                Id = p.PoiId,
                Name = p.Name,
                Lat = p.Lat,
                Lng = p.Lng
            }, fakeLocation);
            await Task.Delay(1000);
        }
    }
}
