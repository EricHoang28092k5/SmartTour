using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartTour.Shared.Models
{
    /// <summary>
    /// Lưu tổng số lần user bước vào vùng radius của từng POI.
    /// Mỗi row = 1 lần trigger (app mở trong radius hoặc bước vào radius).
    /// </summary>
    public class HeatmapEntry
    {
        [Key]
        public int Id { get; set; }

        /// <summary>POI được ghi nhận.</summary>
        public int PoiId { get; set; }

        /// <summary>
        /// Device identifier (anonymous). Dùng để enforce delay chống spam.
        /// Không liên kết với user account.
        /// </summary>
        [MaxLength(128)]
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>Thời điểm ghi nhận (UTC).</summary>
        public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Trigger type:
        /// "app_open"   = lần đầu mở app và đang đứng trong radius
        /// "zone_enter" = bước vào radius từ bên ngoài
        /// </summary>
        [MaxLength(20)]
        public string TriggerType { get; set; } = "zone_enter";

        /// <summary>Vị trí user tại thời điểm ghi nhận (tuỳ chọn, phục vụ viz).</summary>
        public double Lat { get; set; }
        public double Lng { get; set; }
    }
}
