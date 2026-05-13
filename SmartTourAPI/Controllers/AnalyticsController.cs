using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using SmartTour.Shared.Models;
using SmartTourAPI.Data;
using SmartTourAPI.Services;

namespace SmartTourAPI.Controllers;

[ApiController]
[Route("api/analytics")]
public class AnalyticsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAudioListenIngestionService _ingestion;
    private readonly IVisitLogIngestionService _visitLogIngestion;

    public AnalyticsController(
        AppDbContext db,
        IAudioListenIngestionService ingestion,
        IVisitLogIngestionService visitLogIngestion)
    {
        _db = db;
        _ingestion = ingestion;
        _visitLogIngestion = visitLogIngestion;
    }

    [Authorize]
    [EnableRateLimiting("AudioListenPolicy")]
    [HttpPost("poi-audio-listen")]
    public async Task<IActionResult> PostPoiAudioListen([FromBody] PoiAudioListenDto dto)
    {
        if (dto.PoiId <= 0 || dto.DurationSeconds < 0 || string.IsNullOrWhiteSpace(dto.DeviceId))
            return BadRequest(new { accepted = false, reason = "invalid_payload" });

        if (dto.DeviceId.Length > 128)
            return BadRequest(new { accepted = false, reason = "invalid_device_id" });

        if (!IsQualifiedListen(dto.DurationSeconds, dto.TotalDurationSeconds, dto.CompletedNaturally))
            return Ok(new { accepted = false, reason = "listen_threshold_not_reached" });

        var accepted = _ingestion.TryEnqueue(dto.PoiId, dto.DurationSeconds, dto.DeviceId, out var reason);
        if (!accepted)
            return Ok(new { accepted = false, reason });

        return Ok(new { accepted = true, queued = true });
    }

    /// <summary>
    /// Ghi nhận lượt ghé POI (enqueue — không chờ DB). UserId ưu tiên từ JWT nếu có.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("DeviceTokenPolicy")]
    [HttpPost("visit")]
    public IActionResult LogVisit([FromBody] LogVisitDto? dto)
    {
        if (dto == null || dto.PoiId <= 0)
            return BadRequest(new { error = "invalid_poi" });

        var resolvedUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? (dto.UserId ?? string.Empty).Trim();

        var item = new VisitLogQueueItem
        {
            PoiId = dto.PoiId,
            UserId = resolvedUserId,
            Lat = dto.Lat,
            Lng = dto.Lng,
            VisitType = dto.VisitType,
            SpeedKmh = dto.SpeedKmh
        };

        if (!_visitLogIngestion.TryEnqueue(item, out var reason))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "queue_unavailable", reason });

        return Accepted(new { message = "Visit queued" });
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("poi-audio-listen-stats")]
    public async Task<IActionResult> GetPoiAudioListenStats([FromQuery] int days = 30)
    {
        if (days <= 0) days = 30;
        var since = DateTime.UtcNow.AddDays(-days);

        var stats = await _db.PoiAudioListenEvents
            .Where(x => x.CreatedAt >= since)
            .Join(_db.Pois, e => e.PoiId, p => p.Id, (e, p) => new { e, p })
            .GroupBy(x => new { x.p.Id, x.p.Name })
            .Select(g => new
            {
                poiId = g.Key.Id,
                poiName = g.Key.Name,
                listens = g.Count(),
                avgDurationSeconds = g.Average(x => x.e.DurationSeconds),
                totalDurationSeconds = g.Sum(x => x.e.DurationSeconds)
            })
            .OrderByDescending(x => x.totalDurationSeconds)
            .ToListAsync();

        return Ok(new { success = true, data = stats });
    }

    private static bool IsQualifiedListen(int listenedSeconds, int? totalDurationSeconds, bool completedNaturally)
    {
        if (listenedSeconds <= 0) return false;
        if (!totalDurationSeconds.HasValue || totalDurationSeconds.Value <= 0)
            return completedNaturally || listenedSeconds >= 15;

        var threshold = totalDurationSeconds.Value < 15
            ? totalDurationSeconds.Value * 0.5
            : 15.0;
        return listenedSeconds >= threshold;
    }
}

public class PoiAudioListenDto
{
    public int PoiId { get; set; }
    public int DurationSeconds { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public int? TotalDurationSeconds { get; set; }
    public bool CompletedNaturally { get; set; }
}

public class LogVisitDto
{
    public int PoiId { get; set; }
    public string? UserId { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
    public VisitType VisitType { get; set; }
    /// <summary>Tốc độ km/h (tuỳ chọn). Client GPS thường đổi từ m/s sang km/h.</summary>
    public double? SpeedKmh { get; set; }
}
