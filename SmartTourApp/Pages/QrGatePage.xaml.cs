using SmartTourApp.Services;
using ZXing.Net.Maui;
using System.Threading;

namespace SmartTourApp.Pages;

public partial class QrGatePage : ContentPage
{
    private const string QrGateUntilKey = "qr_gate_until_utc";
    private int _processing;

    // YC6: Pre-warm the shell navigation to avoid cold-start delay
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

        // YC6: Eagerly warm up AppShell in background so it's ready when QR is scanned
        _ = Task.Run(() =>
        {
            try
            {
                // Pre-resolve services so DI is warm
                var services = Application.Current?.Handler?.MauiContext?.Services;
                _ = services?.GetService<PoiRepository>();
                _ = services?.GetService<TrackingService>();
            }
            catch { }
        });
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
        // YC6: Guard against multiple rapid callbacks
        if (Interlocked.CompareExchange(ref _processing, 1, 0) != 0) return;
        if (_navigationStarted) return;

        var raw = e.Results?.FirstOrDefault()?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            Interlocked.Exchange(ref _processing, 0);
            return;
        }

        // YC6: Stop detecting immediately to prevent further callbacks
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

        // YC6: Mark navigation started to block any further callbacks
        _navigationStarted = true;

        // YC6: Save session and navigate as fast as possible on main thread
        // No unnecessary awaits or delays
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                // Save valid session immediately
                Preferences.Default.Set(QrGateUntilKey, DateTime.UtcNow.AddDays(7).ToString("O"));

                // YC6: Brief visual feedback (minimal delay)
                StatusLabel.Text = "✅ Quét thành công!";

                // YC6: Switch to AppShell directly — no extra Task.Delay
                Application.Current!.MainPage = new AppShell();

                // Navigate to home after shell is ready
                await Shell.Current.GoToAsync("//home");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[QrGate] Navigation error: {ex.Message}");
                // Reset state on error
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

        // 1) Deep link chuẩn
        if (Uri.TryCreate(text, UriKind.Absolute, out var direct) &&
            string.Equals(direct.Scheme, "smarttour", StringComparison.OrdinalIgnoreCase))
            return direct;

        // 2) Dạng path rút gọn: poi/52 hoặc tour/4
        if (text.StartsWith("poi/", StringComparison.OrdinalIgnoreCase))
            return Uri.TryCreate($"smarttour://{text}", UriKind.Absolute, out var p) ? p : null;
        if (text.StartsWith("tour/", StringComparison.OrdinalIgnoreCase))
            return Uri.TryCreate($"smarttour://{text}", UriKind.Absolute, out var t) ? t : null;

        // 3) URL trung gian dạng /Qr/Open?type=poi&id=52
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

        // 1) Chuẩn deep link chính thức
        if (text.StartsWith("smarttour://poi/", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.StartsWith("smarttour://tour/", StringComparison.OrdinalIgnoreCase)) return true;

        // 2) Một số scanner trả plain path
        if (text.StartsWith("poi/", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.StartsWith("tour/", StringComparison.OrdinalIgnoreCase)) return true;

        // 3) QR URL trung gian/fallback (kể cả chữ hoa/thường khác nhau)
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            var path = (uri.AbsolutePath ?? string.Empty).ToLowerInvariant();
            var query = (uri.Query ?? string.Empty).ToLowerInvariant();

            if (path.Contains("/qr/open")) return true;
            if (path.Contains("/poi/")) return true;
            if (path.Contains("/tour/")) return true;
            if (query.Contains("type=poi") || query.Contains("type=tour")) return true;
        }

        // 4) Fallback mềm
        if (text.Contains("smarttour", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
