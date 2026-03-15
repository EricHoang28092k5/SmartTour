namespace SmartTour.Shared.Models
{
    public class UserLocationLog
    {
        public long Id { get; set; }
        public int UserId { get; set; }
        public decimal Latitude { get; set; }
        public decimal Longitude { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
