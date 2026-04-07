using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartTour.Shared.Models
{
    public class Tour
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Tên Tour không được để trống")]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedBy { get; set; }
        public string? VendorId { get; set; }

        // --- MỐI QUAN HỆ ---
        // Một Tour sẽ có danh sách các POI thông qua bảng trung gian TourPoi
        public List<TourPoi>? TourPois { get; set; }
        [NotMapped] // Bùa này bắt Database bỏ qua, KHÔNG tạo cột này dưới DB Neon
        public List<int> SelectedPoiIds { get; set; } = new List<int>();
        public ICollection<TourTranslation> TourTranslations { get; set; } = new List<TourTranslation>();
    }
}