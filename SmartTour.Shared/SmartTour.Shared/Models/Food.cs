using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

using System.Text.Json.Serialization;

namespace SmartTour.Shared.Models
{
    [Table("Food")] // <-- DÁN CÁI BÙA NÀY NGAY TRÊN ĐẦU CLASS
    public class Food
    {
        public int Id { get; set; }
        public string Name { get; set; } // Tên món (vd: Phở Bò)
        public string Description { get; set; } // Mô tả ngắn gọn
        public decimal Price { get; set; } // Giá tiền

        public string? ImageUrl { get; set; } // CHỈ 1 ẢNH DUY NHẤT

        // --- DÂY MƠ RỄ MÁ VỚI ĐỊA ĐIỂM (POI) ---
        // Để biết món này thuộc nhà hàng nào
        public int PoiId { get; set; }

        [JsonIgnore] // Chặn lỗi vòng lặp JSON 
        public Poi? Poi { get; set; }
    }
}
