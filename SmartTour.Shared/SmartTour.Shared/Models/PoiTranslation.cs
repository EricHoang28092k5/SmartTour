namespace SmartTour.Shared.Models
{
    public class PoiTranslation
    {
        public int Id { get; set; }
        public int PoiId { get; set; }
        public Poi? Poi { get; set; } // Kết nối tới POI

        public int LanguageId { get; set; }
        public Language? Language { get; set; } // Kết nối tới Ngôn ngữ

        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string TtsScript { get; set; } = string.Empty;
    }
}