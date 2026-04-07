using SmartTour.Services;
using SmartTour.Shared.Models;

namespace SmartTourApp.Services
{
    /// <summary>
    /// HeatmapService — ghi nhận 2 trường hợp duy nhất:
    ///
    ///   1. APP_OPEN  : Lần đầu mở app, nếu user đang đứng trong radius của 1 POI → ghi nhận ngay.
    ///   2. ZONE_ENTER: User bước từ ngoài vào radius của 1 POI (edge trigger, không phải level trigger).
    ///
    /// Chống spam:
    ///   - Client-side : mỗi POI chỉ trigger lại sau LOCAL_COOLDOWN_MINUTES phút (tránh gọi API thừa).
    ///   - Server-side : backend có thêm 1 lớp delay DELAY_MINUTES nữa.
    ///   - User đi vòng vòng trong radius → KHÔNG ghi nhận thêm (state machine).
    ///   - User bước ra rồi bước vào lại → ghi nhận, nhưng phải qua cooldown.
    /// </summary>
    public class HeatmapService
    {
        private readonly ApiService _api;

        // ─── State: set các poiId hiện tại đang trong radius ───
        // Dùng để detect EDGE (bước vào) thay vì LEVEL (đang ở trong)
        private readonly HashSet<int> _currentZones = new();

        // ─── Client-side cooldown: poiId → thời điểm trigger gần nhất ───
        private readonly Dictionary<int, DateTime> _lastTriggered = new();

        // Client chỉ gửi API tối đa 1 lần / poi / N phút
        // (Backend còn thêm 1 lớp nữa ở server)
        private const int LOCAL_COOLDOWN_MINUTES = 5;

        // ─── App-open flag: chỉ chạy logic app_open 1 lần duy nhất ───
        private bool _appOpenChecked = false;

        // ─── DeviceId (anonymous) ───
        private readonly string _deviceId;

        public HeatmapService(ApiService api)
        {
            _api = api;

            // Tạo/lấy anonymous device id, lưu vào Preferences để ổn định qua session
            _deviceId = GetOrCreateDeviceId();
        }

        // ══════════════════════════════════════════════════════════════════
        // Gọi 1 LẦN DUY NHẤT khi app mở (từ LoadingPage.InitAsync)
        // Nếu user đang đứng trong radius → ghi nhận "app_open"
        // ══════════════════════════════════════════════════════════════════
        public async Task CheckAppOpenAsync(Location userLocation, List<Poi> pois)
        {
            if (_appOpenChecked) return;
            _appOpenChecked = true;

            foreach (var poi in pois)
            {
                var meters = DistanceMeters(userLocation, poi);
                if (meters <= poi.Radius)
                {
                    // Đánh dấu đang trong zone (để zone_enter sau này hoạt động đúng)
                    _currentZones.Add(poi.Id);

                    // Gửi app_open heatmap (có cooldown để tránh spam khi restart app liên tục)
                    await TrySendAsync(poi, userLocation, "app_open");
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // Gọi mỗi khi TrackingService cập nhật vị trí mới
        // Phát hiện EDGE: bước vào radius từ bên ngoài → ghi nhận "zone_enter"
        // ══════════════════════════════════════════════════════════════════
        public async Task OnLocationUpdatedAsync(Location userLocation, List<Poi> pois)
        {
            // Xác định set POI đang trong radius ở vị trí hiện tại
            var nowInZone = new HashSet<int>();

            foreach (var poi in pois)
            {
                var meters = DistanceMeters(userLocation, poi);
                if (meters <= poi.Radius)
                    nowInZone.Add(poi.Id);
            }

            // ─── Detect ENTER: có trong nowInZone nhưng KHÔNG có trong _currentZones ───
            // (tức là vừa bước vào từ bên ngoài)
            var entered = nowInZone.Except(_currentZones).ToList();

            foreach (var poiId in entered)
            {
                var poi = pois.FirstOrDefault(p => p.Id == poiId);
                if (poi == null) continue;

                await TrySendAsync(poi, userLocation, "zone_enter");
            }

            // Cập nhật state (không xoá — chỉ add/remove theo vị trí thực)
            _currentZones.Clear();
            foreach (var id in nowInZone)
                _currentZones.Add(id);
        }

        // ══════════════════════════════════════════════════════════════════
        // Reset khi app reload (SettingsPage.Save → language change)
        // ══════════════════════════════════════════════════════════════════
        public void Reset()
        {
            _currentZones.Clear();
            _lastTriggered.Clear();
            _appOpenChecked = false;
        }

        // ─────────────────────────────────────────────────────────────────
        // Internal: kiểm tra cooldown rồi gửi API
        // ─────────────────────────────────────────────────────────────────
        private async Task TrySendAsync(Poi poi, Location loc, string triggerType)
        {
            // Client-side cooldown
            if (_lastTriggered.TryGetValue(poi.Id, out var last))
            {
                if ((DateTime.Now - last).TotalMinutes < LOCAL_COOLDOWN_MINUTES)
                    return;
            }

            _lastTriggered[poi.Id] = DateTime.Now;

            // Fire-and-forget (không block tracking loop)
            _ = Task.Run(async () =>
            {
                try
                {
                    await _api.PostHeatmapEntry(new HeatmapEntryDto
                    {
                        PoiId = poi.Id,
                        DeviceId = _deviceId,
                        TriggerType = triggerType,
                        Lat = loc.Latitude,
                        Lng = loc.Longitude
                    });

                    System.Diagnostics.Debug.WriteLine(
                        $"🔥 HEATMAP [{triggerType.ToUpper()}] POI={poi.Id} ({poi.Name})");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Heatmap send error: {ex.Message}");
                }
            });
        }

        // ─────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────
        private static double DistanceMeters(Location from, Poi to)
            => Location.CalculateDistance(from, new Location(to.Lat, to.Lng),
                DistanceUnits.Kilometers) * 1000.0;

        private static string GetOrCreateDeviceId()
        {
            const string key = "heatmap_device_id";
            var existing = Preferences.Default.Get(key, string.Empty);
            if (!string.IsNullOrEmpty(existing)) return existing;

            var newId = Guid.NewGuid().ToString("N");
            Preferences.Default.Set(key, newId);
            return newId;
        }
    }

    // ─── DTO gửi lên API (mirror của server-side HeatmapEntryDto) ───
    public class HeatmapEntryDto
    {
        public int PoiId { get; set; }
        public string? DeviceId { get; set; }
        public string? TriggerType { get; set; }
        public double Lat { get; set; }
        public double Lng { get; set; }
    }
}
