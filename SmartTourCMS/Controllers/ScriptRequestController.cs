using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourBackend.Data;

namespace SmartTourCMS.Controllers;

[Authorize(Roles = "Admin,Vendor")]
public class ScriptRequestController : Controller
{
    private readonly AppDbContext _db;
    private readonly UserManager<IdentityUser> _userManager;

    public ScriptRequestController(AppDbContext db, UserManager<IdentityUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return RedirectToAction("Login", "Account");

        IQueryable<ScriptChangeRequest> q = _db.ScriptChangeRequests.AsQueryable();
        if (!await _userManager.IsInRoleAsync(user, "Admin"))
            q = q.Where(x => x.CreatedByUserId == user.Id);

        var data = await q.OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync();
        return View(data);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return RedirectToAction("Login", "Account");

        var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
        var pois = isAdmin
            ? await _db.Pois.OrderBy(p => p.Name).ToListAsync()
            : await _db.Pois.Where(p => p.VendorId == user.Id).OrderBy(p => p.Name).ToListAsync();
        ViewBag.Pois = pois;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int poiId, string languageCode, string newScript)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return RedirectToAction("Login", "Account");

        if (poiId <= 0 || string.IsNullOrWhiteSpace(newScript))
        {
            TempData["Error"] = "Dữ liệu không hợp lệ.";
            return RedirectToAction(nameof(Create));
        }

        var poi = await _db.Pois.FirstOrDefaultAsync(p => p.Id == poiId);
        if (poi == null) return NotFound();

        var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
        if (!isAdmin && !string.Equals(poi.VendorId, user.Id, StringComparison.Ordinal))
            return Forbid();

        _db.ScriptChangeRequests.Add(new ScriptChangeRequest
        {
            PoiId = poiId,
            LanguageCode = string.IsNullOrWhiteSpace(languageCode) ? "en" : languageCode.Trim().ToLowerInvariant(),
            NewScript = newScript.Trim(),
            Status = "pending",
            CreatedByUserId = user.Id,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã gửi yêu cầu đổi script, chờ admin duyệt.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<IActionResult> Pending()
    {
        var data = await _db.ScriptChangeRequests
            .Where(x => x.Status == "pending")
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();
        return View(data);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(long id)
    {
        var req = await _db.ScriptChangeRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (req == null) return NotFound();
        if (!string.Equals(req.Status, "pending", StringComparison.OrdinalIgnoreCase))
            return RedirectToAction(nameof(Pending));

        var translation = await _db.PoiTranslations
            .Include(t => t.Language)
            .FirstOrDefaultAsync(t => t.PoiId == req.PoiId && t.Language != null && t.Language.Code.ToLower() == req.LanguageCode.ToLower());

        if (translation == null)
        {
            TempData["Error"] = "Không tìm thấy bản dịch phù hợp ngôn ngữ để duyệt.";
            return RedirectToAction(nameof(Pending));
        }

        translation.TtsScript = req.NewScript;
        req.Status = "approved";
        req.ReviewedAt = DateTime.UtcNow;
        req.ReviewedByUserId = _userManager.GetUserId(User);

        _db.AudioPipelineJobs.Add(new AudioPipelineJob
        {
            JobType = "tts_only",
            Status = "pending",
            PoiId = req.PoiId,
            TranslationId = translation.Id,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(
                new { Script = req.NewScript, LanguageCode = req.LanguageCode, PoiId = req.PoiId, TranslationId = translation.Id }),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã duyệt yêu cầu và đưa vào hàng đợi audio.";
        return RedirectToAction(nameof(Pending));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(long id, string? reason)
    {
        var req = await _db.ScriptChangeRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (req == null) return NotFound();
        if (!string.Equals(req.Status, "pending", StringComparison.OrdinalIgnoreCase))
            return RedirectToAction(nameof(Pending));

        req.Status = "rejected";
        req.ReviewedAt = DateTime.UtcNow;
        req.ReviewedByUserId = _userManager.GetUserId(User);
        req.RejectReason = reason?.Trim();
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã từ chối yêu cầu.";
        return RedirectToAction(nameof(Pending));
    }
}
