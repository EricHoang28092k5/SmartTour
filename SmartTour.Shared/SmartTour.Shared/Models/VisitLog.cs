using System.ComponentModel.DataAnnotations;

namespace SmartTour.Shared.Models;

/// <summary>
/// Bản ghi lượt ghé POI (ghi theo lô bởi VisitLogWorker).
/// </summary>
public class VisitLog
{
    [Key]
    public int Id { get; set; }

    public int PoiId { get; set; }

    [MaxLength(256)]
    public string UserId { get; set; } = string.Empty;

    public double Lat { get; set; }

    public double Lng { get; set; }

    public DateTime VisitTime { get; set; }

    public VisitType VisitType { get; set; }

    /// <summary>Tốc độ tại thời điểm visit (km/h), null nếu không gửi / không đo được.</summary>
    public double? SpeedKmh { get; set; }
}
