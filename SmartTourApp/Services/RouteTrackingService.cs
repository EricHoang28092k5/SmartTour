using SmartTour.Services;
using SmartTour.Shared.Models;
using System.Text.Json;

namespace SmartTourApp.Services
{
    /// <summary>
    /// RouteTrackingService — State machine ghi nhận tuyến đi thực tế của user.
    ///
    /// ╔══════════════════════════════════════════════════════════════════╗
    /// ║  ĐIỀU KIỆN GHI NHẬN MỘT "ĐIỂM DỪNG" (POI Entry)               ║
    /// ║  1. DWELL: Ở trong radius liên tục > 5 phút.                   ║
    /// ║  2. AUDIO_MANUAL: Bấm nghe audio khi đang trong radius.        ║
    /// ║     - KHÔNG tính auto-play, KHÔNG tính nghe từ ngoài radius.   ║
    /// ╚══════════════════════════════════════════════════════════════════╝
    ///
    /// QUY TẮC CHUỖI:
    ///   A→A   : chỉ ghi 1 lần (deduplicate liên tiếp).
    ///   A→B→A : ghi đủ 3 lần (quay lại là hành vi có giá trị).
    ///
    /// TIMEOUT:
    ///   Moving  = 30 phút — sẵn sàng nối dài tuyến cũ.
    ///   Session = 90 phút — đóng tuyến nếu không có POI mới.
    ///
    /// KHÔI PHỤC KHI CRASH:
    ///   Trạng thái được persist vào Preferences dưới dạng JSON.
    ///   Khi App mở lại, kiểm tra session cũ: nếu đã quá 90 phút → flush → xóa.
    /// </summary>
    public class RouteTrackingService
    {
        // ══════════════════════════════════════════════════════════════
        // DEPENDENCIES
        // ══════════════════════════════════════════════════════════════

        private readonly ApiService _api;
        private readonly string _deviceId;

        // ══════════════════════════════════════════════════════════════
        // CONSTANTS
        // ══════════════════════════════════════════════════════════════

        private const int DwellThresholdSeconds = 300;   // 5 phút
        private const int MovingTimeoutMinutes = 30;
        private const int SessionTimeoutMinutes = 90;
        private const int MinPoisToSave = 2;

        // ══════════════════════════════════════════════════════════════
        // ACTIVE SESSION STATE
        // ══════════════════════════════════════════════════════════════

        /// <summary>Chuỗi POI đã được xác nhận (theo thứ tự thời gian).</summary>
        private readonly List<ConfirmedStop> _stops = new();

        /// <summary>Thời điểm POI đầu tiên được xác nhận (khởi tạo session).</summary>
        private DateTime? _sessionStartedAt;

        /// <summary>Thời điểm POI cuối cùng được xác nhận.</summary>
        private DateTime _lastStopAt = DateTime.MinValue;

        // ── Dwell tracking ──
        /// <summary>Thời điểm user bước vào radius của POI hiện tại.</summary>
        private readonly Dictionary<int, DateTime> _dwellEnterTime = new();

        /// <summary>Set các poiId mà dwell đã được confirm trong session này (chống A→A liên tiếp).</summary>
        private int? _lastConfirmedPoiId = null;

        // ── Persistence key ──
        private const string PersistKey = "route_session_snapshot";

        // ══════════════════════════════════════════════════════════════
        // CONSTRUCTOR
        // ══════════════════════════════════════════════════════════════

        public RouteTrackingService(ApiService api)
        {
            _api = api;
            _deviceId = GetOrCreateDeviceId();
        }

