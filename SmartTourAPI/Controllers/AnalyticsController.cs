using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
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

    public AnalyticsController(AppDbContext db, IAudioListenIngestionService ingestion)
    {
        _db = db;
        _ingestion = ingestion;
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
