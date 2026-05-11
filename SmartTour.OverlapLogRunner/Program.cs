using System.Globalization;
using System.Text;
using SmartTour.Shared.Models;

namespace SmartTour.OverlapLogRunner;

/// <summary>
/// Mô phỏng theo đoạn chat nhóm: nhiều máy cùng đứng giao vùng nhiều POI,
/// log thứ tự ưu tiên (Premium → heat → gần → thứ tự list) + skip/ghim kiểu MarketOverlap.
/// Không cần MAUI — khoảng cách dùng Haversine (tương đương Location.CalculateDistance).
/// </summary>
internal static class Program
{
    private const double MaxHorizontalAccuracyMeters = 120.0;
    private const int DefaultDeviceCount = 4;
    private const int MinDeviceCount = 1;
    private const int MaxDeviceCount = 256;

    public static async Task<int> Main(string[] args)
    {
        var (outPath, deviceCount) = ParseRunnerArgs(args);
        deviceCount = Math.Clamp(deviceCount, MinDeviceCount, MaxDeviceCount);

        var sb = new StringBuilder();
        void Log(string line)
        {
            var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            var full = $"[{ts}] {line}";
            Console.WriteLine(full);
            sb.AppendLine(full);
        }

        Log("=== SmartTour Overlap LogRunner (mô phỏng khu chợ / giao POI) ===");
        Log($"File log: {outPath}");
        Log($"Số thiết bị: {deviceCount} (DEV-01 … DEV-{deviceCount:00}; đổi bằng tham số dòng lệnh, xem README.txt)");
        Log("");

        var (centerLat, centerLng, pois, popularity) = BuildIntersectionScenario();
        Log($"Điểm giao (user): lat={centerLat:F6}, lng={centerLng:F6}");
        Log($"Số POI: {pois.Count}; popularity: {string.Join(", ", popularity.Select(kv => $"{kv.Key}={kv.Value}"))}");
        Log("");

        var devices = Enumerable.Range(1, deviceCount)
            .Select(i => new DeviceSimulator($"DEV-{i:00}"))
            .ToArray();

        // --- TICK 1: cùng tọa độ, accuracy tốt ---
        Log($"--- TICK 1: {deviceCount} thiết bị cùng vị trí, GPS accuracy = 25m ---");
        foreach (var d in devices)
            d.EvaluateAndLog(Log, centerLat, centerLng, accuracyMeters: 25, pois, popularity);

        // --- Máy đầu vuốt skip POI đang thắng (giống MapPage swipe) ---
        var headAfterT1 = GetOrderedOverlapping(centerLat, centerLng, pois, popularity).FirstOrDefault();
        if (headAfterT1 != null)
        {
            Log("");
            Log($"--- Giữa tick: {devices[0].DeviceId} SKIP POI id={headAfterT1.Id} ({headAfterT1.Name}) ---");
            devices[0].Skip(headAfterT1.Id);
        }

        Log("");
        Log($"--- TICK 2: sau khi {devices[0].DeviceId} skip, các máy khác giữ nguyên trạng thái dispatch ---");
        foreach (var d in devices)
            d.EvaluateAndLog(Log, centerLat, centerLng, accuracyMeters: 25, pois, popularity);

        // --- GPS quá kém (máy thứ 2, nếu có) ---
        Log("");
        if (devices.Length >= 2)
        {
            Log($"--- TICK 3: {devices[1].DeviceId} accuracy = 150m (>120) → không auto-play ---");
            devices[1].EvaluateAndLog(Log, centerLat, centerLng, accuracyMeters: 150, pois, popularity);
        }
        else
            Log("--- TICK 3: bỏ qua (cần ≥2 thiết bị để mô phỏng GPS kém trên máy thứ 2) ---");

        // --- Ghim (máy thứ 3, nếu có) ---
        Log("");
        var pinTarget = pois.FirstOrDefault(p => p.Id != headAfterT1?.Id) ?? pois[0];
        if (devices.Length >= 3)
        {
            Log($"--- {devices[2].DeviceId} GHIM auto-play vào POI id={pinTarget.Id} ({pinTarget.Name}) ---");
            devices[2].SetPinned(pinTarget.Id);
            devices[2].EvaluateAndLog(Log, centerLat, centerLng, accuracyMeters: 25, pois, popularity);
        }
        else
            Log($"--- GHIM: bỏ qua (cần ≥3 thiết bị; ví dụ POI ghim sẽ là id={pinTarget.Id} {pinTarget.Name}) ---");

        // --- Ra khỏi mọi vùng ---
        Log("");
        Log("--- TICK 4: di chuyển xa (ngoài mọi bán kính) → hết vùng, bỏ ghim nếu có ---");
        var farLat = centerLat + 0.02;
        var farLng = centerLng + 0.02;
        foreach (var d in devices)
            d.EvaluateAndLog(Log, farLat, farLng, accuracyMeters: 25, pois, popularity);

        Log("");
        Log("=== Kết thúc mô phỏng ===");

        var logDir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(logDir))
            Directory.CreateDirectory(logDir);

