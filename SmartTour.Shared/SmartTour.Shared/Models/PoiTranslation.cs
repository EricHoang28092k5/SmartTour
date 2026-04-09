using SQLite;

namespace SmartTour.Shared.Models
{
    public class PoiTranslation
    {
        [PrimaryKey]
        public int Id { get; set; }
        public int PoiId { get; set; }
        public int LanguageId { get; set; }
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string? TtsScript { get; set; }

        // --- THÊM DUY NHẤT DÒNG NÀY ĐỂ LƯU LINK AUDIO ---
        public string? AudioUrl { get; set; }

        [Ignore]
        public virtual Poi? Poi { get; set; }
        [Ignore]
        public virtual Language? Language { get; set; }
    }
}