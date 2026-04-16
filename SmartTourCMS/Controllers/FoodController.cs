using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering; // Dùng cho cái Dropdown chọn Quán
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourBackend.Data;
using System.Text.Json;
using X.PagedList.Extensions;
namespace SmartTourCMS.Controllers
{
    // Bắt buộc phải đăng nhập và có quyền Admin hoặc Vendor mới được vào
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

        // --- 1. DANH SÁCH MÓN ĂN ---
        public async Task<IActionResult> Index(string? search, int? poiId, int? page)
        {
            var user = await _userManager.GetUserAsync(User);

            // Thêm dòng này để chống lỗi văng app nếu rớt phiên đăng nhập
            if (user == null) return RedirectToAction("Login", "Account");

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");

            // Kéo danh sách món ăn, lôi luôn thằng Bố (Poi) lên để lấy tên Quán
            var query = _context.Food.Include(f => f.Poi).AsQueryable();

            if (!isAdmin)
            {
                // LƯỚI BẢO MẬT: Vendor nào chỉ nhìn thấy Menu của Vendor đó
                query = query.Where(f => f.Poi != null && f.Poi.VendorId == user.Id);
            }

            // Tìm kiếm theo tên món
            if (!string.IsNullOrWhiteSpace(search))
            {
                var keyword = search.Trim();
                query = query.Where(f =>
                    EF.Functions.Like(f.Name, $"%{keyword}%") ||
                    EF.Functions.Like(f.Description, $"%{keyword}%"));
            }

            // Lọc theo POI
            if (poiId.HasValue && poiId.Value > 0)
            {
                query = query.Where(f => f.PoiId == poiId.Value);
            }

            // --- PHẦN CODE THÊM VÀO ĐỂ PHÂN TRANG ---
            // 1. Sắp xếp dữ liệu (Ví dụ: Id giảm dần để món ăn mới thêm nằm trên cùng)
            query = query.OrderByDescending(f => f.Id);

            // 2. Cấu hình trang
            int pageSize = 10; // Số món ăn hiển thị trên 1 trang
            int pageNumber = page ?? 1; // Mặc định là trang 1

            // 3. Cắt trang bằng ToPagedList thay vì ToListAsync()
            var pagedFoods = query.ToPagedList(pageNumber, pageSize);

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

            // Trả về pagedFoods
            return View(pagedFoods);
        }

        // --- 2. GIAO DIỆN TẠO MÓN MỚI (GET) ---
        public async Task<IActionResult> Create()
        {
            var user = await _userManager.GetUserAsync(User);
            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");

            // Lấy danh sách các Quán (Poi) của ông Vendor này để đưa vào danh sách xổ xuống (Dropdown)
            var pois = isAdmin 
                ? await _context.Pois.ToListAsync() 
                : await _context.Pois.Where(p => p.VendorId == user.Id).ToListAsync();

            // Đẩy danh sách này sang View qua ViewBag
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

            // KIỂM TRA BẢO MẬT KÉP: Chặn việc ông Vendor A F12 hack web để thêm món vào Quán của ông Vendor B
            var poi = await _context.Pois.FindAsync(food.PoiId);
            if (poi == null || (!isAdmin && poi.VendorId != user.Id))
            {
                return Forbid(); // Sai chủ là đuổi cổ ngay
            }

            // Upload 1 ảnh duy nhất lên Cloudinary
            if (imageFile != null && imageFile.Length > 0)
            {
                var uploadParams = new ImageUploadParams()
                {
                    File = new FileDescription(imageFile.FileName, imageFile.OpenReadStream()),
                    Folder = "SmartTour/Foods" // Lưu vào thư mục riêng cho gọn
                };
                var uploadResult = await _cloudinary.UploadAsync(uploadParams);
                food.ImageUrl = uploadResult.SecureUrl.ToString();
            }

            // Lưu vào Database
            _context.Food.Add(food);
            await _context.SaveChangesAsync();

            // Tạo bản dịch tên + mô tả cho tất cả ngôn ngữ hệ thống
            await UpsertFoodTranslationsAsync(food);

            TempData["success"] = "Thêm món ăn thành công!";
            return RedirectToAction(nameof(Index));
        }

        // --- 4. XÓA MÓN ĂN ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            // Tìm món ăn kèm thông tin Quán
            var food = await _context.Food.Include(f => f.Poi).FirstOrDefaultAsync(f => f.Id == id);
            if (food == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");

            // LƯỚI BẢO MẬT: Không phải Admin và không phải chủ quán thì cấm xóa
            if (!isAdmin && food.Poi.VendorId != user.Id)
            {
                return Forbid();
            }

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

            // Bảo mật: Vendor chỉ được sửa món của quán mình
            if (!isAdmin && food.Poi.VendorId != user.Id) return Forbid();

            // Đổ lại danh sách Quán cho Dropdown
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

            // Xử lý nếu người dùng up ảnh mới
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
                // Nếu không chọn ảnh mới thì giữ nguyên ảnh cũ
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

                await Task.Delay(250);
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
    }
}