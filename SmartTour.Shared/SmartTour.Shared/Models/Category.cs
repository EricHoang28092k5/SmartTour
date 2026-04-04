using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using SQLite;

namespace SmartTour.Shared.Models
{
    public class Category
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Required(ErrorMessage = "Tên danh mục không được để trống")]
        public string Name { get; set; } = string.Empty; // VD: Quán ăn, Tham quan, Lưu trú...

        public string? Description { get; set; } // Mô tả ngắn

        // CHỨA LINK ẢNH (Up lên Cloudinary rồi lưu link vào đây)
        public string? IconUrl { get; set; }

        // Màu của cái ghim trên bản đồ (VD: "#FF3366")
        public string? ColorCode { get; set; }

        // --- MỐI QUAN HỆ (1 Danh mục chứa nhiều POI) ---
        [JsonIgnore]
        [SQLite.Ignore]
        public ICollection<Poi>? Pois { get; set; }
    }
}