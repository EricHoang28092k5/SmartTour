using System;
using System.Collections.Generic;
using System.Text;

namespace SmartTour.Shared.Models
{
    public class TourTranslation
    {
        public int Id { get; set; }

        public int TourId { get; set; }
        public Tour Tour { get; set; } // Khóa ngoại móc vào bảng Tour

        public string LanguageCode { get; set; } // Chứa mã ngôn ngữ: "en", "ko", "ja"...

        public string Name { get; set; } // Tên Tour sau khi dịch
        public string Description { get; set; } // Mô tả sau khi dịch
    }
}
