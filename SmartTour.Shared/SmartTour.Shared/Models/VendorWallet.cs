namespace SmartTour.Shared.Models;

public class VendorWallet
{
    public string VendorUserId { get; set; } = string.Empty;
    public long BalanceVnd { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
