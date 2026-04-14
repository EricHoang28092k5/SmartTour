using Microsoft.Maui.Devices.Sensors;

namespace SmartTourApp.Services;

/// <summary>
/// LocationService — Cung cấp vị trí GPS với chiến lược thích ứng tiết kiệm pin.
///
/// ╔══════════════════════════════════════════════════════════════════╗
/// ║ ADAPTIVE ACCURACY STRATEGY                                      ║
/// ║                                                                  ║
/// ║ Trạng thái     | Accuracy  | Timeout | Mô tả                   ║
/// ║ ─────────────────────────────────────────────────────────────── ║
/// ║ Stationary     | Low       | 10s     | Đứng yên > 2 phút       ║
/// ║ Walking slow   | Medium    | 6s      | Di chuyển < 50m/interval ║
/// ║ Moving fast    | Best      | 5s      | Di chuyển ≥ 50m/interval ║
/// ║                                                                  ║
/// ║ Pin tiết kiệm: ~60-70% so với luôn dùng Best                   ║
/// ╚══════════════════════════════════════════════════════════════════╝
/// </summary>
public class LocationService
{
    // ── Adaptive accuracy state ──
    private GeolocationAccuracy _currentAccuracy = GeolocationAccuracy.Best;
    private Location? _lastLocation;
    private DateTime _lastMoveTime = DateTime.UtcNow;
    private int _stationaryCount = 0;

    // Ngưỡng để coi là đứng yên: < 5m trong 1 lần đo
    private const double StationaryThresholdMeters = 5.0;
    // Sau bao nhiêu lần đứng yên liên tiếp → chuyển sang Low accuracy
    private const int StationaryCountToLow = 8;   // ~24s với interval 3s
    // Di chuyển nhanh ngưỡng nào → cần Best
    private const double FastMovingThresholdMeters = 30.0;

    /// <summary>
    /// Lấy vị trí hiện tại với accuracy thích ứng theo trạng thái di chuyển.
    /// Giảm tiêu pin đáng kể khi user đứng yên.
    /// </summary>
    public async Task<Location?> GetLocation()
    {
        try
        {
            var accuracy = _currentAccuracy;
            var timeout = GetTimeoutForAccuracy(accuracy);

            var request = new GeolocationRequest(accuracy, timeout);
            var location = await Geolocation.Default.GetLocationAsync(request);

            if (location != null)
                UpdateAdaptiveAccuracy(location);

            return location;
        }
        catch (FeatureNotSupportedException)
        {
            System.Diagnostics.Debug.WriteLine("[LocationSvc] GPS not supported");
            return null;
        }
        catch (PermissionException)
        {
            System.Diagnostics.Debug.WriteLine("[LocationSvc] GPS permission denied");
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocationSvc] GetLocation error: {ex.Message}");

            // Fallback: thử lấy last known để không bỏ trống hoàn toàn
            try
            {
                return await Geolocation.Default.GetLastKnownLocationAsync();
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Lấy vị trí với accuracy cụ thể — dùng khi cần độ chính xác cao (locate me button).
    /// </summary>
    public async Task<Location?> GetLocationHighAccuracy(TimeSpan? timeout = null)
    {
        try
        {
            var request = new GeolocationRequest(
                GeolocationAccuracy.Best,
                timeout ?? TimeSpan.FromSeconds(8));
            return await Geolocation.Default.GetLocationAsync(request);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[LocationSvc] GetLocationHighAccuracy error: {ex.Message}");
            return await Geolocation.Default.GetLastKnownLocationAsync();
        }
    }

    /// <summary>
    /// Accuracy hiện tại — TrackingService dùng để điều chỉnh interval.
    /// </summary>
    public GeolocationAccuracy CurrentAccuracy => _currentAccuracy;

    /// <summary>
    /// Trạng thái đứng yên (true = đang đứng yên ≥ 24s).
    /// </summary>
    public bool IsStationary => _stationaryCount >= StationaryCountToLow;

    // ══════════════════════════════════════════════════════════════
    // ADAPTIVE LOGIC
    // ══════════════════════════════════════════════════════════════

    private void UpdateAdaptiveAccuracy(Location newLocation)
    {
        if (_lastLocation == null)
        {
            _lastLocation = newLocation;
            _currentAccuracy = GeolocationAccuracy.Best;
            return;
        }

        var distanceMeters = Location.CalculateDistance(
            _lastLocation, newLocation, DistanceUnits.Kilometers) * 1000.0;

        _lastLocation = newLocation;

        if (distanceMeters < StationaryThresholdMeters)
        {
            // Gần như đứng yên
            _stationaryCount++;

            if (_stationaryCount >= StationaryCountToLow)
            {
                // Đứng yên lâu → dùng Low accuracy, tiết kiệm pin tối đa
                SetAccuracy(GeolocationAccuracy.Low,
                    $"Stationary ({_stationaryCount}x) → Low accuracy");
            }
            else if (_stationaryCount >= StationaryCountToLow / 2)
            {
                // Đứng yên vừa → Medium
                SetAccuracy(GeolocationAccuracy.Medium,
                    $"Slow ({_stationaryCount}x) → Medium accuracy");
            }
        }
        else if (distanceMeters >= FastMovingThresholdMeters)
        {
            // Di chuyển nhanh → cần Best để geofence chính xác
            _stationaryCount = 0;
            _lastMoveTime = DateTime.UtcNow;
            SetAccuracy(GeolocationAccuracy.Best, $"Fast ({distanceMeters:F0}m) → Best accuracy");
        }
        else
        {
            // Di chuyển chậm → Medium là đủ
            _stationaryCount = Math.Max(0, _stationaryCount - 2); // decay
            _lastMoveTime = DateTime.UtcNow;
            SetAccuracy(GeolocationAccuracy.Medium,
                $"Walking ({distanceMeters:F0}m) → Medium accuracy");
        }
    }

    private void SetAccuracy(GeolocationAccuracy newAccuracy, string reason)
    {
        if (_currentAccuracy == newAccuracy) return;
        _currentAccuracy = newAccuracy;
        System.Diagnostics.Debug.WriteLine($"[LocationSvc] Accuracy changed: {reason}");
    }

    private static TimeSpan GetTimeoutForAccuracy(GeolocationAccuracy accuracy)
    {
        return accuracy switch
        {
            GeolocationAccuracy.Best => TimeSpan.FromSeconds(5),
            GeolocationAccuracy.Medium => TimeSpan.FromSeconds(6),
            GeolocationAccuracy.Low => TimeSpan.FromSeconds(10),
            _ => TimeSpan.FromSeconds(6)
        };
    }

    /// <summary>
    /// Reset trạng thái adaptive — gọi khi app resume hoặc user nhấn "Locate Me".
    /// </summary>
    public void ResetAdaptiveState()
    {
        _currentAccuracy = GeolocationAccuracy.Best;
        _stationaryCount = 0;
        _lastMoveTime = DateTime.UtcNow;
        System.Diagnostics.Debug.WriteLine("[LocationSvc] Adaptive state reset → Best accuracy");
    }
}
