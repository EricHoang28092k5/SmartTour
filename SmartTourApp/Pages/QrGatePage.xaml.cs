using SmartTour.Services;
using SmartTourApp.Services;
using ZXing.Net.Maui;
using System.Threading;

namespace SmartTourApp.Pages;

public partial class QrGatePage : ContentPage
{
    private const string QrGateUntilKey = "qr_gate_until_utc";
    private int _processing;
    private bool _navigationStarted = false;

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

        // Pre-warm DI services in background
        _ = Task.Run(() =>
        {
            try
            {
                var services = Application.Current?.Handler?.MauiContext?.Services;
                _ = services?.GetService<PoiRepository>();
                _ = services?.GetService<TrackingService>();
            }
            catch { }
        });
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Reset trạng thái khi rời trang để có thể quét lại nếu quay lại
        _navigationStarted = false;
        Interlocked.Exchange(ref _processing, 0);
    }

    private async Task<bool> EnsureCameraPermissionAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (status == PermissionStatus.Granted) return true;

        status = await Permissions.RequestAsync<Permissions.Camera>();
        return status == PermissionStatus.Granted;
    }

    private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        if (Interlocked.CompareExchange(ref _processing, 1, 0) != 0) return;
        if (_navigationStarted) return;

        var raw = e.Results?.FirstOrDefault()?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            Interlocked.Exchange(ref _processing, 0);
            return;
        }

        // Stop detecting immediately
        MainThread.BeginInvokeOnMainThread(() =>
        {
            QrReader.IsDetecting = false;
            QrReader.IsEnabled = false;
        });

        if (!IsValidGateQr(raw))
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                StatusLabel.Text = "QR không hợp lệ. Vui lòng quét lại mã SmartTour.";
                await Task.Delay(600);
                QrReader.IsEnabled = true;
                QrReader.IsDetecting = true;
                Interlocked.Exchange(ref _processing, 0);
            });
            return;
        }

        var targetUri = NormalizeQr(raw);
        if (targetUri == null)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                StatusLabel.Text = "Không đọc được định dạng QR.";
                await Task.Delay(600);
                QrReader.IsEnabled = true;
                QrReader.IsDetecting = true;
                Interlocked.Exchange(ref _processing, 0);
            });
            return;
        }

        _navigationStarted = true;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                // YC1: Lưu phiên QR 7 ngày
                Preferences.Default.Set(QrGateUntilKey,
                    DateTime.UtcNow.AddDays(7).ToString("O"));

                StatusLabel.Text = "✅ Quét thành công!";

                // YC1: Lần đầu vào → LoadingPage → AppShell (home)
                // Lấy services để tạo LoadingPage
                var services = Application.Current?.Handler?.MauiContext?.Services;
                var poiRepo = services?.GetService<PoiRepository>();
                var tracking = services?.GetService<TrackingService>();

                if (poiRepo != null && tracking != null)
                {
                    // LoadingPage sẽ tự chuyển sang AppShell sau khi init xong
                    Application.Current!.MainPage = new LoadingPage(poiRepo, tracking);
                }
                else
                {
                    // Fallback nếu DI chưa sẵn sàng
                    Application.Current!.MainPage = new AppShell();
                    await Shell.Current.GoToAsync("//home");
                    var api = services?.GetService<ApiService>();
                    if (api != null)
                    {
                        try { await api.PostPresenceHeartbeatAsync(); } catch { }
                    }
                    if (Application.Current is App app)
                        app.StartPresenceHeartbeatTimer();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[QrGate] Navigation error: {ex.Message}");
                _navigationStarted = false;
                QrReader.IsEnabled = true;
                QrReader.IsDetecting = true;
            }
            finally
            {
                Interlocked.Exchange(ref _processing, 0);
            }
        });
    }

    private static Uri? NormalizeQr(string raw)
    {
        var text = raw.Trim();

        if (Uri.TryCreate(text, UriKind.Absolute, out var direct) &&
            string.Equals(direct.Scheme, "smarttour", StringComparison.OrdinalIgnoreCase))
            return direct;

        if (text.StartsWith("poi/", StringComparison.OrdinalIgnoreCase))
            return Uri.TryCreate($"smarttour://{text}", UriKind.Absolute, out var p) ? p : null;
        if (text.StartsWith("tour/", StringComparison.OrdinalIgnoreCase))
            return Uri.TryCreate($"smarttour://{text}", UriKind.Absolute, out var t) ? t : null;

        if (Uri.TryCreate(text, UriKind.Absolute, out var web))
        {
            var path = web.AbsolutePath?.ToLowerInvariant() ?? string.Empty;
            if (path.Contains("/qr/open"))
            {
                var qs = ParseQuery(web.Query);
                if (qs.TryGetValue("type", out var type) &&
                    qs.TryGetValue("id", out var id) &&
                    int.TryParse(id, out var parsedId) &&
                    parsedId > 0 &&
                    (string.Equals(type, "poi", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(type, "tour", StringComparison.OrdinalIgnoreCase)))
                {
                    var deep = $"smarttour://{type.ToLowerInvariant()}/{parsedId}";
                    return Uri.TryCreate(deep, UriKind.Absolute, out var u) ? u : null;
                }
            }
        }

        return null;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query)) return dict;

        var q = query.TrimStart('?');
        foreach (var pair in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            dict[key] = value;
        }
        return dict;
    }

    private static bool IsValidGateQr(string value)
    {
        var text = value.Trim();
        if (string.IsNullOrWhiteSpace(text)) return false;

        if (text.StartsWith("smarttour://poi/", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.StartsWith("smarttour://tour/", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.StartsWith("poi/", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.StartsWith("tour/", StringComparison.OrdinalIgnoreCase)) return true;

        if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            var path = (uri.AbsolutePath ?? string.Empty).ToLowerInvariant();
            var query = (uri.Query ?? string.Empty).ToLowerInvariant();

            if (path.Contains("/qr/open")) return true;
            if (path.Contains("/poi/")) return true;
            if (path.Contains("/tour/")) return true;
            if (query.Contains("type=poi") || query.Contains("type=tour")) return true;
        }

        if (text.Contains("smarttour", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
