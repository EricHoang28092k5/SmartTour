using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourBackend.Data;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using X.PagedList;
using X.PagedList.Extensions;

namespace SmartTourCMS.Controllers
{
    [Authorize(Roles = "Admin,Vendor")]
    public class PoiController : Controller
    {
        private readonly AppDbContext _context;
        private readonly Cloudinary _cloudinary;
        private readonly UserManager<IdentityUser> _userManager;
        private static readonly HttpClient _httpClient = new HttpClient();

        public PoiController(AppDbContext context, Cloudinary cloudinary, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _cloudinary = cloudinary;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index(int? page)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");
            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            var query = _context.Pois.Include(p => p.AudioFiles).AsQueryable();
            if (!isAdmin) query = query.Where(p => p.VendorId == user.Id);
            query = query.OrderByDescending(p => p.Id);
            return View(query.ToPagedList(page ?? 1, 10));
        }

        [HttpGet]
        public IActionResult Create() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Poi poi, IFormFile imageFile, List<IFormFile> galleryFiles)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user != null) poi.VendorId = user.Id;

            // 1. Upload ảnh bìa
            if (imageFile != null && imageFile.Length > 0)
            {
                var uploadParams = new ImageUploadParams() { File = new FileDescription(imageFile.FileName, imageFile.OpenReadStream()), Folder = "SmartTour/Pois" };
                var res = await _cloudinary.UploadAsync(uploadParams);
                poi.ImageUrl = res.SecureUrl.ToString();
            }

            // =========================================================================
            // QUAN TRỌNG: GỌI AI TRƯỚC KHI LƯU ĐỂ TRÁNH LỖI NOT NULL DATABASE
            // =========================================================================
            if (string.IsNullOrWhiteSpace(poi.Description))
            {
                poi.Description = await GenerateScriptWithAI(poi.Name);
            }

            // 2. LƯU POI GỐC (Lúc này Description chắc chắn đã có chữ)
            _context.Pois.Add(poi);
            await _context.SaveChangesAsync();

            // 3. Upload Album ảnh phụ
            if (galleryFiles != null)
            {
                foreach (var file in galleryFiles)
                {
                    var uploadParams = new ImageUploadParams() { File = new FileDescription(file.FileName, file.OpenReadStream()), Folder = "SmartTour/PoiGalleries" };
                    var res = await _cloudinary.UploadAsync(uploadParams);
                    _context.PoiImages.Add(new PoiImage { PoiId = poi.Id, ImageUrl = res.SecureUrl.ToString() });
                }
                await _context.SaveChangesAsync();
            }

            // 4. DỊCH ĐA NGÔN NGỮ
            try
            {
                var languages = await _context.Languages.ToListAsync();
                foreach (var lang in languages)
                {
                    string title = (lang.Code.ToLower() == "vi") ? poi.Name : await AutoTranslateAsync(poi.Name, lang.Code.ToLower());
                    string desc = (lang.Code.ToLower() == "vi") ? poi.Description : await AutoTranslateAsync(poi.Description, lang.Code.ToLower());

                    _context.PoiTranslations.Add(new PoiTranslation
                    {
                        PoiId = poi.Id,
                        LanguageId = lang.Id,
                        Title = title,
                        Description = desc,
                        TtsScript = desc
                    });
                }
                await _context.SaveChangesAsync();
            }
            catch (Exception ex) { Console.WriteLine("Lỗi dịch: " + ex.Message); }

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var poi = await _context.Pois.Include(p => p.PoiImages).FirstOrDefaultAsync(p => p.Id == id);
            var user = await _userManager.GetUserAsync(User);
            if (poi == null || (!await _userManager.IsInRoleAsync(user, "Admin") && poi.VendorId != user.Id)) return Forbid();
            return View(poi);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Poi poi, IFormFile? imageFile, List<IFormFile>? newGalleryFiles)
        {
            if (id != poi.Id) return NotFound();
            var existing = await _context.Pois.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
            poi.ImageUrl = (imageFile != null) ? (await _cloudinary.UploadAsync(new ImageUploadParams { File = new FileDescription(imageFile.FileName, imageFile.OpenReadStream()), Folder = "SmartTour/Pois" })).SecureUrl.ToString() : existing.ImageUrl;

            try
            {
                poi.VendorId = existing.VendorId;
                _context.Update(poi);
                await _context.SaveChangesAsync();
                if (newGalleryFiles != null)
                {
                    foreach (var f in newGalleryFiles)
                    {
                        var res = await _cloudinary.UploadAsync(new ImageUploadParams { File = new FileDescription(f.FileName, f.OpenReadStream()), Folder = "SmartTour/PoiGalleries" });
                        _context.PoiImages.Add(new PoiImage { PoiId = poi.Id, ImageUrl = res.SecureUrl.ToString() });
                    }
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex) { ModelState.AddModelError("", ex.Message); return View(poi); }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var poi = await _context.Pois.Include(p => p.AudioFiles).Include(p => p.PoiImages).FirstOrDefaultAsync(p => p.Id == id);
            if (poi != null)
            {
                _context.PoiTranslations.RemoveRange(_context.PoiTranslations.Where(t => t.PoiId == id));
                _context.Pois.Remove(poi);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        private async Task<string> AutoTranslateAsync(string text, string targetLang)
        {
            try
            {
                var res = await _httpClient.GetStringAsync($"https://translate.googleapis.com/translate_a/single?client=gtx&sl=vi&tl={targetLang}&dt=t&q={Uri.EscapeDataString(text)}");
                return res.Split('"')[1];
            }
            catch { return text; }
        }

        private async Task<string> GenerateScriptWithAI(string poiName)
        {
            // THAY API KEY CỦA MÀY VÀO ĐÂY !!!
            string apiKey = "AIzaSyD5RGt4RCsZTeBM5wg0lPztxQPq04HeCJQ";
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={apiKey}";

            var payload = new
            {
                contents = new[] {
            new { parts = new[] { new { text = $"Viết thuyết minh du lịch 150 chữ cho: {poiName}. Không tiêu đề, không dùng dấu *." } } }
        }
            };

            try
            {
                var jsonPayload = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var res = await _httpClient.PostAsync(url, content);

                if (res.IsSuccessStatusCode)
                {
                    var responseBody = await res.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(responseBody);
                    return doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
                }
                else
                {
                    // ĐOẠN NÀY ĐỂ BẮT BỆNH THẰNG GOOGLE ĐÂY !!!
                    var errorBody = await res.Content.ReadAsStringAsync();
                    Console.WriteLine("==============================================");
                    Console.WriteLine("====== LỖI GOOGLE TỪ CHỐI API (Mã: " + (int)res.StatusCode + ") ======");
                    Console.WriteLine("Nội dung lỗi: " + errorBody);
                    Console.WriteLine("==============================================");
                }
            }
            catch (Exception ex)
            {
                // LỖI DO MẠNG HOẶC CODE C# CỦA MÀY
                Console.WriteLine("====== LỖI CODE C# KHI GỌI AI ======");
                Console.WriteLine(ex.Message);
            }

            // Nếu hỏng thì trả về văn mẫu chống cháy
            return $"Chào mừng bạn đến với {poiName}. Đây là một địa điểm tuyệt vời để bạn khám phá.";
        }
    }
}