using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourBackend.Data;
using System.Net.Http;

namespace SmartTourCMS.Controllers
{
    [Authorize(Roles = "Admin")]
    public class PoiController : Controller
    {
        private readonly AppDbContext _context;
        private readonly Cloudinary _cloudinary;
        private static readonly HttpClient _httpClient = new HttpClient(); // Dùng chung để tiết kiệm tài nguyên

        public PoiController(AppDbContext context, Cloudinary cloudinary)
        {
            _context = context;
            _cloudinary = cloudinary;
        }

        public async Task<IActionResult> Index()
        {
            var pois = await _context.Pois
                .Include(p => p.AudioFiles)
                .ToListAsync();
            return View(pois);
        }

        public IActionResult Create() => View();

        [HttpPost]
        public async Task<IActionResult> Create(Poi poi, IFormFile imageFile)
        {
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
            // 3. TỰ ĐỘNG DỊCH ĐA NGÔN NGỮ (Dịch cả Tiêu đề và Mô tả)
            // 3. TỰ ĐỘNG DỊCH ĐA NGÔN NGỮ (Kèm câu chào mừng)
            try
            {
                var allLanguages = await _context.Languages.ToListAsync();
                foreach (var lang in allLanguages)
                {
                    string translatedTitle;
                    string translatedDescription;
                    string translatedTts;

                    // Câu gốc tiếng Việt để đem đi dịch
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
                        // Dịch Tiêu đề
                        translatedTitle = await AutoTranslateAsync(poi.Name, lang.Code.ToLower());
                        // Dịch Mô tả
                        translatedDescription = await AutoTranslateAsync(baseDescription, lang.Code.ToLower());
                        // Dịch Kịch bản Audio (TTS)
                        translatedTts = await AutoTranslateAsync(baseTts, lang.Code.ToLower());
                    }

                    var translation = new PoiTranslation
                    {
                        PoiId = poi.Id,
                        LanguageId = lang.Id,
                        Title = translatedTitle,
                        Description = translatedDescription,
                        TtsScript = translatedTts // <--- Giờ kịch bản audio cũng được dịch siêu mượt
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

        [HttpGet]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var poi = await _context.Pois.FindAsync(id);
            if (poi == null) return NotFound();
            return View(poi);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(int id, Poi poi, IFormFile? imageFile)
        {
            if (id != poi.Id) return NotFound();

            var existingPoi = await _context.Pois.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
            if (existingPoi == null) return NotFound();

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

        public async Task<IActionResult> Delete(int id)
        {
            var poi = await _context.Pois.FindAsync(id);
            if (poi != null)
            {
                _context.Pois.Remove(poi);
                await _context.SaveChangesAsync();
                TempData["success"] = "Xóa thành công!";
            }
            return RedirectToAction(nameof(Index));
        }
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> AssignToVendor(int poiId, string vendorEmail)
        {
            var poi = await _context.Pois.FindAsync(poiId);
            if (poi != null)
            {
                poi.CreatedBy = vendorEmail; // Chuyển quyền sở hữu sang Vendor
                await _context.SaveChangesAsync();
                TempData["success"] = $"Đã gán địa điểm cho {vendorEmail} quản lý!";
            }
            return RedirectToAction("Index");
        }
    }
}