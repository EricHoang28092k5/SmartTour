namespace SmartTourCMS.Models;

public class DeviceStatusViewModel
{
    public string DeviceId { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string DeviceModel { get; set; } = "";
    public string Platform { get; set; } = "";
    public string OsVersion { get; set; } = "";
    public string AppVersion { get; set; } = "";
    public string UserAgent { get; set; } = "";
    public DateTime LastSeenUtc { get; set; }
    public bool IsActive { get; set; }
}
