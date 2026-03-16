using System.ComponentModel.DataAnnotations;

namespace SmartTour.Shared.Models
{
    public class Tour
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Tên Tour không được để trống")]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // --- MỐI QUAN HỆ ---
        // Một Tour sẽ có danh sách các POI thông qua bảng trung gian TourPoi
        public List<TourPoi>? TourPois { get; set; }
    }
}