namespace SmartTour.Shared.Models
{
    public class QrCode
    {
        public int Id { get; set; }
        public int PoiId { get; set; }
        public string QrToken { get; set; } = string.Empty;
        public string? LocationName { get; set; }
    }
}
