using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity; // Bổ sung Identity
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourBackend.Data;
using System.Net.Http;

namespace SmartTourCMS.Controllers
{
    // 1. Mở cửa cho cả Admin và Vendor vào quản lý
    [Authorize(Roles = "Admin,Vendor")]
    public class PoiController : Controller
    {
        private readonly AppDbContext _context;
        private readonly Cloudinary _cloudinary;
        private readonly UserManager<IdentityUser> _userManager; // Bơm thêm UserManager
        private static readonly HttpClient _httpClient = new HttpClient();

        public PoiController(AppDbContext context, Cloudinary cloudinary, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _cloudinary = cloudinary;
            _userManager = userManager;
        }


        // --- 1. DANH SÁCH ĐỊA ĐIỂM (Lọc theo quyền) ---
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            var query = _context.Pois.Include(p => p.AudioFiles).AsQueryable();

            if (!isAdmin)
            {
                // Vendor: Chỉ lấy hàng của mình
                query = query.Where(p => p.VendorId == user.Id);
            }

            var pois = await query.ToListAsync();

            // MA THUẬT HIỂN THỊ: Lấy danh sách Email của tất cả User để dịch cái ID loằng ngoằng ra tên cho dễ đọc
            var users = _userManager.Users.ToList();
            var userDict = users.ToDictionary(u => u.Id, u => u.Email); // Tạo từ điển Map ID -> Email

            ViewBag.VendorDict = userDict;
            ViewBag.IsAdmin = isAdmin; // Báo cho View biết ông này là Admin để hiện cột

            return View(pois);
        }

        // --- 2. TẠO MỚI (GET) ---
        public IActionResult Create() => View();

        // --- 3. TẠO MỚI (POST) ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Poi poi, IFormFile imageFile)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user != null)
            {
                // Tự động đóng dấu Sổ đỏ cho ông đang tạo
                poi.VendorId = user.Id;
            }

            // 1. Upload ảnh
            if (imageFile != null && imageFile.Length > 0)
            {
                var uploadParams = new ImageUploadParams()
                {
                    File = new FileDescription(imageFile.FileName, imageFile.OpenReadStream()),
                    Folder = "SmartTour/Pois"
                };
                var uploadResult = await _cloudinary.UploadAsync(uploadParams);
                poi.ImageUrl = uploadResult.SecureUrl.ToString();
            }

            // 2. Lưu POI gốc
            _context.Pois.Add(poi);
            await _context.SaveChangesAsync();

            // 3. TỰ ĐỘNG DỊCH ĐA NGÔN NGỮ
            try
            {
                var allLanguages = await _context.Languages.ToListAsync();
                foreach (var lang in allLanguages)
                {
                    string translatedTitle, translatedDescription, translatedTts;

                    string baseDescription = $"Chào mừng bạn đến với {poi.Name}. Đây là một địa điểm du lịch tuyệt vời.";
                    string baseTts = $"Chào mừng bạn đến với {poi.Name}. Chúc bạn có một chuyến tham quan vui vẻ!";

                    if (lang.Code.ToLower() == "vi")
                    {
                        translatedTitle = poi.Name;
                        translatedDescription = baseDescription;
                        translatedTts = baseTts;
                    }
                    else
                    {
                        translatedTitle = await AutoTranslateAsync(poi.Name, lang.Code.ToLower());
                        translatedDescription = await AutoTranslateAsync(baseDescription, lang.Code.ToLower());
                        translatedTts = await AutoTranslateAsync(baseTts, lang.Code.ToLower());
                    }

                    var translation = new PoiTranslation
                    {
                        PoiId = poi.Id,
                        LanguageId = lang.Id,
                        Title = translatedTitle,
                        Description = translatedDescription,
                        TtsScript = translatedTts
                    };
                    _context.PoiTranslations.Add(translation);
                }
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Lỗi dịch đa ngôn ngữ: " + ex.Message);
            }

