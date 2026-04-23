using System.ComponentModel.DataAnnotations;

namespace SmartTour.Shared.Models;

public class AudioPipelineJob
{
    [Key]
    public long Id { get; set; }

    [MaxLength(50)]
    public string JobType { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Status { get; set; } = "pending";

    public int PoiId { get; set; }

    public int? TranslationId { get; set; }

    public string PayloadJson { get; set; } = "{}";

    public int RetryCount { get; set; }

    public int MaxRetries { get; set; } = 5;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? NextRetryAt { get; set; }

    public DateTime? ProcessedAt { get; set; }

    public string? LastError { get; set; }
}