        // ══════════════════════════════════════════════════════════════
        // PUBLIC API — gọi từ TrackingService & PoiDetailAudioManager
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Gọi mỗi khi TrackingService cập nhật vị trí mới.
        /// Xử lý logic dwell timer và session timeout.
        /// </summary>
        public async Task OnLocationUpdatedAsync(Location userLocation, List<Poi> pois)
        {
            var now = DateTime.UtcNow;

            // ── 1. Kiểm tra session timeout ──
            if (_sessionStartedAt.HasValue &&
                (now - _lastStopAt).TotalMinutes >= SessionTimeoutMinutes)
            {
                await FlushSessionAsync("expired");
                return;
            }

            // ── 2. Tính tập POI đang trong radius ──
            var inRadius = new HashSet<int>();
            foreach (var poi in pois)
            {
                if (IsInRadius(userLocation, poi))
                    inRadius.Add(poi.Id);
            }

            // ── 3. Cập nhật dwell timers ──
            foreach (var poi in pois)
            {
                if (inRadius.Contains(poi.Id))
                {
                    // Bước vào → bắt đầu đếm dwell nếu chưa có
                    if (!_dwellEnterTime.ContainsKey(poi.Id))
                        _dwellEnterTime[poi.Id] = now;

                    // Kiểm tra đã đủ 5 phút chưa
                    var dwellSec = (now - _dwellEnterTime[poi.Id]).TotalSeconds;
                    if (dwellSec >= DwellThresholdSeconds)
                    {
                        await TryConfirmStopAsync(poi, "dwell", (int)dwellSec, now);
                    }
                }
                else
                {
                    // Ra khỏi radius → reset dwell timer
                    _dwellEnterTime.Remove(poi.Id);
                }
            }

            // ── 4. Dọn dẹp POI không còn tồn tại trong danh sách ──
            var validIds = pois.Select(p => p.Id).ToHashSet();
            foreach (var key in _dwellEnterTime.Keys.Where(k => !validIds.Contains(k)).ToList())
                _dwellEnterTime.Remove(key);

            SaveSnapshot();
        }

        /// <summary>
        /// Gọi từ PoiDetailAudioManager khi user BẤM PLAY thủ công (không phải auto-play).
        /// Kiểm tra user có đang trong radius của poi đó không trước khi ghi nhận.
        /// </summary>
        /// <param name="poi">POI đang phát audio.</param>
        /// <param name="userLocation">Vị trí hiện tại của user.</param>
        public async Task OnManualAudioPlayedAsync(Poi poi, Location userLocation)
        {
            // Guard: chỉ ghi nhận nếu đang trong radius
            if (!IsInRadius(userLocation, poi))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[RouteTracking] SKIP audio trigger — outside radius: POI={poi.Id}");
                return;
            }

            var dwellSec = _dwellEnterTime.TryGetValue(poi.Id, out var enterTime)
                ? (int)(DateTime.UtcNow - enterTime).TotalSeconds
                : 0;

            await TryConfirmStopAsync(poi, "audio_manual", dwellSec, DateTime.UtcNow);
            SaveSnapshot();
        }

        /// <summary>
        /// Gọi khi App tắt / language reset → flush session đang dang dở lên API.
        /// </summary>
        public async Task FlushOnAppClosingAsync()
        {
            if (_stops.Count >= MinPoisToSave)
                await FlushSessionAsync("completed");
            else
                ClearSession();
        }

        /// <summary>
        /// Gọi ngay khi App khởi động (trước khi tracking bắt đầu).
        /// Kiểm tra snapshot cũ → nếu đã quá 90 phút → flush → xóa.
        /// </summary>
        public async Task RecoverSessionOnStartupAsync()
        {
            var snapshot = LoadSnapshot();
            if (snapshot == null) return;

            var elapsed = (DateTime.UtcNow - snapshot.LastStopAt).TotalMinutes;

            if (elapsed >= SessionTimeoutMinutes)
            {
                // Khôi phục vào bộ nhớ rồi flush
                RestoreFromSnapshot(snapshot);
                await FlushSessionAsync("expired");
            }
            else
            {
                // Session còn sống → khôi phục để tiếp tục
                RestoreFromSnapshot(snapshot);
                System.Diagnostics.Debug.WriteLine(
                    $"[RouteTracking] Session recovered: {_stops.Count} stops, " +
                    $"elapsed={elapsed:F1} min");
            }
        }

