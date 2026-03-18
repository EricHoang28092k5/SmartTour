using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourBackend.Data;

[Authorize(Roles = "Admin,Vendor")]
public class TranslationController : Controller
{

    private readonly AppDbContext _context;
    public TranslationController(AppDbContext context) => _context = context;

    // 1. Trang danh sách bản dịch của 1 địa điểm cụ thể
    public async Task<IActionResult> Index(int poiId)
    {
        var poi = await _context.Pois.FindAsync(poiId);
        ViewBag.PoiName = poi?.Name;
        ViewBag.PoiId = poiId;

        var translations = await _context.PoiTranslations
            .Include(t => t.Language)
            .Where(t => t.PoiId == poiId)
            .ToListAsync();
        return View(translations);
    }

    // 2. Form thêm bản dịch mới
    public async Task<IActionResult> Create(int poiId)
    {
        var poi = await _context.Pois.FindAsync(poiId);
        ViewBag.PoiId = poiId;
        ViewBag.PoiName = poi?.Name; // <--- Dòng này phải có để hiện tên gốc
        ViewBag.Languages = await _context.Languages.ToListAsync();
        return View();
    }

    [HttpPost]
    [HttpPost]
    public async Task<IActionResult> Create(PoiTranslation translation)
    {
        if (ModelState.IsValid)
        {
            _context.PoiTranslations.Add(translation);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index), new { poiId = translation.PoiId });
        }

        // --- ĐÂY LÀ CHỖ QUAN TRỌNG ĐỂ FIX LỖI ---
        // Nếu dữ liệu sai (ModelState không hợp lệ), mình phải nạp lại ViewBag trước khi trả về View
        var poi = await _context.Pois.FindAsync(translation.PoiId);
        ViewBag.PoiName = poi?.Name;
        ViewBag.PoiId = translation.PoiId;

        // Đừng quên cái <Language> thần thánh nãy anh em mình vừa fix nhé
        ViewBag.Languages = await _context.Languages.ToListAsync<Language>();

        return View(translation);
    }
    [HttpPost]
    public async Task<IActionResult> Delete(int id, int poiId)
    {
        var translation = await _context.PoiTranslations.FindAsync(id);
        if (translation != null)
        {
            _context.PoiTranslations.Remove(translation);
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index), new { poiId = poiId });
    }
    // 1. XEM CHI TIẾT
    public async Task<IActionResult> Details(int id)
    {
        var translation = await _context.PoiTranslations
            .Include(t => t.Language)
            .Include(t => t.Poi)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (translation == null) return NotFound();
        return View(translation);
    }

    // 2. SỬA (Giao diện)
    public async Task<IActionResult> Edit(int id)
    {
        var translation = await _context.PoiTranslations.FindAsync(id);
        if (translation == null) return NotFound();

        // Load lại tên địa điểm để hiển thị cho đẹp
        var poi = await _context.Pois.FindAsync(translation.PoiId);
        ViewBag.PoiName = poi?.Name;

        return View(translation);
    }

    // 3. SỬA (Xử lý lưu)
    [HttpPost]
    public async Task<IActionResult> Edit(int id, PoiTranslation translation)
    {
        if (id != translation.Id) return NotFound();

        try
        {
            _context.Update(translation);
            await _context.SaveChangesAsync();
            TempData["success"] = "Đã cập nhật bản dịch thành công!";
        }
        catch (Exception ex)
        {
            return BadRequest("Lỗi rồi bác ơi: " + ex.Message);
        }

        return RedirectToAction("Index", new { poiId = translation.PoiId });
    }
}