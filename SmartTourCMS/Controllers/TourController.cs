using System.Net.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourBackend.Data;

namespace SmartTourCMS.Controllers
{
    [Authorize(Roles = "Admin,Vendor")]
    public class TourController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        // Khai báo thêm cái này để gọi API Google Dịch
        private static readonly HttpClient _httpClient = new HttpClient();

        public TourController(AppDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // 1. Trang danh sách Tour (Đã lọc theo Vendor)
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");

            var tours = new List<Tour>();

            if (await _userManager.IsInRoleAsync(user, "Admin"))
            {
                tours = await _context.Tours
                    .Include(t => t.TourPois)
                    .ToListAsync();
            }
            else
            {
                tours = await _context.Tours
                    .Include(t => t.TourPois)
                    .Where(t => t.VendorId == user.Id)
                    .ToListAsync();
            }

            return View(tours);
        }

        // 2. Trang Tạo mới (GET)
        [HttpGet]
        public IActionResult Create()
        {
            ViewBag.Pois = _context.Pois.ToList();
            return View();
        }

        // 3. Logic Tạo mới (POST) - ĐÃ NHỒI TÍNH NĂNG DỊCH
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Tour tour)
        {
            if (ModelState.IsValid)
            {
                // 1. Lưu Tour xuống để lấy ID trước
                _context.Tours.Add(tour);
                await _context.SaveChangesAsync();

                // 2. Lấy đống ID trong cái rổ ráp vào bảng TourPoi 
                if (tour.SelectedPoiIds != null && tour.SelectedPoiIds.Any())
                {
                    int stt = 1;

                    foreach (var poiId in tour.SelectedPoiIds)
                    {
                        var tourPoi = new TourPoi
                        {
                            TourId = tour.Id,
                            PoiId = poiId,
                            OrderIndex = stt
                        };

                        _context.TourPois.Add(tourPoi);
                        stt++;
                    }
                    await _context.SaveChangesAsync();
                }

                // ==========================================
                // 3. AUTO TRANSLATE TÊN VÀ MÔ TẢ (Anh, Hàn, Nhật)
                // ==========================================
                var targetLanguages = await _context.Languages
                                                         .Select(l => l.Code)
                                                         .ToListAsync();

                foreach (var lang in targetLanguages)
                {
                    // Lọc bỏ tiếng Việt ra đéo dịch (nếu bảng Language của mày có chứa cả tiếng Việt)
                    if (lang.ToLower() == "vi") continue;

                    // Gọi hàm dịch tự viết ở tít dưới cùng
                    var translatedName = await AutoTranslateAsync(tour.Name, lang);

                    var translatedDesc = "";
                    if (!string.IsNullOrEmpty(tour.Description))
                    {
                        translatedDesc = await AutoTranslateAsync(tour.Description, lang);
                    }

                    // Tống xuống bảng dịch
                    var translation = new TourTranslation
                    {
                        TourId = tour.Id,
                        LanguageCode = lang,
                        Name = translatedName,
                        Description = translatedDesc
                    };
                    _context.TourTranslations.Add(translation);
                }

                // Lưu đống bản dịch vào Database
                await _context.SaveChangesAsync();
                // ==========================================

                TempData["success"] = "Tạo Tour và tự động dịch thành công!";
                return RedirectToAction(nameof(Index));
            }
            return View(tour);
        }

        // 4. Chi tiết
        // 4. Chi tiết
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var tour = await _context.Tours
                .Include(t => t.TourTranslations) // BÙA KÉO BẢN DỊCH LÊN: ĐÉO CÓ DÒNG NÀY LÀ MÙ MẮT NHÉ
                .Include(t => t.TourPois.OrderBy(tp => tp.OrderIndex))
                    .ThenInclude(tp => tp.Poi)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (tour == null) return NotFound();

            return View(tour);
        }
        // 5. Sửa (GET)
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var tour = await _context.Tours.Include(t => t.TourPois).FirstOrDefaultAsync(t => t.Id == id);
            if (tour == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            if (!await _userManager.IsInRoleAsync(user, "Admin") && tour.VendorId != user.Id)
            {
                return Forbid();
            }

            ViewBag.Pois = await _context.Pois.ToListAsync();
            ViewBag.SelectedPoiIds = tour.TourPois.Select(tp => tp.PoiId).ToList();
            return View(tour);
        }

        // 6. Logic Sửa (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Tour tour, int[] selectedPoiIds)
        {
            if (id != tour.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    tour.CreatedAt = DateTime.SpecifyKind(tour.CreatedAt, DateTimeKind.Utc);
                    _context.Update(tour);

                    var oldPois = _context.TourPois.Where(tp => tp.TourId == id);
                    _context.TourPois.RemoveRange(oldPois);

                    if (selectedPoiIds != null)
                    {
                        int order = 1;
                        foreach (var poiId in selectedPoiIds)
                        {
                            _context.TourPois.Add(new TourPoi { TourId = id, PoiId = poiId, OrderIndex = order++ });
                        }
                    }
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Tours.Any(e => e.Id == tour.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(tour);
        }

        // 7. Xóa
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var tour = await _context.Tours.FindAsync(id);
            if (tour == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            if (!await _userManager.IsInRoleAsync(user, "Admin") && tour.VendorId != user.Id)
            {
                return Forbid();
            }

            var tourPois = _context.TourPois.Where(tp => tp.TourId == id);
            _context.TourPois.RemoveRange(tourPois);

            _context.Tours.Remove(tour);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // ==============================================================
        // HÀM DỊCH CHÙA GOOGLE TRANSLATE (Giống y hệt bên PoiController)
        // ==============================================================
        private async Task<string> AutoTranslateAsync(string text, string targetLanguage)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text)) return text;

                // Xài thẳng API lậu của Google, truyền tiếng Việt (vi) sang targetLanguage
                var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=vi&tl={targetLanguage}&dt=t&q={Uri.EscapeDataString(text)}";

                var response = await _httpClient.GetStringAsync(url);

                // Móc lấy chữ trong cái mảng JSON lằng nhằng của Google trả về
                using var doc = JsonDocument.Parse(response);
                var translatedText = doc.RootElement[0][0][0].GetString();

                return translatedText ?? text; // Lỗi thì trả về chữ gốc
            }
            catch
            {
                // Mất mạng hoặc Google ban IP thì nhả lại chữ gốc, đéo cho sập Web
                return text;
            }
        }
    }
}