using System;
using System.Collections.Generic;
using System.Text;

namespace SmartTour.Shared.Models
{
    public class PoiImage
    {
        public int Id { get; set; }
        public string ImageUrl { get; set; } // Đường dẫn ảnh trên Cloudinary

        // Dây mơ rễ má với thằng bố POI
        public int PoiId { get; set; }
        public Poi? Poi { get; set; }
    }
}
