using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourBackend.Data;

namespace SmartTourBackend.Controllers;

/// <summary>
/// API endpoint nhận và phân tích tuyến đi của user.
///
/// POST /api/routes/session  — nhận một session từ client
/// GET  /api/routes/popular  — trả về các tuyến đi phổ biến (cho admin analytics)
/// GET  /api/routes/stats    — trả về tổng quan thống kê
/// </summary>
[ApiController]
[Route("api/routes")]
public class RouteSessionController : ControllerBase
{
    private readonly AppDbContext _db;

    // Server-side anti-spam: mỗi device chỉ ghi nhận session cách nhau tối thiểu 5 phút
    private const int MinSessionIntervalMinutes = 5;

    public RouteSessionController(AppDbContext db)
    {
        _db = db;
    }

    // ══════════════════════════════════════════════════════════════════
    // POST /api/routes/session
    // Nhận tuyến đi từ client — validate rồi lưu vào DB
    // ══════════════════════════════════════════════════════════════════
    [HttpPost("session")]
    public async Task<IActionResult> PostSession([FromBody] RouteSessionDto dto)
    {
        // ── 1. Validate cơ bản ──
        if (dto == null)
            return BadRequest(new { success = false, message = "Payload rỗng." });

        if (string.IsNullOrWhiteSpace(dto.DeviceId))
            return BadRequest(new { success = false, message = "DeviceId không hợp lệ." });

        if (dto.Stops == null || dto.Stops.Count < 2)
            return BadRequest(new { success = false, message = "Session cần ít nhất 2 điểm dừng." });

        // ── 2. Server-side anti-spam: kiểm tra session gần nhất của device ──
        var lastSession = await _db.RouteSessions
            .Where(r => r.DeviceId == dto.DeviceId)
            .OrderByDescending(r => r.EndedAt)
            .FirstOrDefaultAsync();

        if (lastSession != null)
        {
            var elapsed = (DateTime.UtcNow - lastSession.EndedAt).TotalMinutes;
            if (elapsed < MinSessionIntervalMinutes)
            {
                return Ok(new
                {
                    success = true,
                    message = "Throttled — session gần nhất chưa đủ thời gian.",
                    throttled = true
                });
            }
        }

        // ── 3. Validate các POI ID có tồn tại không ──
        var poiIds = dto.Stops.Select(s => s.PoiId).Distinct().ToList();
        var validPoiIds = await _db.Pois
            .Where(p => poiIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToHashSetAsync();

        var invalidStops = dto.Stops.Where(s => !validPoiIds.Contains(s.PoiId)).ToList();
        if (invalidStops.Any())
        {
            return BadRequest(new
            {
                success = false,
                message = $"POI không tồn tại: {string.Join(", ", invalidStops.Select(s => s.PoiId))}"
            });
        }

        // ── 4. Tạo RouteSession entity ──
        var session = new RouteSession
        {
            DeviceId = dto.DeviceId,
            PoiSequence = dto.PoiSequence,
            StopCount = dto.Stops.Count,
            StartedAt = dto.StartedAt,
            EndedAt = dto.EndedAt,
            DurationMinutes = dto.DurationMinutes,
            Status = dto.Status ?? "completed",
            RouteSessionPois = dto.Stops.Select(s => new RouteSessionPoi
            {
                PoiId = s.PoiId,
                OrderIndex = s.OrderIndex,
                TriggerType = s.TriggerType,
                TriggeredAt = s.TriggeredAt,
                DwellSeconds = s.DwellSeconds
            }).ToList()
        };

        _db.RouteSessions.Add(session);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            sessionId = session.Id,
            message = $"Tuyến đi đã được ghi nhận: {session.StopCount} điểm dừng."
        });
    }

    // ══════════════════════════════════════════════════════════════════
    // GET /api/routes/popular?limit=10
    // Trả về top tuyến đi phổ biến nhất (PoiSequence xuất hiện nhiều nhất)
    // ══════════════════════════════════════════════════════════════════
    [HttpGet("popular")]
    public async Task<IActionResult> GetPopularRoutes([FromQuery] int limit = 10)
    {
        var popular = await _db.RouteSessions
            .Where(r => r.Status != "expired" && r.StopCount >= 2)
            .GroupBy(r => r.PoiSequence)
            .Select(g => new
            {
                PoiSequence = g.Key,
                Count = g.Count(),
                AvgDurationMinutes = g.Average(r => r.DurationMinutes)
            })
            .OrderByDescending(x => x.Count)
            .Take(limit)
            .ToListAsync();

        return Ok(new
        {
            success = true,
            total = popular.Count,
            data = popular
        });
    }

    // ══════════════════════════════════════════════════════════════════
    // GET /api/routes/stats
    // Tổng quan: số sessions, avg stops, avg duration, top POIs
    // ══════════════════════════════════════════════════════════════════
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var totalSessions = await _db.RouteSessions.CountAsync();
        var completedSessions = await _db.RouteSessions.CountAsync(r => r.Status == "completed");

        double avgStops = totalSessions > 0 ? await _db.RouteSessions.AverageAsync(r => (double)r.StopCount) : 0;
        double avgDuration = totalSessions > 0 ? await _db.RouteSessions.AverageAsync(r => (double)r.DurationMinutes) : 0;

        // Top POIs xuất hiện trong tuyến (Inner Join với bảng Pois)
        var topPois = await _db.RouteSessionPois
            .Join(_db.Pois,
                rsp => rsp.PoiId,
                p => p.Id,
                (rsp, p) => new { rsp, p }) // <--- INNER JOIN
            .GroupBy(x => new { x.p.Id, x.p.Name })
            .Select(g => new
            {
                poiId = g.Key.Id,
                poiName = g.Key.Name,
                count = g.Count(),
                audioManualCount = g.Count(x => x.rsp.TriggerType == "audio_manual"),
                dwellCount = g.Count(x => x.rsp.TriggerType == "dwell"),
                avgDwellSeconds = g.Average(x => (double)x.rsp.DwellSeconds)
            })
            .OrderByDescending(x => x.count)
            .Take(10)
            .ToListAsync();

        return Ok(new
        {
            success = true,
            totalSessions,
            completedSessions,
            avgStops = Math.Round(avgStops, 1),
            avgDurationMinutes = Math.Round(avgDuration, 1),
            topPois
        });
    }
}
