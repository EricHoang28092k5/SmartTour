namespace SmartTour.Shared.Models
{
    public class TourPoi
    {
        public int Id { get; set; }

        // Khóa ngoại kết nối tới Tour
        public int TourId { get; set; }
        public Tour? Tour { get; set; } // Thêm dòng này để truy cập data của Tour

        // Khóa ngoại kết nối tới POI
        public int PoiId { get; set; }
        public Poi? Poi { get; set; } // Thêm dòng này để truy cập data của POI

        public int OrderIndex { get; set; }
    }
}