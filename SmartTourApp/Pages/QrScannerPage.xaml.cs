using System.Linq;
using SmartTourApp.Services;

namespace SmartTourApp.Pages;

public partial class QrScannerPage : ContentPage
{
    private readonly PoiRepository repo;
    private readonly NarrationEngine narration;

    private bool isProcessing = false;
    private bool isFlashOn = false;

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
        if (isProcessing) return;

        var result = e.Results?.FirstOrDefault();
        if (result == null) return;

        isProcessing = true;

        try
        {
            var value = result.Value;

            var pois = await repo.GetPois();

            var poi = pois.FirstOrDefault(x =>
                x.Id.ToString() == value);

            if (poi != null)
            {
                // Rung máy
                Vibration.Default.Vibrate();

                // Phát narration
                await narration.Play(
                    poi,
                    new Location(poi.Lat, poi.Lng));

                // Dừng scan
                cameraView.IsDetecting = false;

                await DisplayAlertAsync("QR", "Đã kích hoạt POI", "OK");
            }
            else
            {
                await DisplayAlertAsync("QR", "Không tìm thấy địa điểm", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
        finally
        {
            isProcessing = false;
        }
    }

    private void OnToggleFlash(object sender, EventArgs e)
    {
        isFlashOn = !isFlashOn;
        cameraView.IsTorchOn = isFlashOn;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var status = await Permissions.RequestAsync<Permissions.Camera>();

        if (status != PermissionStatus.Granted)
        {
            await DisplayAlertAsync("Permission", "Camera permission required", "OK");
            return;
        }

        cameraView.IsDetecting = true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        cameraView.IsDetecting = false;
    }
}