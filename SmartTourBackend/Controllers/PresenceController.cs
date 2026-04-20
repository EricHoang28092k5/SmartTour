using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourBackend.Data;

namespace SmartTourBackend.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PresenceController : ControllerBase
{
    private readonly AppDbContext _context;

    public PresenceController(AppDbContext context)
    {
        _context = context;
    }

    [HttpPost("heartbeat")]
    [AllowAnonymous]
    public async Task<IActionResult> Heartbeat([FromBody] PresenceHeartbeatDto? dto)
    {
        var id = (dto?.DeviceId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128)
            return BadRequest(new { success = false, message = "DeviceId không hợp lệ." });

        var now = DateTime.UtcNow;
        var ip = GetClientIpAddress();
        var userAgent = Request.Headers.UserAgent.ToString();

        var existing = await _context.DevicePresences.FirstOrDefaultAsync(x => x.DeviceId == id);
        if (existing == null)
        {
            _context.DevicePresences.Add(new DevicePresence
            {
                DeviceId = id,
                IpAddress = Truncate(ip, 128),
                UserAgent = Truncate(userAgent, 512),
                DeviceModel = Truncate(dto?.DeviceModel, 128),
                Platform = Truncate(dto?.Platform, 64),
                OsVersion = Truncate(dto?.OsVersion, 64),
                AppVersion = Truncate(dto?.AppVersion, 32),
                LastSeenUtc = now
            });
        }
        else
        {
            existing.IpAddress = Truncate(ip, 128);
            existing.UserAgent = Truncate(userAgent, 512);
            existing.DeviceModel = Truncate(dto?.DeviceModel, 128);
            existing.Platform = Truncate(dto?.Platform, 64);
            existing.OsVersion = Truncate(dto?.OsVersion, 64);
            existing.AppVersion = Truncate(dto?.AppVersion, 32);
            existing.LastSeenUtc = now;
        }

        await _context.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("offline")]
    [AllowAnonymous]
    public async Task<IActionResult> Offline([FromBody] PresenceHeartbeatDto? dto)
    {
        var id = (dto?.DeviceId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128)
            return BadRequest(new { success = false, message = "DeviceId không hợp lệ." });

        var existing = await _context.DevicePresences.FirstOrDefaultAsync(x => x.DeviceId == id);
        if (existing == null) return Ok(new { success = true });

        // Đánh dấu offline ngay để dashboard cập nhật trạng thái tức thì.
        existing.LastSeenUtc = DateTime.UtcNow.AddMinutes(-30);
        await _context.SaveChangesAsync();
        return Ok(new { success = true });
    }

    private string GetClientIpAddress()
    {
        var forwarded = Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded.Split(',')[0].Trim();
            if (!string.IsNullOrWhiteSpace(first))
                return first;
        }

        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
    }

    private static string Truncate(string? value, int maxLength)
    {
        var v = (value ?? string.Empty).Trim();
        return v.Length <= maxLength ? v : v[..maxLength];
    }

    public class PresenceHeartbeatDto
    {
        public string? DeviceId { get; set; }
        public string? DeviceModel { get; set; }
        public string? Platform { get; set; }
        public string? OsVersion { get; set; }
        public string? AppVersion { get; set; }
    }
}
