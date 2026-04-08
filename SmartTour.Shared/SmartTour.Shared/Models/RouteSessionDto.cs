namespace SmartTour.Shared.Models
{
    /// <summary>
    /// DTO gửi từ client lên API khi flush một route session.
    /// Nằm trong Shared để cả SmartTourApp và SmartTourBackend cùng dùng.
    /// </summary>
    public class RouteSessionDto
    {
        public string DeviceId { get; set; } = "";
        public string PoiSequence { get; set; } = "";
        public int StopCount { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime EndedAt { get; set; }
        public int DurationMinutes { get; set; }
        public string Status { get; set; } = "completed";
        public List<RouteStopDto> Stops { get; set; } = new();
    }

    public class RouteStopDto
    {
        public int PoiId { get; set; }
        public int OrderIndex { get; set; }
        public string TriggerType { get; set; } = "";
        public DateTime TriggeredAt { get; set; }
        public int DwellSeconds { get; set; }
    }
}
