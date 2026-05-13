namespace SmartTour.Shared.Models;

public class VendorWalletLedgerEntry
{
    public long Id { get; set; }
    public string VendorUserId { get; set; } = string.Empty;
    /// <summary>Dương: nạp; âm: chi.</summary>
    public long DeltaVnd { get; set; }
    public long BalanceAfterVnd { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
