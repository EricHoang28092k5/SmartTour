namespace SmartTourCMS.Models;

public class PoiPendingApprovalRowViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string VendorEmail { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string ApprovalStatus { get; set; } = "pending";
    public string RequestType { get; set; } = "create";
    public string ScriptPreview { get; set; } = string.Empty;
    public List<PoiFieldChangeViewModel> ChangedFields { get; set; } = [];
}

public class PoiFieldChangeViewModel
{
    public string Field { get; set; } = string.Empty;
    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
}
