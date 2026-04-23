using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourBackend.Data;
using SmartTourBackend.Services;

namespace SmartTourBackend.Controllers;

[ApiController]
[Route("api/admin/script-requests")]
[Authorize(Roles = "Admin")]
public class AdminScriptRequestsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAudioPipelineQueue _queue;
    private readonly IAdminKeyValidator _adminKeyValidator;

    public AdminScriptRequestsController(AppDbContext db, IAudioPipelineQueue queue, IAdminKeyValidator adminKeyValidator)
    {
        _db = db;
        _queue = queue;
        _adminKeyValidator = adminKeyValidator;
    }

    [HttpGet("pending")]
    public async Task<IActionResult> GetPending()
    {
        if (!_adminKeyValidator.IsValid(HttpContext))
            return Unauthorized(new { success = false, message = "Invalid X-Admin-Key" });

        var data = await _db.ScriptChangeRequests
            .Where(x => x.Status == "pending")
            .OrderBy(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.PoiId,
                x.LanguageCode,
                x.NewScript,
                x.CreatedByUserId,
                x.CreatedAt
            })
            .ToListAsync();

        return Ok(new { success = true, data });
    }

    [HttpPost("{id:long}/approve")]
    public async Task<IActionResult> Approve(long id)
    {
        if (!_adminKeyValidator.IsValid(HttpContext))
            return Unauthorized(new { success = false, message = "Invalid X-Admin-Key" });

        var req = await _db.ScriptChangeRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (req == null) return NotFound(new { success = false, message = "Request not found." });
        if (!string.Equals(req.Status, "pending", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { success = false, message = "Request is not pending." });

        var translation = await _db.PoiTranslations
            .Include(t => t.Language)
            .FirstOrDefaultAsync(t => t.PoiId == req.PoiId && t.Language != null && t.Language.Code.ToLower() == req.LanguageCode.ToLower());

        if (translation == null)
            return BadRequest(new { success = false, message = "No translation found for requested language." });

        translation.TtsScript = req.NewScript;
        req.Status = "approved";
        req.ReviewedAt = DateTime.UtcNow;
        req.ReviewedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var jobId = await _queue.EnqueueAsync(
            "tts_only",
            new AudioJobPayload(req.NewScript, req.LanguageCode, req.PoiId, translation.Id));

        await _db.SaveChangesAsync();

        return Ok(new { success = true, requestId = req.Id, jobId });
    }

    [HttpPost("{id:long}/reject")]
    public async Task<IActionResult> Reject(long id, [FromBody] RejectScriptRequestDto dto)
    {
        if (!_adminKeyValidator.IsValid(HttpContext))
            return Unauthorized(new { success = false, message = "Invalid X-Admin-Key" });

        var req = await _db.ScriptChangeRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (req == null) return NotFound(new { success = false, message = "Request not found." });
        if (!string.Equals(req.Status, "pending", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { success = false, message = "Request is not pending." });

        req.Status = "rejected";
        req.RejectReason = dto.Reason?.Trim();
        req.ReviewedAt = DateTime.UtcNow;
        req.ReviewedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await _db.SaveChangesAsync();

        return Ok(new { success = true, requestId = req.Id });
    }
}

public class RejectScriptRequestDto
{
    public string? Reason { get; set; }
}
