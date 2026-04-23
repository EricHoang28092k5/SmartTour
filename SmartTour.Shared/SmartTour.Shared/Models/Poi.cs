using SQLite;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartTour.Shared.Models
{
    public class Poi
    {
        [PrimaryKey]
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public double Lat { get; set; }
        public double Lng { get; set; }
        public int Radius { get; set; }
        public string Description { get; set; } = "";
        public string AudioUrl { get; set; } = ""; // Giữ lại cho App di động bản cũ
        public string ImageUrl { get; set; } = "";
        public int Priority { get; set; } = 1;
        public string? TtsScript { get; set; }
        public bool IsActive { get; set; } = true;
        public string ApprovalStatus { get; set; } = "approved"; // pending|approved|rejected
        public string? ApprovedByUserId { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? ApprovalNote { get; set; }

        [Ignore]
        public bool IsNearest { get; set; }

        /// <summary>
        /// YC3: Tên hiển thị đã được dịch theo ngôn ngữ hiện tại.
        /// Được set bởi HomePage và MapPage sau khi fetch TTS scripts.
        /// Mặc định là Name nếu chưa có translation.
        /// </summary>
        [Ignore]
        public string DisplayName
        {
            get => string.IsNullOrWhiteSpace(_displayName) ? Name : _displayName;
            set => _displayName = value;
        }
        private string _displayName = "";

        public string? CreatedBy { get; set; }
        public string? VendorId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public TimeSpan? OpenTime { get; set; }
        public TimeSpan? CloseTime { get; set; }
        public int? CategoryId { get; set; }

        // --- CÁC QUAN HỆ (CHỈ DÙNG TRÊN SERVER NÊN PHẢI [IGNORE] ĐỂ APP DI ĐỘNG KHÔNG LỖI) ---
        [Ignore]
        public virtual ICollection<PoiImage> PoiImages { get; set; } = new List<PoiImage>();

        [Ignore]
        public virtual ICollection<PoiTranslation> PoiTranslations { get; set; } = new List<PoiTranslation>();

        [Ignore]
        public virtual ICollection<AudioFile> AudioFiles { get; set; } = new List<AudioFile>();

        [Ignore]
        public virtual ICollection<Food>? Foods { get; set; }

        [Ignore]
        public Category? Category { get; set; }

        [Ignore]
        [NotMapped]
        public object? GeoLocation { get; set; }
    }
}
