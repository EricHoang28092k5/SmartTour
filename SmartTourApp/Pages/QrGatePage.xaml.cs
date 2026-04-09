using SmartTourApp.Services;
using ZXing.Net.Maui;

namespace SmartTourApp.Pages;

public partial class QrGatePage : ContentPage
{
    private bool _unlocked;

    public QrGatePage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var hasCamera = await EnsureCameraPermissionAsync();
        if (!hasCamera)
        {
            StatusLabel.Text = "Chưa cấp quyền camera. Vui lòng cấp quyền rồi mở lại app.";
            QrReader.IsDetecting = false;
            return;
        }

        QrReader.IsDetecting = true;
    }

    private async Task<bool> EnsureCameraPermissionAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (status == PermissionStatus.Granted) return true;

        status = await Permissions.RequestAsync<Permissions.Camera>();
        return status == PermissionStatus.Granted;
    }

    private async void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        if (_unlocked) return;
        var raw = e.Results?.FirstOrDefault()?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return;

        if (!IsValidGateQr(raw))
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                StatusLabel.Text = "QR không hợp lệ. Hãy quét QR SmartTour.";
                await this.DisplayAlert("QR không hợp lệ", "Vui lòng quét mã QR của SmartTour.", "OK");
            });
            return;
        }

        _unlocked = true;
        QrReader.IsDetecting = false;

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            StatusLabel.Text = "Quét thành công, đang mở ứng dụng...";
            Application.Current!.MainPage = new AppShell();

            // Nếu QR chứa deep link smarttour://... thì điều hướng luôn.
            if (Uri.TryCreate(raw, UriKind.Absolute, out var uri) &&
                string.Equals(uri.Scheme, "smarttour", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(250);
                await DeepLinkService.NavigateAsync(uri);
            }
        });
    }

    private static bool IsValidGateQr(string value)
    {
        var text = value.Trim();
        if (text.StartsWith("smarttour://", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains("/Qr/Open?", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains("smarttour", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}

