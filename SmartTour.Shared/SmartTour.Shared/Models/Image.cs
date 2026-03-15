namespace SmartTour.Shared.Models
{
    public class Image
    {
        public int Id { get; set; }
        public int PoiId { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public string? Caption { get; set; }
    }
}
