using SQLite;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartTour.Shared.Models
{
    /// <summary>
    /// Đại diện cho một tuyến đi hoàn chỉnh của user.
    /// Chỉ được lưu lên server khi có ít nhất 2 POI khác nhau liên tiếp.
    /// </summary>
    public class RouteSession
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>Device ID ẩn danh (giống HeatmapService).</summary>
        [MaxLength(128)]
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>Chuỗi POI theo thứ tự thời gian, phân cách bởi dấu phẩy. VD: "102,108,102".</summary>
        public string PoiSequence { get; set; } = string.Empty;

        /// <summary>Số điểm dừng hợp lệ trong tuyến.</summary>
        public int StopCount { get; set; }

        /// <summary>Thời điểm bắt đầu tuyến (UTC).</summary>
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Thời điểm kết thúc tuyến (UTC).</summary>
        public DateTime EndedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Tổng thời gian tuyến (phút).</summary>
        public int DurationMinutes { get; set; }

        /// <summary>Trạng thái: "active" | "completed" | "expired".</summary>
        [MaxLength(20)]
        public string Status { get; set; } = "active";

        // ─── Navigation (server-side EF only) ───
        [Ignore]
        [NotMapped]
        public List<RouteSessionPoi>? RouteSessionPois { get; set; }
    }

    /// <summary>
    /// Chi tiết từng điểm dừng trong một tuyến.
    /// Lưu cả loại trigger để phân tích sau.
    /// </summary>
    public class RouteSessionPoi
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public int RouteSessionId { get; set; }

        public int PoiId { get; set; }

        /// <summary>Thứ tự trong tuyến (1-based).</summary>
        public int OrderIndex { get; set; }

        /// <summary>
        /// Loại trigger:
        /// "dwell"        = ở lại &gt; 5 phút
        /// "audio_manual" = nghe audio chủ động khi trong radius
        /// </summary>
        [MaxLength(20)]
        public string TriggerType { get; set; } = string.Empty;

        /// <summary>Thời điểm điểm dừng này được xác nhận (UTC).</summary>
        public DateTime TriggeredAt { get; set; } = DateTime.UtcNow;

        /// <summary>Số giây user ở trong radius tại thời điểm ghi nhận.</summary>
        public int DwellSeconds { get; set; }

        [Ignore]
        [NotMapped]
        public Poi? Poi { get; set; }
    }
}
