using SmartTour.Shared.Models;

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
/// Dùng static để share giữa TourPage và MapPage mà không cần DI thêm.
/// </summary>
public static class TourSession
{
    private static TourViewModel? _activeTour;
    private static int _currentStepIndex = 0;

    public static TourViewModel? ActiveTour => _activeTour;
    public static bool IsActive => _activeTour != null && _activeTour.Pois.Count > 1;
    public static int CurrentStepIndex => _currentStepIndex;

    /// <summary>Khởi động tour mới — ghi đè tour cũ nếu có.</summary>
    public static void StartTour(TourViewModel tour)
    {
        _activeTour = tour;
        _currentStepIndex = 0;
    }

    /// <summary>Advance sang step tiếp theo sau khi user bấm Đường đi tới poi[n].</summary>
    public static void AdvanceStep()
    {
        if (_activeTour == null) return;
        _currentStepIndex = Math.Min(_currentStepIndex + 1, _activeTour.Pois.Count - 1);
    }

    /// <summary>Thoát tour mode — reset toàn bộ state.</summary>
    public static void EndTour()
    {
        _activeTour = null;
        _currentStepIndex = 0;
    }

    /// <summary>Lấy POI hiện tại theo step index.</summary>
    public static TourPoiDto? CurrentPoi =>
        _activeTour != null && _currentStepIndex < _activeTour.Pois.Count
            ? _activeTour.Pois[_currentStepIndex]
            : null;

    /// <summary>Lấy POI theo order index (1-based từ API, 0-based trong list).</summary>
    public static TourPoiDto? GetPoiAt(int zeroIndex) =>
        _activeTour != null && zeroIndex < _activeTour.Pois.Count
            ? _activeTour.Pois[zeroIndex]
            : null;
}
