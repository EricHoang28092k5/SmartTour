using System.ComponentModel.DataAnnotations;

namespace SmartTour.Shared.Models;

/// <summary>
/// Lần gần nhất app mobile gửi heartbeat (ước lượng thiết bị đang mở app).
/// </summary>
public class DevicePresence
{
    public int Id { get; set; }

    [MaxLength(128)]
    public string DeviceId { get; set; } = "";

    public DateTime LastSeenUtc { get; set; }
}
