using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourBackend.Data;
using System.Globalization; // Bắt buộc phải có để lột dấu
using System.Text; // Bắt buộc phải có cho StringBuilder
using System.Text.Json;
using X.PagedList.Extensions;

namespace SmartTourCMS.Controllers
{
    [Authorize(Roles = "Admin,Vendor")]
    public class FoodController : Controller
    {
        private readonly AppDbContext _context;
        private readonly Cloudinary _cloudinary;
        private readonly UserManager<IdentityUser> _userManager;
        private static readonly HttpClient _httpClient = new HttpClient();

        public FoodController(AppDbContext context, Cloudinary cloudinary, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _cloudinary = cloudinary;
            _userManager = userManager;
        }

        // --- 1. DANH SÁCH MÓN ĂN (ĐÃ SỬA TÌM KIẾM KHÔNG DẤU) ---
       // Hiển thị danh sách Món ăn, kèm theo ti tỉ thứ như tìm kiếm, lọc, phân trang.
        public async Task<IActionResult> Index(string? search, int? poiId, int? page)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");

            // Dọn orphan records
            var orphanFoodIds = await _context.Food
                .Where(f => !_context.Pois.Any(p => p.Id == f.PoiId))
                .Select(f => f.Id)
                .ToListAsync();
            if (orphanFoodIds.Count > 0)
            {
                var orphanTrans = _context.FoodTranslations.Where(t => orphanFoodIds.Contains(t.FoodId));
                var orphanFoods = _context.Food.Where(f => orphanFoodIds.Contains(f.Id));
                _context.FoodTranslations.RemoveRange(orphanTrans);
                _context.Food.RemoveRange(orphanFoods);
                await _context.SaveChangesAsync();
            }

            var query = _context.Food.Include(f => f.Poi).AsQueryable();

            if (!isAdmin)
            {
                query = query.Where(f => f.Poi != null && f.Poi.VendorId == user.Id);
            }

            // Lọc theo POI TRƯỚC khi kéo về RAM cho nhẹ
            if (poiId.HasValue && poiId.Value > 0)
            {
                query = query.Where(f => f.PoiId == poiId.Value);
            }

            // Ép nó lấy dữ liệu ra List trước (để C# so sánh không dấu)
            var foodList = await query.OrderByDescending(f => f.Id).ToListAsync();

            // --- XỬ LÝ TÌM KIẾM KHÔNG DẤU BẰNG C# ---
            if (!string.IsNullOrWhiteSpace(search))
            {
                var keyword = RemoveDiacritics(search.Trim()).ToLower();

                foodList = foodList.Where(f =>
                    (f.Name != null && RemoveDiacritics(f.Name).ToLower().Contains(keyword)) ||
                    (f.Description != null && RemoveDiacritics(f.Description).ToLower().Contains(keyword))
                ).ToList();
            }

            int pageSize = 10;
            int pageNumber = page ?? 1;

            // Cắt trang trên cái list đã lột dấu
            var pagedFoods = foodList.ToPagedList(pageNumber, pageSize);

            // Dropdown POI cho bộ lọc
            var poiQuery = _context.Pois.AsQueryable();
            if (!isAdmin)
            {
                poiQuery = poiQuery.Where(p => p.VendorId == user.Id);
            }
            var pois = await poiQuery.OrderBy(p => p.Name).ToListAsync();

            ViewBag.PoiFilterList = new SelectList(pois, "Id", "Name", poiId);
            ViewBag.CurrentSearch = search;
            ViewBag.CurrentPoiId = poiId;

            return View(pagedFoods);
        }

        // --- 2. GIAO DIỆN TẠO MÓN MỚI (GET) ---
        public async Task<IActionResult> Create()
        {
            var user = await _userManager.GetUserAsync(User);
            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");

            var pois = isAdmin
                ? await _context.Pois.ToListAsync()
                : await _context.Pois.Where(p => p.VendorId == user.Id).ToListAsync();

            ViewBag.PoiList = new SelectList(pois, "Id", "Name");
            return View();
        }

