namespace SmartTourCMS.Models;

/// <summary>
/// Bản nháp tạo POI (vendor) lưu Session trước khi xác nhận trừ ví.
/// </summary>
public class VendorPoiCreateDraft
{
    public string VendorUserId { get; set; } = string.Empty;
    public string? CreatedBy { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? TtsScript { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
    public int Radius { get; set; } = 100;
    public string ImageUrl { get; set; } = string.Empty;
    public int Priority { get; set; } = 1;
    public long? OpenTicks { get; set; }
    public long? CloseTicks { get; set; }
    public int? CategoryId { get; set; }
    public long ChargeVnd { get; set; }
    /// <summary>Không còn dùng để tính phí; giữ field để tương thích session cũ.</summary>
    public int TotalTtsChars { get; set; }
}

public class PoiCreateConfirmViewModel
{
    public VendorPoiCreateDraft Draft { get; set; } = new();
    public long BalanceVnd { get; set; }
    public bool SufficientBalance { get; set; }
    public long MinWalletTopUpVnd { get; set; }
}
