namespace SmartTour.Shared.Models;

/// <summary>
/// Nguồn kích hoạt ghi nhận lượt ghé POI (analytics).
/// </summary>
public enum VisitType
{
    Geofence = 0,
    MapClick = 1,
    QRCode = 2
}
