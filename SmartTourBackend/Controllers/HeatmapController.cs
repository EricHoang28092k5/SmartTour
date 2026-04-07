using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourBackend.Data;
using SmartTour.Shared.Models;

namespace SmartTourBackend.Controllers
{
    /// <summary>
    /// Heatmap API — ghi nhận và truy vấn dữ liệu "bao nhiêu lần user đã bước vào vùng POI".
    ///
    /// POST /api/heatmap/entry   → ghi nhận 1 lần trigger
    /// GET  /api/heatmap         → tổng hợp theo POI (poiId + sum)
    /// GET  /api/heatmap/{poiId} → chi tiết 1 POI
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class HeatmapController : ControllerBase
    {
        private readonly AppDbContext _context;

        // ─── Delay chống spam: mỗi device chỉ được ghi nhận 1 lần / POI / khoảng thời gian này ───
        private const int DELAY_MINUTES = 5;

        public HeatmapController(AppDbContext context)
        {
            _context = context;
        }

        // ══════════════════════════════════════════════════════════════════
        // POST /api/heatmap/entry
        // Body: { poiId, deviceId, triggerType, lat, lng }
        // ══════════════════════════════════════════════════════════════════
        [HttpPost("entry")]
        public async Task<IActionResult> PostEntry([FromBody] HeatmapEntryDto dto)
        {
            if (dto == null || dto.PoiId <= 0)
                return BadRequest(new { success = false, message = "PoiId không hợp lệ." });

            // Validate POI tồn tại
            var poiExists = await _context.Pois.AnyAsync(p => p.Id == dto.PoiId);
            if (!poiExists)
                return NotFound(new { success = false, message = "POI không tồn tại." });

            var deviceId = (dto.DeviceId ?? string.Empty).Trim();
            var cutoff = DateTime.UtcNow.AddMinutes(-DELAY_MINUTES);

            // ─── Kiểm tra delay: nếu device này đã trigger POI này trong DELAY_MINUTES phút gần đây → bỏ qua ───
            if (!string.IsNullOrEmpty(deviceId))
            {
                var recentExists = await _context.HeatmapEntries.AnyAsync(h =>
                    h.PoiId == dto.PoiId &&
                    h.DeviceId == deviceId &&
                    h.RecordedAt >= cutoff);

                if (recentExists)
                {
                    return Ok(new
                    {
                        success = false,
                        throttled = true,
                        message = $"Đã ghi nhận gần đây, vui lòng thử lại sau {DELAY_MINUTES} phút."
                    });
                }
            }

            // ─── Ghi nhận entry mới ───
            var entry = new HeatmapEntry
            {
                PoiId = dto.PoiId,
                DeviceId = deviceId,
                RecordedAt = DateTime.UtcNow,
                TriggerType = dto.TriggerType ?? "zone_enter",
                Lat = dto.Lat,
                Lng = dto.Lng
            };

            _context.HeatmapEntries.Add(entry);
            await _context.SaveChangesAsync();

            // Trả về tổng sum hiện tại của POI đó
            var currentSum = await _context.HeatmapEntries
                .CountAsync(h => h.PoiId == dto.PoiId);

            return Ok(new
            {
                success = true,
                entryId = entry.Id,
                poiId = entry.PoiId,
                triggerType = entry.TriggerType,
                currentSum = currentSum
            });
        }

        // ══════════════════════════════════════════════════════════════════
        // GET /api/heatmap
        // Trả về tổng hợp: poiId + sum (tổng lần trigger) cho tất cả POI
        // ══════════════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> GetHeatmap()
        {
            var data = await _context.HeatmapEntries
                .GroupBy(h => h.PoiId)
                .Select(g => new
                {
                    poiId = g.Key,
                    sum = g.Count(),
                    appOpenCount = g.Count(x => x.TriggerType == "app_open"),
                    zoneEnterCount = g.Count(x => x.TriggerType == "zone_enter"),
                    lastRecordedAt = g.Max(x => x.RecordedAt)
                })
                .OrderByDescending(x => x.sum)
                .ToListAsync();

            // Kèm theo thông tin POI nếu cần (name, lat, lng) để frontend render map
            var poiIds = data.Select(d => d.poiId).ToList();
            var poiInfo = await _context.Pois
                .Where(p => poiIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Name, p.Lat, p.Lng })
                .ToListAsync();

            var result = data.Select(d =>
            {
                var poi = poiInfo.FirstOrDefault(p => p.Id == d.poiId);
                return new
                {
                    d.poiId,
                    poiName = poi?.Name ?? "",
                    lat = poi?.Lat ?? 0.0,
                    lng = poi?.Lng ?? 0.0,
                    d.sum,
                    d.appOpenCount,
                    d.zoneEnterCount,
                    d.lastRecordedAt
                };
            });

            return Ok(new { success = true, total = data.Count, data = result });
        }

        // ══════════════════════════════════════════════════════════════════
        // GET /api/heatmap/{poiId}
        // Chi tiết 1 POI: tổng + 100 entry gần nhất
        // ══════════════════════════════════════════════════════════════════
        [HttpGet("{poiId}")]
        public async Task<IActionResult> GetPoiHeatmap(int poiId)
        {
            var total = await _context.HeatmapEntries.CountAsync(h => h.PoiId == poiId);

            if (total == 0)
                return Ok(new { success = true, poiId, sum = 0, data = Array.Empty<object>() });

            var entries = await _context.HeatmapEntries
                .Where(h => h.PoiId == poiId)
                .OrderByDescending(h => h.RecordedAt)
                .Take(100)
                .Select(h => new
                {
                    h.Id,
                    h.RecordedAt,
                    h.TriggerType,
                    h.Lat,
                    h.Lng
                })
                .ToListAsync();

            return Ok(new
            {
                success = true,
                poiId,
                sum = total,
                data = entries
            });
        }
    }

    // ─── DTO nhận từ app ───
    public class HeatmapEntryDto
    {
        public int PoiId { get; set; }
        public string? DeviceId { get; set; }

        /// <summary>"app_open" hoặc "zone_enter"</summary>
        public string? TriggerType { get; set; }

        public double Lat { get; set; }
        public double Lng { get; set; }
    }
}
