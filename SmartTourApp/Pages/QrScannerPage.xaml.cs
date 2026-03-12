using System.Linq;
using SmartTourApp.Services;

namespace SmartTourApp.Pages;

public partial class QrScannerPage : ContentPage
{
    private readonly PoiRepository repo;
    private readonly NarrationEngine narration;

    public QrScannerPage(
        PoiRepository repo,
        NarrationEngine narration)
    {
        InitializeComponent();

        this.repo = repo;
        this.narration = narration;
    }

    private async void OnDetected(
        object sender,
        ZXing.Net.Maui.BarcodeDetectionEventArgs e)
    {
        var value = e.Results.First().Value;

        var poi =
            repo.GetPois()
            .FirstOrDefault(x =>
                x.Id.ToString() == value);

        if (poi != null)
        {
            await narration.Play(
                poi,
                new Location(poi.Lat, poi.Lng));
        }

        await DisplayAlertAsync("QR", "Đã kích hoạt POI", "OK");
    }
}