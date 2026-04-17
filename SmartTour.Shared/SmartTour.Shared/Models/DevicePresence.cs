using System.ComponentModel.DataAnnotations;

namespace SmartTour.Shared.Models;

public class DevicePresence
{
    public int Id { get; set; }

    [MaxLength(128)]
    public string DeviceId { get; set; } = "";

    [MaxLength(128)]
    public string IpAddress { get; set; } = "";

    [MaxLength(512)]
    public string UserAgent { get; set; } = "";

    [MaxLength(128)]
    public string DeviceModel { get; set; } = "";

    [MaxLength(64)]
    public string Platform { get; set; } = "";

    [MaxLength(64)]
    public string OsVersion { get; set; } = "";

    [MaxLength(32)]
    public string AppVersion { get; set; } = "";

    public DateTime LastSeenUtc { get; set; }
}
