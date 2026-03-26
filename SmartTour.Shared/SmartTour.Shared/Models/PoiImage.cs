using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization; // Nhớ thêm dòng này ở trên cùng nhé bác
namespace SmartTour.Shared.Models
{
    public class PoiImage
    {
        public int Id { get; set; }
        public string ImageUrl { get; set; } // Đường dẫn ảnh trên Cloudinary

        // Dây mơ rễ má với thằng bố POI
        public int PoiId { get; set; }
        [JsonIgnore] // BƠM CÁI BÙA NÀY VÀO ĐÂY
        public Poi? Poi { get; set; }
    }
}
