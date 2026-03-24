namespace SmartTour.Shared.Models
{
    public class AudioFile
    {
        public int Id { get; set; }
        public int PoiId { get; set; }
        public int LanguageId { get; set; }
        public string FileUrl { get; set; } = string.Empty;
        public int Duration { get; set; }
        public string AudioType { get; set; } = "Narration";
        public string? VendorId { get; set; }
    }
}