        await File.WriteAllTextAsync(outPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Console.WriteLine();
        Console.WriteLine($"Đã ghi log UTF-8: {outPath}");
        return 0;
    }

    /// <summary>
    /// Tham số tùy ý thứ tự: đường dẫn file log và/hoặc số thiết bị (số nguyên 1–256).
    /// Ví dụ: <c>-- "D:\a.txt" 7</c> hoặc <c>-- 12</c>.
    /// </summary>
    private static (string outPath, int deviceCount) ParseRunnerArgs(string[] args)
    {
        string? pathArg = null;
        int? countArg = null;

        foreach (var raw in args)
        {
            var a = raw.Trim();
            if (a.Length == 0)
                continue;

            if (int.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) &&
                n >= MinDeviceCount && n <= MaxDeviceCount)
            {
                countArg = n;
                continue;
            }

            pathArg = Path.GetFullPath(a);
        }

        var outPath = pathArg ?? GetDefaultLogFilePath();
        var deviceCount = countArg ?? DefaultDeviceCount;
        return (outPath, deviceCount);
    }

    private const string DefaultLogFileName = "overlap_simulation_log.txt";

    /// <summary>
    /// Mặc định: <c>SmartTour.OverlapLogRunner/overlap_simulation_log.txt</c> (cùng thư mục với .csproj).
    /// Nếu layout không phải build dev (vd. publish đơn lẻ) → file cạnh exe.
    /// </summary>
    private static string GetDefaultLogFilePath()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidateProjectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));
        var csproj = Path.Combine(candidateProjectRoot, "SmartTour.OverlapLogRunner.csproj");
        if (File.Exists(csproj))
            return Path.Combine(candidateProjectRoot, DefaultLogFileName);

        return Path.Combine(Path.GetFullPath(baseDir), DefaultLogFileName);
    }

    /// <summary>4 POI quanh giao lộ; user đứng chính giữa → nằm trong cả 4 vòng.</summary>
    private static (double lat, double lng, List<Poi> pois, Dictionary<int, int> popularity) BuildIntersectionScenario()
    {
        // Gần trung tâm TP.HCM (chỉ làm ví dụ toạ độ)
        const double centerLat = 10.776889;
        const double centerLng = 106.700806;
        const double d = 0.00072; // ~80m theo vĩ độ

        var pois = new List<Poi>
        {
            new()
            {
                Id = 1,
                Name = "Quán A (Premium)",
                Lat = centerLat + d,
                Lng = centerLng,
                Radius = 150,
                IsPremium = true,
                Priority = 2
            },
            new()
            {
                Id = 2,
                Name = "Quán B",
                Lat = centerLat - d,
                Lng = centerLng,
                Radius = 150,
                IsPremium = false,
                Priority = 5
            },
            new()
            {
                Id = 3,
                Name = "Quán C",
                Lat = centerLat,
                Lng = centerLng + d / Math.Cos(centerLat * Math.PI / 180.0),
                Radius = 150,
                IsPremium = false,
                Priority = 3
            },
            new()
            {
                Id = 4,
                Name = "Quán D",
                Lat = centerLat,
                Lng = centerLng - d / Math.Cos(centerLat * Math.PI / 180.0),
                Radius = 150,
                IsPremium = false,
                Priority = 4
            }
        };

        var popularity = new Dictionary<int, int>
        {
            [1] = 50,
            [2] = 200,
            [3] = 200,
            [4] = 10
        };

        return (centerLat, centerLng, pois, popularity);
    }

    /// <summary>Khoảng cách mét (WGS84).</summary>
    public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000.0;
        var p1 = lat1 * Math.PI / 180.0;
        var p2 = lat2 * Math.PI / 180.0;
        var dP = (lat2 - lat1) * Math.PI / 180.0;
        var dL = (lon2 - lon1) * Math.PI / 180.0;
        var a = Math.Sin(dP / 2) * Math.Sin(dP / 2) +
                Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dL / 2) * Math.Sin(dL / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    public static List<Poi> GetOrderedOverlapping(
        double userLat,
        double userLng,
        List<Poi> pois,
        IReadOnlyDictionary<int, int> popularity)
    {
        var raw = new List<(Poi poi, double dist, int queryIndex)>();
        for (var i = 0; i < pois.Count; i++)
        {
            var poi = pois[i];
            var meters = DistanceMeters(userLat, userLng, poi.Lat, poi.Lng);
            if (meters <= poi.Radius)
                raw.Add((poi, meters, i));
        }

        if (raw.Count == 0)
            return new List<Poi>();

        static int Pop(IReadOnlyDictionary<int, int> snap, int id) =>
            snap.TryGetValue(id, out var v) ? v : 0;

        return raw
            .OrderByDescending(x => x.poi.IsPremium)
            .ThenByDescending(x => Pop(popularity, x.poi.Id))
            .ThenBy(x => x.dist)
            .ThenBy(x => x.queryIndex)
            .Select(x => x.poi)
            .ToList();
    }

    private sealed class DeviceSimulator
    {
        private readonly string _deviceId;
        private readonly HashSet<int> _skipped = new();
        private int? _pinnedPoiId;
        private int? _lastDispatchedPoiId;

        public DeviceSimulator(string deviceId) => _deviceId = deviceId;

        public string DeviceId => _deviceId;

        public void Skip(int poiId)
        {
            _skipped.Add(poiId);
            if (_lastDispatchedPoiId == poiId)
                _lastDispatchedPoiId = null;
        }

        public void SetPinned(int poiId)
        {
            _pinnedPoiId = poiId;
            _lastDispatchedPoiId = null;
        }

        public void EvaluateAndLog(
            Action<string> log,
            double userLat,
            double userLng,
            double? accuracyMeters,
            List<Poi> pois,
            IReadOnlyDictionary<int, int> popularity)
        {
            if (accuracyMeters is double acc && acc > MaxHorizontalAccuracyMeters)
            {
                log($"{_deviceId} | GPS accuracy {acc:F0}m > {MaxHorizontalAccuracyMeters} → bỏ qua auto-play (giống MarketOverlapPlaybackService).");
                return;
            }

            var ordered = GetOrderedOverlapping(userLat, userLng, pois, popularity);
            var inZone = ordered.Select(p => p.Id).ToHashSet();
            _skipped.RemoveWhere(id => !inZone.Contains(id));

            if (ordered.Count == 0)
            {
                var hadPin = _pinnedPoiId != null;
                _pinnedPoiId = null;
                _lastDispatchedPoiId = null;
                log($"{_deviceId} | Không còn POI trong bán kính. hadPinBeforeClear={hadPin} → StopNarration={(hadPin ? "yes" : "no")}.");
                return;
            }

            IEnumerable<Poi> seq = ordered;
            if (_pinnedPoiId is int pinId)
            {
                if (!ordered.Any(p => p.Id == pinId))
                {
                    _pinnedPoiId = null;
                    _lastDispatchedPoiId = null;
                    log($"{_deviceId} | POI ghim id={pinId} không còn trong vùng → bỏ ghim, StopNarration=yes.");
                    return;
                }

                seq = ordered.Where(p => p.Id == pinId);
            }

            var chain = seq.Where(p => !_skipped.Contains(p.Id)).ToList();
            var inRadiusLines = ordered.Select(p =>
                    $"{p.Name}(id={p.Id}, prem={p.IsPremium}, dist={DistanceMeters(userLat, userLng, p.Lat, p.Lng):F1}m, pop={popularity.GetValueOrDefault(p.Id)})")
                .ToList();
            log($"{_deviceId} | Trong vùng (đã sắp): {string.Join(" | ", inRadiusLines)}");

            if (chain.Count == 0)
            {
                log($"{_deviceId} | Tất cả POI trong chuỗi đều đang skip → không phát.");
                return;
            }

            var head = chain[0];
            if (_lastDispatchedPoiId == head.Id)
            {
                log($"{_deviceId} | HOLD — head vẫn là id={head.Id}, không gọi Play lại (tránh spam tick).");
                return;
            }

            _lastDispatchedPoiId = head.Id;
            log($"{_deviceId} | PLAY → id={head.Id} \"{head.Name}\" (pinned={_pinnedPoiId?.ToString() ?? "none"}, skipped=[{string.Join(",", _skipped)}])");
        }
    }
}
