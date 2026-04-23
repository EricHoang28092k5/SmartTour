using System.ComponentModel.DataAnnotations;

namespace SmartTour.Shared.Models;

public class PoiAudioListenEvent
{
    [Key]
    public long Id { get; set; }

    public int PoiId { get; set; }

    public int DurationSeconds { get; set; }

    [MaxLength(128)]
    public string DeviceId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
