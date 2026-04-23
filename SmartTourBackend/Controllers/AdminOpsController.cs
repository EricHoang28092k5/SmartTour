using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourBackend.Data;
using SmartTourBackend.Services;

namespace SmartTourBackend.Controllers;

[ApiController]
[Route("api/admin/ops")]
[Authorize(Roles = "Admin")]
public class AdminOpsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAdminKeyValidator _adminKeyValidator;

    public AdminOpsController(AppDbContext db, IAdminKeyValidator adminKeyValidator)
    {
        _db = db;
        _adminKeyValidator = adminKeyValidator;
    }

    [HttpGet("jobs/queue")]
    public async Task<IActionResult> GetQueueSummary()
    {
        if (!_adminKeyValidator.IsValid(HttpContext))
            return Unauthorized(new { success = false, message = "Invalid X-Admin-Key" });

        var pending = await _db.AudioPipelineJobs.CountAsync(x => x.Status == "pending");
        var processing = await _db.AudioPipelineJobs.CountAsync(x => x.Status == "processing");
        var retrying = await _db.AudioPipelineJobs.CountAsync(x => x.Status == "retrying");
        var deadLetter = await _db.AudioPipelineJobs.CountAsync(x => x.Status == "dead_letter");

        return Ok(new
        {
            success = true,
            pending,
            processing,
            retrying,
            deadLetter
        });
    }

    [HttpGet("jobs/recent")]
    public async Task<IActionResult> GetRecentJobs([FromQuery] int take = 50)
    {
        if (!_adminKeyValidator.IsValid(HttpContext))
            return Unauthorized(new { success = false, message = "Invalid X-Admin-Key" });

        if (take <= 0 || take > 200) take = 50;

        var rows = await _db.AudioPipelineJobs
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .Select(x => new
            {
                x.Id,
                x.JobType,
                x.Status,
                x.PoiId,
                x.TranslationId,
                x.RetryCount,
                x.MaxRetries,
                x.CreatedAt,
                x.UpdatedAt,
                x.ProcessedAt,
                x.LastError
            })
            .ToListAsync();

        return Ok(new { success = true, data = rows });
    }
}
