namespace SmartTour.Shared.Models;

/// <summary>
/// Dữ liệu tạm lưu trong đơn MoMo trước khi tạo bản ghi POI (sau thanh toán thành công).
/// </summary>
public class PoiCreationDraftPayload
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? TtsScript { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
    public int Radius { get; set; } = 100;
    public string ImageUrl { get; set; } = string.Empty;
    public int Priority { get; set; } = 1;
    public string? OpenTime { get; set; }
    public string? CloseTime { get; set; }
    public int? CategoryId { get; set; }
    public string VendorId { get; set; } = string.Empty;
    public string? CreatedBy { get; set; }
}
