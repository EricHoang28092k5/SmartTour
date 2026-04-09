using System.Globalization;

namespace SmartTourApp.Services;

public static class DeepLinkService
{
    private static readonly object _lock = new();
    private static Uri? _pendingUri;

    public static event Action<Uri>? DeepLinkReceived;

    public static void Publish(string? uriText)
    {
        if (string.IsNullOrWhiteSpace(uriText)) return;
        if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri)) return;

        lock (_lock)
        {
            _pendingUri = uri;
        }

        DeepLinkReceived?.Invoke(uri);
    }

    public static Uri? ConsumePending()
    {
        lock (_lock)
        {
            var tmp = _pendingUri;
            _pendingUri = null;
            return tmp;
        }
    }

    public static async Task<bool> NavigateAsync(Uri uri)
    {
        if (uri == null) return false;
        if (!string.Equals(uri.Scheme, "smarttour", StringComparison.OrdinalIgnoreCase))
            return false;

        var host = (uri.Host ?? string.Empty).Trim().ToLowerInvariant();
        var path = (uri.AbsolutePath ?? string.Empty).Trim('/');

        if (string.IsNullOrWhiteSpace(host)) return false;

        if (host == "poi")
        {
            var poiId = path;
            if (int.TryParse(poiId, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await Shell.Current.GoToAsync($"//map?targetPoi={Uri.EscapeDataString(poiId)}");
                });
                return true;
            }
        }

        if (host == "tour")
        {
            var tourId = path;
            if (int.TryParse(tourId, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await Shell.Current.GoToAsync($"//tour?targetTour={Uri.EscapeDataString(tourId)}");
                });
                return true;
            }
        }

        return false;
    }
}

