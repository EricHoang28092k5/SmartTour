using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity; // Bơm thêm Identity
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourBackend.Data;

namespace SmartTourCMS.Controllers
{
    [Authorize(Roles = "Admin,Vendor")]
    public class TranslationController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<IdentityUser> _userManager; // 1. Bơm UserManager

        public TranslationController(AppDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // --- 1. Trang danh sách bản dịch của 1 địa điểm cụ thể ---
        public async Task<IActionResult> Index(int poiId)
        {
            var poi = await _context.Pois.FindAsync(poiId);
            if (poi == null) return NotFound();

            // BẢO MẬT: Phải là Admin hoặc đúng chủ của POI mới được xem danh sách dịch
            var user = await _userManager.GetUserAsync(User);
            if (!await _userManager.IsInRoleAsync(user, "Admin") && poi.VendorId != user.Id)
            {
                return Forbid();
            }

            ViewBag.PoiName = poi.Name;
            ViewBag.PoiId = poiId;

            var translations = await _context.PoiTranslations
                .Include(t => t.Language)
                .Where(t => t.PoiId == poiId)
                .ToListAsync();
            return View(translations);
        }

        // --- 2. Form thêm bản dịch mới (GET) ---
        public async Task<IActionResult> Create(int poiId)
        {
            var poi = await _context.Pois.FindAsync(poiId);
            if (poi == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            if (!await _userManager.IsInRoleAsync(user, "Admin") && poi.VendorId != user.Id) return Forbid();

            ViewBag.PoiId = poiId;
            ViewBag.PoiName = poi.Name;
            ViewBag.Languages = await _context.Languages.ToListAsync();
            return View();
        }

        // --- 3. Xử lý thêm bản dịch mới (POST) ---
        [HttpPost]
        [ValidateAntiForgeryToken] // Thêm bảo mật chống giả mạo
        public async Task<IActionResult> Create(PoiTranslation translation)
        {
            var poi = await _context.Pois.FindAsync(translation.PoiId);
            if (poi == null) return NotFound();

            // BẢO MẬT: Tránh hacker chọc ngoáy truyền poiId của người khác vào form
            var user = await _userManager.GetUserAsync(User);
            if (!await _userManager.IsInRoleAsync(user, "Admin") && poi.VendorId != user.Id) return Forbid();

            if (ModelState.IsValid)
            {
                _context.PoiTranslations.Add(translation);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index), new { poiId = translation.PoiId });
            }

            // Nếu lỗi, nạp lại ViewBag
            ViewBag.PoiName = poi.Name;
            ViewBag.PoiId = translation.PoiId;
            ViewBag.Languages = await _context.Languages.ToListAsync();

            return View(translation);
        }

        // --- 4. Xem chi tiết ---
        public async Task<IActionResult> Details(int id)
        {
            var translation = await _context.PoiTranslations
                .Include(t => t.Language)
                .Include(t => t.Poi) // Include POI để lấy được VendorId
                .FirstOrDefaultAsync(t => t.Id == id);

            if (translation == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            if (!await _userManager.IsInRoleAsync(user, "Admin") && translation.Poi.VendorId != user.Id) return Forbid();

            return View(translation);
        }

        // --- 5. SỬA (Giao diện GET) ---
        public async Task<IActionResult> Edit(int id)
        {
            var translation = await _context.PoiTranslations.Include(t => t.Poi).FirstOrDefaultAsync(t => t.Id == id);
            if (translation == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            if (!await _userManager.IsInRoleAsync(user, "Admin") && translation.Poi.VendorId != user.Id) return Forbid();

            ViewBag.PoiName = translation.Poi.Name;
            return View(translation);
        }

        // --- 6. SỬA (Xử lý lưu POST) ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, PoiTranslation translation)
        {
            if (id != translation.Id) return NotFound();

            var existingTranslation = await _context.PoiTranslations.Include(t => t.Poi).AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
            if (existingTranslation == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            if (!await _userManager.IsInRoleAsync(user, "Admin") && existingTranslation.Poi.VendorId != user.Id) return Forbid();

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

        // --- 7. XÓA BẢN DỊCH ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id, int poiId)
        {
            var translation = await _context.PoiTranslations.Include(t => t.Poi).FirstOrDefaultAsync(t => t.Id == id);
            if (translation == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            if (!await _userManager.IsInRoleAsync(user, "Admin") && translation.Poi.VendorId != user.Id) return Forbid();

            _context.PoiTranslations.Remove(translation);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), new { poiId = poiId });
        }
    }
}