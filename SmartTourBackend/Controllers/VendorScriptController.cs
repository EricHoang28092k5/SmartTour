using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourBackend.Data;

namespace SmartTourBackend.Controllers;

[ApiController]
[Route("api/vendor")]
[Authorize(Roles = "Vendor,Admin")]
public class VendorScriptController : ControllerBase
{
    private readonly AppDbContext _db;

    public VendorScriptController(AppDbContext db)
    {
        _db = db;
    }

    [HttpPost("submit-script")]
    public async Task<IActionResult> SubmitScript([FromBody] SubmitScriptRequestDto dto)
    {
        if (dto.PoiId <= 0 || string.IsNullOrWhiteSpace(dto.NewScript))
            return BadRequest(new { success = false, message = "Invalid request payload." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new { success = false, message = "Unauthenticated." });

        var poi = await _db.Pois.FirstOrDefaultAsync(p => p.Id == dto.PoiId);
        if (poi == null)
            return NotFound(new { success = false, message = "POI not found." });

        var isAdmin = User.IsInRole("Admin");
        if (!isAdmin && !string.Equals(poi.VendorId, userId, StringComparison.Ordinal))
            return Forbid();

        var req = new ScriptChangeRequest
        {
            PoiId = dto.PoiId,
            LanguageCode = string.IsNullOrWhiteSpace(dto.LanguageCode) ? "en" : dto.LanguageCode.Trim().ToLowerInvariant(),
            NewScript = dto.NewScript.Trim(),
            Status = "pending",
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        _db.ScriptChangeRequests.Add(req);
        await _db.SaveChangesAsync();

        return Ok(new { success = true, requestId = req.Id, status = req.Status });
    }
}

public class SubmitScriptRequestDto
{
    public int PoiId { get; set; }
    public string LanguageCode { get; set; } = "en";
    public string NewScript { get; set; } = string.Empty;
}
