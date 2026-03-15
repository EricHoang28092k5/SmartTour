namespace SmartTour.Shared.Models
{
    public class PlayLog
    {
        public int Id { get; set; }

        public int PoiId { get; set; }

        // Gán mặc định là chuỗi rỗng để tránh lỗi Null sau này
        public string DeviceId { get; set; } = string.Empty;

        public DateTime Time { get; set; }

        public double Lat { get; set; }

        public double Lng { get; set; }

        public int DurationListened { get; set; }
    }
} // Cần dấu ngoặc này để đóng Namespace