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

    /// <summary>
    /// App gọi định kỳ để cập nhật "đang online" (ước lượng theo heartbeat gần nhất).
    /// </summary>
    [HttpPost("heartbeat")]
    [AllowAnonymous]
    public async Task<IActionResult> Heartbeat([FromBody] PresenceHeartbeatDto? dto)
    {
        var id = (dto?.DeviceId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(id) || id.Length > 128)
            return BadRequest(new { success = false, message = "DeviceId không hợp lệ." });

        var now = DateTime.UtcNow;
        var existing = await _context.DevicePresences.FirstOrDefaultAsync(x => x.DeviceId == id);
        if (existing == null)
        {
            _context.DevicePresences.Add(new DevicePresence
            {
                DeviceId = id,
                LastSeenUtc = now
            });
        }
        else
        {
            existing.LastSeenUtc = now;
        }

        await _context.SaveChangesAsync();
        return Ok(new { success = true });
    }

    public class PresenceHeartbeatDto
    {
        public string? DeviceId { get; set; }
    }
}
