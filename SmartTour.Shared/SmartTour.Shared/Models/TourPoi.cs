namespace SmartTour.Shared.Models
{
    public class TourPoi
    {
        public int Id { get; set; }
        public int TourId { get; set; }
        public int PoiId { get; set; }
        public int OrderIndex { get; set; }
    }
}