        /// <summary>Reset toàn bộ state (khi đổi ngôn ngữ / reload app).</summary>
        public void Reset()
        {
            ClearSession();
            Preferences.Default.Remove(PersistKey);
        }

        // ══════════════════════════════════════════════════════════════
        // CORE STATE MACHINE
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Cố gắng xác nhận một điểm dừng mới vào chuỗi.
        /// Áp dụng quy tắc A→A (deduplicate liên tiếp) và A→B→A (cho phép).
        /// </summary>
        private async Task TryConfirmStopAsync(Poi poi, string triggerType, int dwellSec, DateTime now)
        {
            // ── Quy tắc A→A: không ghi lại nếu poi cuối cùng cũng là poi này ──
            // (nhưng A→B→A vẫn được: _lastConfirmedPoiId sẽ là B khi đó)
            if (_lastConfirmedPoiId == poi.Id)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[RouteTracking] SKIP A→A duplicate: POI={poi.Id}");
                return;
            }

            // ── Kiểm tra moving timeout ──
            // Nếu đã có stops trước đó, và thời gian từ stop cuối > 30 phút
            // thì flush session cũ rồi bắt đầu session mới
            if (_stops.Count > 0 &&
                (now - _lastStopAt).TotalMinutes > MovingTimeoutMinutes)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[RouteTracking] Moving timeout exceeded → flush & new session");
                await FlushSessionAsync("completed");
                // Sau flush, tiếp tục ghi nhận POI này vào session mới
            }

            // ── Khởi tạo session nếu chưa có ──
            if (_sessionStartedAt == null)
                _sessionStartedAt = now;

            // ── Thêm stop ──
            var stop = new ConfirmedStop
            {
                PoiId = poi.Id,
                PoiName = poi.Name,
                OrderIndex = _stops.Count + 1,
                TriggerType = triggerType,
                TriggeredAt = now,
                DwellSeconds = dwellSec
            };

            _stops.Add(stop);
            _lastStopAt = now;
            _lastConfirmedPoiId = poi.Id;

            System.Diagnostics.Debug.WriteLine(
                $"[RouteTracking] ✅ STOP #{stop.OrderIndex} confirmed: " +
                $"POI={poi.Id} ({poi.Name}) via [{triggerType}] dwell={dwellSec}s");
        }

        // ══════════════════════════════════════════════════════════════
        // FLUSH SESSION TO API
        // ══════════════════════════════════════════════════════════════

        private async Task FlushSessionAsync(string status)
        {
            if (_stops.Count < MinPoisToSave)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[RouteTracking] Flush skipped: only {_stops.Count} stop(s) — min={MinPoisToSave}");
                ClearSession();
                return;
            }

            var session = BuildSessionDto(status);

            // Fire-and-forget với retry nhẹ
            _ = Task.Run(async () =>
            {
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        await _api.PostRouteSession(session);
                        System.Diagnostics.Debug.WriteLine(
                            $"[RouteTracking] ✅ Session flushed: " +
                            $"{_stops.Count} stops, seq={session.PoiSequence}");
                        break;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[RouteTracking] Attempt {attempt} failed: {ex.Message}");
                        if (attempt < 3) await Task.Delay(2000 * attempt);
                    }
                }
            });

            ClearSession();
        }

        private RouteSessionDto BuildSessionDto(string status)
        {
            var now = DateTime.UtcNow;
            var startedAt = _sessionStartedAt ?? now;
            var duration = (int)(now - startedAt).TotalMinutes;

            return new RouteSessionDto
            {
                DeviceId = _deviceId,
                PoiSequence = string.Join(",", _stops.Select(s => s.PoiId)),
                StopCount = _stops.Count,
                StartedAt = startedAt,
                EndedAt = now,
                DurationMinutes = duration,
                Status = status,
                Stops = _stops.Select(s => new RouteStopDto
                {
                    PoiId = s.PoiId,
                    OrderIndex = s.OrderIndex,
                    TriggerType = s.TriggerType,
                    TriggeredAt = s.TriggeredAt,
                    DwellSeconds = s.DwellSeconds
                }).ToList()
            };
        }

        // ══════════════════════════════════════════════════════════════
        // SNAPSHOT PERSISTENCE (crash recovery)
        // ══════════════════════════════════════════════════════════════

        private void SaveSnapshot()
        {
            if (_stops.Count == 0) return;

            try
            {
                var snapshot = new SessionSnapshot
                {
                    Stops = new List<ConfirmedStop>(_stops),
                    SessionStartedAt = _sessionStartedAt ?? DateTime.UtcNow,
                    LastStopAt = _lastStopAt,
                    LastConfirmedPoiId = _lastConfirmedPoiId
                };

                var json = JsonSerializer.Serialize(snapshot);
                Preferences.Default.Set(PersistKey, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RouteTracking] Snapshot save error: {ex.Message}");
            }
        }

        private SessionSnapshot? LoadSnapshot()
        {
            try
            {
                var json = Preferences.Default.Get(PersistKey, string.Empty);
                if (string.IsNullOrEmpty(json)) return null;
                return JsonSerializer.Deserialize<SessionSnapshot>(json);
            }
            catch
            {
                return null;
            }
        }

        private void RestoreFromSnapshot(SessionSnapshot snapshot)
        {
            _stops.Clear();
            _stops.AddRange(snapshot.Stops);
            _sessionStartedAt = snapshot.SessionStartedAt;
            _lastStopAt = snapshot.LastStopAt;
            _lastConfirmedPoiId = snapshot.LastConfirmedPoiId;
        }

        private void ClearSession()
        {
            _stops.Clear();
            _sessionStartedAt = null;
            _lastStopAt = DateTime.MinValue;
            _lastConfirmedPoiId = null;
            _dwellEnterTime.Clear();
            Preferences.Default.Remove(PersistKey);
        }

        // ══════════════════════════════════════════════════════════════
        // HELPERS
        // ══════════════════════════════════════════════════════════════

        private static bool IsInRadius(Location user, Poi poi)
        {
            var meters = Location.CalculateDistance(
                user,
                new Location(poi.Lat, poi.Lng),
                DistanceUnits.Kilometers) * 1000.0;
            return meters <= poi.Radius;
        }

        private static string GetOrCreateDeviceId()
        {
            const string key = "route_device_id";
            var existing = Preferences.Default.Get(key, string.Empty);
            if (!string.IsNullOrEmpty(existing)) return existing;
            var newId = Guid.NewGuid().ToString("N");
            Preferences.Default.Set(key, newId);
            return newId;
        }

        // ══════════════════════════════════════════════════════════════
        // INNER TYPES
        // ══════════════════════════════════════════════════════════════

        private class ConfirmedStop
        {
            public int PoiId { get; set; }
            public string PoiName { get; set; } = "";
            public int OrderIndex { get; set; }
            public string TriggerType { get; set; } = "";
            public DateTime TriggeredAt { get; set; }
            public int DwellSeconds { get; set; }
        }

        private class SessionSnapshot
        {
            public List<ConfirmedStop> Stops { get; set; } = new();
            public DateTime SessionStartedAt { get; set; }
            public DateTime LastStopAt { get; set; }
            public int? LastConfirmedPoiId { get; set; }
        }
    }

    // RouteSessionDto và RouteStopDto nằm trong SmartTour.Shared.Models (RouteSessionDto.cs)
    // để cả SmartTourApp và SmartTourBackend dùng chung — không định nghĩa lại ở đây.
}
