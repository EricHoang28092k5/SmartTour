using System.ComponentModel.DataAnnotations;

namespace SmartTour.Shared.Models;

public class ScriptChangeRequest
{
    [Key]
    public long Id { get; set; }

    public int PoiId { get; set; }

    [MaxLength(20)]
    public string LanguageCode { get; set; } = "en";

    public string NewScript { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Status { get; set; } = "pending";

    [MaxLength(128)]
    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(128)]
    public string? ReviewedByUserId { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public string? RejectReason { get; set; }
}
