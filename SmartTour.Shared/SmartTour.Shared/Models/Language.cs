namespace SmartTour.Shared.Models
{
    public class Language
    {
        public int Id { get; set; }
        public string Code { get; set; } = "vi"; // vi, en, ja...
        public string Name { get; set; } = string.Empty;
    }
}
