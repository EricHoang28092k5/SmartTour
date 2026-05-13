using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourAPI.Data;
using SmartTourCMS.Services;

namespace SmartTourCMS.Controllers;

/// <summary>
/// Load test & Geofence Simulator (Admin). Visit thật qua API; log DB đọc trực tiếp từ CMS (cùng DB).
/// </summary>
[Authorize(Roles = "Admin")]
public sealed class LoadTestController : Controller
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogRunner _logRunner;

    public LoadTestController(
        AppDbContext db,
        IHttpClientFactory httpFactory,
        IConfiguration configuration,
        ILogRunner logRunner)
    {
        _db = db;
        _httpFactory = httpFactory;
        _configuration = configuration;
        _logRunner = logRunner;
    }

    public IActionResult Index() => View();

    /// <summary>Hướng dẫn NFR (k6, script PS), LogRunner &amp; Geofence Simulator — Admin.</summary>
    public IActionResult NfrGuide() => View();

    public IActionResult GeofenceSimulator() => View();

    /// <summary>
    /// Stress test: N request song song tới <c>api/analytics/visit</c>, userId <c>SIM-BULK-###</c>, tọa độ 0,0
    /// (API dùng <c>double</c> — tương đương HeriStep gửi lat/lng null cho bulk).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> RunBulkVisit([FromBody] BulkVisitRunRequest? body, CancellationToken cancellationToken)
    {
        body ??= new BulkVisitRunRequest();
        var n = Math.Clamp(body.DeviceCount, 1, 200);
        if (body.PoiId <= 0)
            return BadRequest(new { ok = false, error = "invalid_poi" });

        if (string.IsNullOrWhiteSpace(_configuration["BackendApi:BaseUrl"]))
            return BadRequest(new { ok = false, error = "BackendApi:BaseUrl_missing" });

        var poiOk = await _db.Pois.AsNoTracking()
            .AnyAsync(p => p.Id == body.PoiId && p.IsActive, cancellationToken)
            .ConfigureAwait(false);
        if (!poiOk)
            return BadRequest(new { ok = false, error = "poi_not_found_or_inactive" });

        var client = _httpFactory.CreateClient("SmartTourApi");
        if (client.BaseAddress == null)
            return BadRequest(new { ok = false, error = "HttpClient_BaseAddress_missing" });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        // Mỗi tác vụ lấy HttpClient riêng từ factory — tránh nghẽn khi bắn song song cao.
        var tasks = new List<Task<(bool ok, int status, string? detail)>>(n);
        for (var i = 1; i <= n; i++)
        {
            var index = i;
            var poiId = body.PoiId;
            tasks.Add(Task.Run(
                async () =>
                {
                    var c = _httpFactory.CreateClient("SmartTourApi");
                    return await FireOneBulkVisitAsync(c, poiId, index, cancellationToken).ConfigureAwait(false);
                },
                cancellationToken));
        }

        var rows = await Task.WhenAll(tasks).ConfigureAwait(false);
        sw.Stop();
        var okCount = rows.Count(r => r.ok);
        var byStatus = rows
            .GroupBy(r => r.status)
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key == 0 ? "exception_or_no_response" : g.Key.ToString(), g => g.Count());
        var failures = rows.Where(r => !r.ok).ToList();
        object? firstFailurePayload = failures.Count == 0
            ? null
            : new { failures[0].status, detail = failures[0].detail };
        return Json(new
        {
            ok = true,
            requested = n,
            acceptedHttp = okCount,
            failed = n - okCount,
            elapsedMs = sw.ElapsedMilliseconds,
            sampleUserIds = new[] { "SIM-BULK-001", $"SIM-BULK-{n:D3}" },
            httpStatusCounts = byStatus,
            firstFailure = firstFailurePayload,
            hint = okCount == 0
                ? "Thường gặp: API không chạy / sai BackendApi:BaseUrl; 429 (rate limit 60/10s/IP); lỗi TLS; timeout. Xem firstFailure + httpStatusCounts."
                : "Nếu có 429: giảm số thiết bị hoặc chạy lại sau 10s (DeviceTokenPolicy).",
            note = "Lat/Lng = 0 — API không nhận null; VisitType = MapClick (1)."
        });
    }

    private static async Task<(bool ok, int status, string? detail)> FireOneBulkVisitAsync(
        HttpClient client,
        int poiId,
        int deviceIndex,
        CancellationToken cancellationToken)
    {
        var userId = $"SIM-BULK-{deviceIndex:D3}";
        try
        {
            using var res = await client.PostAsJsonAsync(
                    "api/analytics/visit",
                    new
                    {
                        poiId,
                        userId,
                        lat = 0.0,
                        lng = 0.0,
                        visitType = VisitType.MapClick
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            var code = (int)res.StatusCode;
            if (res.IsSuccessStatusCode)
                return (true, code, null);

            var body = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var snippet = body.Length > 200 ? body[..200] + "…" : body;
            return (false, code, string.IsNullOrWhiteSpace(snippet) ? res.ReasonPhrase : snippet);
        }
        catch (Exception ex)
        {
            return (false, 0, ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>
    /// Simulator: <c>clusterSize</c> vòng (3–12) — <strong>tâm &amp; bán kính ngẫu nhiên</strong> (không đồng tâm),
    /// nhưng luôn có điểm <c>intersection</c> nằm trong <strong>mọi</strong> vòng (chứng minh bằng cách đặt tâm POI
    /// lệch quanh neo và <c>radius &gt; khoảng cách neo → tâm</c>). Id/tên/visit = POI thật từ DB; hình học map = giả lập.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> POIs(
        [FromQuery] int clusterSize = 4,
        [FromQuery] double? seedLat = null,
        [FromQuery] double? seedLng = null,
        [FromQuery] bool randomLocation = false)
    {
        clusterSize = Math.Clamp(clusterSize, 3, 12);

        double anchorLat;
        double anchorLng;
        if (randomLocation)
        {
            (anchorLat, anchorLng) = RandomPointVietnamMainland();
        }
        else
        {
            var rawLat = seedLat ?? 10.776889;
            var rawLng = seedLng ?? 106.700806;
            (anchorLat, anchorLng) = ClampToVietnamMainland(rawLat, rawLng);
        }

        var approvedCount = await _db.Pois.AsNoTracking()
            .CountAsync(p => p.IsActive && p.ApprovalStatus == "approved");
        if (approvedCount < clusterSize)
        {
            return Json(new
            {
                center = new { lat = anchorLat, lng = anchorLng },
                pois = Array.Empty<PoiSimRow>(),
                error = "not_enough_pois",
                message = $"Cần ít nhất {clusterSize} POI đã duyệt trong DB (hiện có {approvedCount})."
            });
        }

        var rows = await _db.Pois.AsNoTracking()
            .Where(p => p.IsActive && p.ApprovalStatus == "approved")
            .OrderBy(p => p.Id)
            .Take(clusterSize)
            .Select(p => new { p.Id, p.Name, p.IsPremium, p.Priority })
            .ToListAsync();

        var tierAssignments = BuildRandomTierAssignments(rows.Count);

        // Neo nằm trong mọi đĩa: tâm POI_i cách neo delta_i mét, bán kính > delta_i.
        var maxOffset = clusterSize <= 4 ? 110.0 : clusterSize <= 7 ? 85.0 : 65.0;
        var pois = new List<PoiSimRow>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var bearing = Random.Shared.NextDouble() * 2 * Math.PI;
            var deltaM = 12 + Random.Shared.NextDouble() * maxOffset;
            var (pLat, pLng) = OffsetMeters(anchorLat, anchorLng, bearing, deltaM);
            var margin = 38 + Random.Shared.NextDouble() * 155;
            var radius = (int)Math.Round(deltaM + margin);
            radius = Math.Clamp(radius, (int)Math.Ceiling(deltaM) + 25, 520);

            var row = rows[i];
            var a = tierAssignments[i];
            pois.Add(new PoiSimRow(
                row.Id,
                row.Name ?? string.Empty,
                pLat,
                pLng,
                Math.Max(radius, 80),
                a.IsPremium,
                row.Priority,
                a.Popularity,
                a.SimTier));
        }

        return Json(new
        {
            center = new { lat = anchorLat, lng = anchorLng },
            intersection = new { lat = anchorLat, lng = anchorLng },
            syntheticLayout = "random_overlapping_disks",
            region = "VN",
            randomLocation,
            clusterSizeRequested = clusterSize,
            clusterSizeActual = pois.Count,
            priorityOrder = "Premium → Heatmap(popularity) → Khoảng cách → thứ tự list",
            pois
        });
    }

    /// <summary>Chia N POI: luôn có Premium, Heatmap (≥2 POI thì 2 heatmap với pop khác nhau), và phần còn lại “khoảng cách”.</summary>
    private static (bool IsPremium, int Popularity, string SimTier)[] BuildRandomTierAssignments(int n)
    {
        var (prem, heat, plain) = SplitSimulatorTierCounts(n);
        var perm = Enumerable.Range(0, n).ToArray();
        Shuffle(perm);

        var result = new (bool IsPremium, int Popularity, string SimTier)[n];

        var hi = Random.Shared.Next(280, 520);
        var lo = Random.Shared.Next(40, Math.Max(41, hi - 80));
        if (Random.Shared.Next(2) == 0)
            (hi, lo) = (lo, hi);

        var pi = 0;
        for (var k = 0; k < prem; k++)
        {
            var idx = perm[pi++];
            result[idx] = (true, Random.Shared.Next(8, 45), "Premium");
        }

        for (var k = 0; k < heat; k++)
        {
            var idx = perm[pi++];
            var pop = heat == 1 ? hi : (k == 0 ? hi : lo);
            result[idx] = (false, pop, "Heatmap");
        }

        for (var k = 0; k < plain; k++)
        {
            var idx = perm[pi++];
            result[idx] = (false, Random.Shared.Next(0, 6), "Distance");
        }

        return result;
    }

    private static (int Premium, int Heatmap, int Distance) SplitSimulatorTierCounts(int n)
    {
        if (n < 3)
            return (0, 0, n);
        return n switch
        {
            3 => (1, 1, 1),
            4 => (2, 1, 1),
            5 => (2, 2, 1),
            _ => (2, 2, n - 4)
        };
    }

    private static void Shuffle(int[] a)
    {
        for (var i = a.Length - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (a[i], a[j]) = (a[j], a[i]);
        }
    }

    private static (double lat, double lng) OffsetMeters(double lat0, double lon0, double bearingRad, double distMeters)
    {
        const double latScale = 111320.0;
        var cosLat = Math.Cos(lat0 * Math.PI / 180.0);
        if (Math.Abs(cosLat) < 0.15)
            cosLat = cosLat < 0 ? -0.15 : 0.15;
        var lngScale = 111320.0 * cosLat;
        return (
            lat0 + distMeters * Math.Cos(bearingRad) / latScale,
            lon0 + distMeters * Math.Sin(bearingRad) / lngScale);
    }

    // Bbox đất liền Việt Nam (WGS84). Lề ~0.007° (~770m) để kể cả đĩa lớn vẫn nằm trong VN.
    private const double VnMinLat = 8.62 + 0.007;
    private const double VnMaxLat = 23.33 - 0.007;
    private const double VnMinLng = 102.14 + 0.007;
    private const double VnMaxLng = 109.42 - 0.007;

    private static (double lat, double lng) RandomPointVietnamMainland()
    {
        return (
            VnMinLat + Random.Shared.NextDouble() * (VnMaxLat - VnMinLat),
            VnMinLng + Random.Shared.NextDouble() * (VnMaxLng - VnMinLng));
    }

    /// <summary>Giới hạn neo/seed trong Việt Nam (simulator chỉ trong VN).</summary>
    private static (double lat, double lng) ClampToVietnamMainland(double lat, double lng) =>
        (Math.Clamp(lat, VnMinLat, VnMaxLat), Math.Clamp(lng, VnMinLng, VnMaxLng));

    /// <summary>Proxy POST analytics/visit (202). Chỉ cho userId bắt đầu SIM-.</summary>
    [HttpPost]
    public async Task<IActionResult> FireGeofenceVisit([FromBody] FireGeofenceVisitRequest? body,
        CancellationToken cancellationToken)
    {
        if (body == null || body.PoiId <= 0)
            return BadRequest(new { ok = false, error = "invalid_payload" });

        var uid = (body.UserId ?? string.Empty).Trim();
        if (!uid.StartsWith("SIM-", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { ok = false, error = "userId_must_start_with_SIM_" });

        if (string.IsNullOrWhiteSpace(_configuration["BackendApi:BaseUrl"]))
            return BadRequest(new { ok = false, error = "BackendApi:BaseUrl_missing" });

        var client = _httpFactory.CreateClient("SmartTourApi");
        if (client.BaseAddress == null)
            return BadRequest(new { ok = false, error = "HttpClient_BaseAddress_missing" });

        HttpResponseMessage res;
        try
        {
            res = await client.PostAsJsonAsync(
                    "api/analytics/visit",
                    new
                    {
                        poiId = body.PoiId,
                        userId = uid,
                        lat = body.Lat,
                        lng = body.Lng,
                        visitType = VisitType.Geofence,
                        speedKmh = body.SpeedKmh
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "http_error", detail = ex.Message });
        }

        var text = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return Json(new
        {
            ok = res.IsSuccessStatusCode,
            status = (int)res.StatusCode,
            body = text.Length > 500 ? text[..500] : text
        });
    }

    /// <summary>Đọc VisitLog gần đây của user SIM-* (minh chứng worker/API).</summary>
    [HttpGet]
    public async Task<IActionResult> RecentVisitLogs([FromQuery] int minutes = 45, [FromQuery] int take = 200)
    {
        minutes = Math.Clamp(minutes, 5, 24 * 60);
        take = Math.Clamp(take, 10, 500);
        var since = DateTime.UtcNow.AddMinutes(-minutes);

        var rows = await _db.VisitLogs.AsNoTracking()
            .Where(v => v.UserId.StartsWith("SIM-") && v.VisitTime >= since)
            .OrderByDescending(v => v.VisitTime)
            .Take(take)
            .Select(v => new
            {
                v.Id,
                v.PoiId,
                v.UserId,
                v.Lat,
                v.Lng,
                v.SpeedKmh,
                v.VisitTime,
                visitType = v.VisitType.ToString()
            })
            .ToListAsync();

        return Json(new { ok = true, count = rows.Count, items = rows });
    }

    /// <summary>Ghi client log qua ILogRunner: ghi đè hoặc nối tiếp (append) theo body.</summary>
    [HttpPost]
    public async Task<IActionResult> SaveSimulatorLog([FromBody] SaveSimulatorLogRequest? body)
    {
        body ??= new SaveSimulatorLogRequest();
        var text = body.Text ?? string.Empty;
        var sid = body.SessionId ?? "geofence-sim";
        if (body.Append)
            await _logRunner.AppendAsync(text, sid).ConfigureAwait(false);
        else
            await _logRunner.OverwriteAsync(text, sid).ConfigureAwait(false);
        return Json(new { ok = true, path = _logRunner.LogFilePath, append = body.Append });
    }

    /// <summary>Tải <c>logqueue.txt</c> (cùng file ILogRunner) — gắn LoadTest để dùng ngay trên Geofence Simulator.</summary>
    [HttpGet]
    public async Task<IActionResult> DownloadQueueLog()
    {
        var bytes = await _logRunner.ReadBytesAsync().ConfigureAwait(false);
        return File(bytes, "text/plain", "logqueue.txt");
    }

    /// <summary>Đọc toàn bộ nội dung file log (JSON) — dùng cho nút View trên simulator.</summary>
    [HttpGet]
    public async Task<IActionResult> ReadQueueLog()
    {
        var text = await _logRunner.ReadAsync().ConfigureAwait(false);
        return Json(new { ok = true, path = _logRunner.LogFilePath, text });
    }

    public sealed record PoiSimRow(
        int Id,
        string Name,
        double Lat,
        double Lng,
        int Radius,
        bool IsPremium,
        int Priority,
        int Popularity,
        string SimTier);

    public sealed class BulkVisitRunRequest
    {
        /// <summary>Số thiết bị giả (1–200), mỗi thiết bị một POST visit.</summary>
        public int DeviceCount { get; set; } = 10;

        public int PoiId { get; set; }
    }

    public sealed class FireGeofenceVisitRequest
    {
        public int PoiId { get; set; }
        public double Lat { get; set; }
        public double Lng { get; set; }
        public string? UserId { get; set; }
        /// <summary>Tốc độ km/h tại thời điểm visit (simulator / client).</summary>
        public double? SpeedKmh { get; set; }
    }

    public sealed class SaveSimulatorLogRequest
    {
        public string? Text { get; set; }
        public string? SessionId { get; set; }
        /// <summary>Nếu true: nối vào cuối file; false: ghi đè cả file.</summary>
        public bool Append { get; set; }
    }
}
