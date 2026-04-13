using SmartTour.Shared.Models;
using SmartTourApp.Services;

public class TourViewModel : BindableObject
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    public List<TourPoiDto> Pois { get; set; } = new();

    private bool isExpanded;
    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            isExpanded = value;
            OnPropertyChanged();
        }
    }

    private TourOfflineStatus offlineStatus = TourOfflineStatus.NotDownloaded;
    public TourOfflineStatus OfflineStatus
    {
        get => offlineStatus;
        set
        {
            offlineStatus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OfflineStatusIcon));
            OnPropertyChanged(nameof(OfflineStatusText));
            OnPropertyChanged(nameof(IsDownloading));
        }
    }

    /// <summary>Icon hiển thị trạng thái offline.</summary>
    public string OfflineStatusIcon => OfflineStatus switch
    {
        TourOfflineStatus.Ready => "✅",
        TourOfflineStatus.Downloading => "⏳",
        TourOfflineStatus.Partial => "⚠️",
        TourOfflineStatus.Error => "❌",
        _ => "📥"
    };

    /// <summary>Text hiển thị trạng thái offline (ngắn gọn).</summary>
    public string OfflineStatusText => OfflineStatus switch
    {
        TourOfflineStatus.Ready => "Offline",
        TourOfflineStatus.Downloading => "...",
        TourOfflineStatus.Partial => "Partial",
        TourOfflineStatus.Error => "Error",
        _ => "DL"
    };

    public bool IsDownloading => OfflineStatus == TourOfflineStatus.Downloading;
}

public class TourResponse
{
    public bool Success { get; set; }
    public List<TourDto> Data { get; set; } = new();
}

public class TourDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public List<TourPoiDto>? Pois { get; set; }
}

public class TourPoiDto
{
    public int PoiId { get; set; }
    public string Name { get; set; } = "";
    public double Lat { get; set; }
    public double Lng { get; set; }
    public int OrderIndex { get; set; }
}

/// <summary>
/// Singleton lưu trạng thái tour map mode hiện tại.
/// </summary>
public static class TourSession
{
    private static TourViewModel? _activeTour;
    private static int _currentStepIndex = 0;

    public static TourViewModel? ActiveTour => _activeTour;
    public static bool IsActive => _activeTour != null && _activeTour.Pois.Count > 1;
    public static int CurrentStepIndex => _currentStepIndex;

    public static void StartTour(TourViewModel tour)
    {
        _activeTour = tour;
        _currentStepIndex = 0;
    }

    public static void AdvanceStep()
    {
        if (_activeTour == null) return;
        _currentStepIndex = Math.Min(_currentStepIndex + 1, _activeTour.Pois.Count - 1);
    }

    public static void EndTour()
    {
        _activeTour = null;
        _currentStepIndex = 0;
    }

    public static TourPoiDto? CurrentPoi =>
        _activeTour != null && _currentStepIndex < _activeTour.Pois.Count
            ? _activeTour.Pois[_currentStepIndex]
            : null;

    public static TourPoiDto? GetPoiAt(int zeroIndex) =>
        _activeTour != null && zeroIndex < _activeTour.Pois.Count
            ? _activeTour.Pois[zeroIndex]
            : null;
}