        // --- 3. XỬ LÝ LƯU MÓN ĂN MỚI (POST) ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Food food, IFormFile? imageFile)
        {
            var user = await _userManager.GetUserAsync(User);
            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");

            var poi = await _context.Pois.FindAsync(food.PoiId);
            if (poi == null || (!isAdmin && poi.VendorId != user.Id))
            {
                return Forbid();
            }

            if (imageFile != null && imageFile.Length > 0)
            {
                var uploadParams = new ImageUploadParams()
                {
                    File = new FileDescription(imageFile.FileName, imageFile.OpenReadStream()),
                    Folder = "SmartTour/Foods"
                };
                var uploadResult = await _cloudinary.UploadAsync(uploadParams);
                food.ImageUrl = uploadResult.SecureUrl.ToString();
            }

            _context.Food.Add(food);
            await _context.SaveChangesAsync();

            await UpsertFoodTranslationsAsync(food);

            TempData["success"] = "Thêm món ăn thành công!";
            return RedirectToAction(nameof(Index));
        }

        // --- 4. XÓA MÓN ĂN ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var food = await _context.Food.Include(f => f.Poi).FirstOrDefaultAsync(f => f.Id == id);
            if (food == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");

            if (!isAdmin && (food.Poi == null || food.Poi.VendorId != user.Id))
            {
                return Forbid();
            }

            var trans = _context.FoodTranslations.Where(t => t.FoodId == id);
            _context.FoodTranslations.RemoveRange(trans);
            _context.Food.Remove(food);
            await _context.SaveChangesAsync();

            TempData["success"] = "Đã xóa món ăn khỏi Menu!";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegenerateTranslations(int id)
        {
            var food = await _context.Food
                .Include(f => f.Poi)
                .FirstOrDefaultAsync(f => f.Id == id);
            if (food == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            if (!isAdmin && (food.Poi == null || food.Poi.VendorId != user.Id))
                return Forbid();

            await UpsertFoodTranslationsAsync(food);
            TempData["success"] = $"Đã tạo lại bản dịch Food cho \"{food.Name}\".";
            return RedirectToAction(nameof(Index));
        }

        // --- 5. SỬA MÓN ĂN (GET) ---
        [HttpGet]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var food = await _context.Food.Include(f => f.Poi).FirstOrDefaultAsync(f => f.Id == id);
            if (food == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");

            if (!isAdmin && food.Poi.VendorId != user.Id) return Forbid();

            var pois = isAdmin
                ? await _context.Pois.ToListAsync()
                : await _context.Pois.Where(p => p.VendorId == user.Id).ToListAsync();
            ViewBag.PoiList = new SelectList(pois, "Id", "Name", food.PoiId);
            await PopulateFoodTranslationViewBag(food.Id);

            return View(food);
        }

        // --- 6. SỬA MÓN ĂN (POST) ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Food food, IFormFile? imageFile, IFormCollection form)
        {
            if (id != food.Id) return NotFound();

            var existingFood = await _context.Food.Include(f => f.Poi).AsNoTracking().FirstOrDefaultAsync(f => f.Id == id);
            if (existingFood == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");

            if (!isAdmin && existingFood.Poi.VendorId != user.Id) return Forbid();

            if (imageFile != null && imageFile.Length > 0)
            {
                var uploadParams = new ImageUploadParams()
                {
                    File = new FileDescription(imageFile.FileName, imageFile.OpenReadStream()),
                    Folder = "SmartTour/Foods"
                };
                var uploadResult = await _cloudinary.UploadAsync(uploadParams);
                food.ImageUrl = uploadResult.SecureUrl.ToString();
            }
            else
            {
                food.ImageUrl = existingFood.ImageUrl;
            }

            try
            {
                _context.Update(food);
                await _context.SaveChangesAsync();
                await UpsertFoodTranslationsAsync(food);
                TempData["success"] = "Cập nhật thông tin món ăn thành công!";
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Lỗi: " + ex.Message);
                await PopulateFoodTranslationViewBag(food.Id);
                return View(food);
            }

            await ApplyManualTranslationOverridesAsync(food.Id, form);
            return RedirectToAction(nameof(Index));
        }

        // --- HELPER METHODS ---

        private async Task PopulateFoodTranslationViewBag(int foodId)
        {
            var langs = await _context.Languages.OrderBy(l => l.Id).ToListAsync();
            var trans = await _context.FoodTranslations
                .Where(t => t.FoodId == foodId)
                .ToListAsync();

            ViewBag.FoodLangs = langs;
            ViewBag.FoodTransMap = trans.ToDictionary(t => t.LanguageId, t => t);
        }

        private async Task ApplyManualTranslationOverridesAsync(int foodId, IFormCollection form)
        {
            var langs = await _context.Languages.OrderBy(l => l.Id).ToListAsync();
            if (langs.Count == 0) return;

            var all = await _context.FoodTranslations
                .Where(t => t.FoodId == foodId)
                .ToListAsync();

            var changed = false;
            foreach (var lang in langs)
            {
                var nameKey = $"trans_name_{lang.Id}";
                var descKey = $"trans_desc_{lang.Id}";

                var manualName = form[nameKey].ToString().Trim();
                var manualDesc = form[descKey].ToString().Trim();
                if (string.IsNullOrWhiteSpace(manualName) && string.IsNullOrWhiteSpace(manualDesc))
                    continue;

                var row = all.FirstOrDefault(x => x.LanguageId == lang.Id);
                if (row == null)
                {
                    row = new FoodTranslation
                    {
                        FoodId = foodId,
                        LanguageId = lang.Id,
                        Name = string.IsNullOrWhiteSpace(manualName) ? string.Empty : manualName,
                        Description = string.IsNullOrWhiteSpace(manualDesc) ? string.Empty : manualDesc,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.FoodTranslations.Add(row);
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(manualName))
                        row.Name = manualName;
                    if (!string.IsNullOrWhiteSpace(manualDesc))
                        row.Description = manualDesc;
                    row.UpdatedAt = DateTime.UtcNow;
                }

                changed = true;
            }

            if (changed)
                await _context.SaveChangesAsync();
        }

        private async Task UpsertFoodTranslationsAsync(Food food)
        {
            var languages = await _context.Languages.OrderBy(l => l.Id).ToListAsync();
            if (languages.Count == 0) return;

            var sourceLang = ResolveSourceLanguageCode(languages);
            var existing = await _context.FoodTranslations
                .Where(x => x.FoodId == food.Id)
                .ToListAsync();

            foreach (var lang in languages)
            {
                var targetLang = NormalizeTranslateLanguageCode(lang.Code);
                var translatedName = await TranslateIfNeededAsync(food.Name ?? string.Empty, sourceLang, targetLang);
                var translatedDesc = await TranslateIfNeededAsync(food.Description ?? string.Empty, sourceLang, targetLang);

                var row = existing.FirstOrDefault(x => x.LanguageId == lang.Id);
                if (row == null)
                {
                    _context.FoodTranslations.Add(new FoodTranslation
                    {
                        FoodId = food.Id,
                        LanguageId = lang.Id,
                        Name = translatedName,
                        Description = translatedDesc,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    row.Name = translatedName;
                    row.Description = translatedDesc;
                    row.UpdatedAt = DateTime.UtcNow;
                }

                await Task.Delay(250); // Chống block Google Translate
            }

            await _context.SaveChangesAsync();
        }

        private static string ResolveSourceLanguageCode(IReadOnlyList<Language> languages)
        {
            if (languages.Count == 0) return "vi";
            var vi = languages.FirstOrDefault(l => NormalizeTranslateLanguageCode(l.Code) == "vi");
            if (vi != null) return "vi";
            return NormalizeTranslateLanguageCode(languages[0].Code);
        }

        private static string NormalizeTranslateLanguageCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "en";
            var normalized = code.Trim().ToLowerInvariant();
            var dashIndex = normalized.IndexOf('-');
            return dashIndex > 0 ? normalized[..dashIndex] : normalized;
        }

        private async Task<string> TranslateIfNeededAsync(string text, string sourceLang, string targetLang)
        {
            if (string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;
            if (string.Equals(sourceLang, targetLang, StringComparison.OrdinalIgnoreCase))
                return text;
            return await AutoTranslateAsync(text, sourceLang, targetLang);
        }

        private async Task<string> AutoTranslateAsync(string text, string sourceLang, string targetLang)
        {
            if (string.Equals(sourceLang, targetLang, StringComparison.OrdinalIgnoreCase))
                return text;
            try
            {
                var res = await _httpClient.GetStringAsync(
                    $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={Uri.EscapeDataString(sourceLang)}&tl={Uri.EscapeDataString(targetLang)}&dt=t&q={Uri.EscapeDataString(text)}");
                using var doc = JsonDocument.Parse(res);
                return doc.RootElement[0][0][0].GetString() ?? text;
            }
            catch
            {
                return text;
            }
        }

        // --- HÀM LỘT DẤU TIẾNG VIỆT ---
        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder();

            foreach (var c in normalizedString)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC)
                .Replace("đ", "d").Replace("Đ", "D");
        }
    }
}