            return RedirectToAction(nameof(Index));
        }

        private async Task<string> AutoTranslateAsync(string text, string targetLang)
        {
            try
            {
                var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=vi&tl={targetLang}&dt=t&q={Uri.EscapeDataString(text)}";
                var response = await _httpClient.GetStringAsync(url);
                var parts = response.Split('"');
                return parts.Length > 1 ? parts[1] : text;
            }
            catch
            {
                return text;
            }
        }

        // --- 4. SỬA ĐỊA ĐIỂM (GET) ---
        [HttpGet]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var poi = await _context.Pois.FindAsync(id);
            if (poi == null) return NotFound();

            // BẢO MẬT: Check xem có phải chủ nhà không
            var user = await _userManager.GetUserAsync(User);
            if (!await _userManager.IsInRoleAsync(user, "Admin") && poi.VendorId != user.Id)
            {
                return Forbid();
            }

            return View(poi);
        }

        // --- 5. SỬA ĐỊA ĐIỂM (POST) ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Poi poi, IFormFile? imageFile)
        {
            if (id != poi.Id) return NotFound();

            var existingPoi = await _context.Pois.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
            if (existingPoi == null) return NotFound();

            // BẢO MẬT CẤP 2
            var user = await _userManager.GetUserAsync(User);
            if (!await _userManager.IsInRoleAsync(user, "Admin") && existingPoi.VendorId != user.Id)
            {
                return Forbid();
            }

            if (imageFile != null && imageFile.Length > 0)
            {
                var uploadParams = new ImageUploadParams()
                {
                    File = new FileDescription(imageFile.FileName, imageFile.OpenReadStream()),
                    Folder = "SmartTour/Pois"
                };
                var uploadResult = await _cloudinary.UploadAsync(uploadParams);
                poi.ImageUrl = uploadResult.SecureUrl.ToString();
            }
            else
            {
                poi.ImageUrl = existingPoi.ImageUrl;
            }

            try
            {
                // Giữ nguyên VendorId cũ kẻo bị mất
                poi.VendorId = existingPoi.VendorId;

                _context.Update(poi);
                await _context.SaveChangesAsync();
                TempData["success"] = "Cập nhật thành công!";
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Lỗi: " + ex.Message);
                return View(poi);
            }

            return RedirectToAction(nameof(Index));
        }

        // --- 6. XÓA ĐỊA ĐIỂM ---
        // --- 6. XÓA ĐỊA ĐIỂM (Phiên bản nhổ cỏ tận gốc) ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            // Bơm thêm Include để lôi đầu đám "con cái" (Audio) lên
            var poi = await _context.Pois
                .Include(p => p.AudioFiles)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (poi == null) return NotFound();

            // BẢO MẬT: Chặn xóa lén
            var user = await _userManager.GetUserAsync(User);
            if (!await _userManager.IsInRoleAsync(user, "Admin") && poi.VendorId != user.Id)
            {
                return Forbid();
            }

            try
            {
                // BƯỚC 1: Xóa sạch các Bản dịch (PoiTranslations) của địa điểm này
                var translations = _context.PoiTranslations.Where(t => t.PoiId == id);
                _context.PoiTranslations.RemoveRange(translations);

                // BƯỚC 2: Xóa sạch các File âm thanh (AudioFiles) (nếu có)
                if (poi.AudioFiles != null && poi.AudioFiles.Any())
                {
                    _context.AudioFiles.RemoveRange(poi.AudioFiles);
                }

                // BƯỚC 3: Nếu POI này đang nằm trong Tour nào đó, xóa luôn liên kết TourPoi
                // (Bỏ comment 2 dòng dưới nếu bác có bảng TourPoi)
                // var tourPois = _context.TourPois.Where(tp => tp.PoiId == id);
                // _context.TourPois.RemoveRange(tourPois);

                // BƯỚC 4: Cuối cùng mới "trảm" thằng POI gốc
                _context.Pois.Remove(poi);

                // Lưu một cục xuống DB
                await _context.SaveChangesAsync();
                TempData["success"] = "Đã dọn dẹp sạch sẽ và xóa địa điểm thành công!";
            }
            catch (Exception ex)
            {
                TempData["error"] = "Lỗi khi xóa: " + ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        // --- 7. ADMIN GIAO ĐỊA ĐIỂM CHO VENDOR ---
        [Authorize(Roles = "Admin")] // Hàm này ĐỘC QUYỀN cho Admin
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignToVendor(int poiId, string vendorEmail)
        {
            var poi = await _context.Pois.FindAsync(poiId);

            // Tìm ông Vendor trong hệ thống bằng Email
            var vendorUser = await _userManager.FindByEmailAsync(vendorEmail);

            if (poi != null && vendorUser != null)
            {
                // Cập nhật Sổ đỏ (VendorId) sang cho ông Vendor mới
                poi.VendorId = vendorUser.Id;
                await _context.SaveChangesAsync();
                TempData["success"] = $"Đã gán địa điểm cho {vendorEmail} quản lý!";
            }
            else
            {
                TempData["error"] = "Không tìm thấy địa điểm hoặc Email Vendor không tồn tại trong hệ thống!";
            }
            return RedirectToAction("Index");
        }
    }
}