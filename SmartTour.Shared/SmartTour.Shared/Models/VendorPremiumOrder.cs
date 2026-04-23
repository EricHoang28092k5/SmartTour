namespace SmartTour.Shared.Models;

public class VendorPremiumOrder
{
    public long Id { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public int PoiId { get; set; }
    public string VendorUserId { get; set; } = string.Empty;
    public long Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public string Provider { get; set; } = "momo";
    public string Status { get; set; } = "pending";
    public long? MoMoTransId { get; set; }
    public string? RawIpnPayload { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
