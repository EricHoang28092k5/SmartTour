namespace SmartTour.Shared.Models
{
    public class PoiTranslation
    {
        public int Id { get; set; }
        public int PoiId { get; set; }
        public int LanguageId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string TtsScript { get; set; } = string.Empty;
    }
}
