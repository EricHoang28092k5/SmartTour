using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourBackend.Data;

namespace SmartTourCMS.Controllers
{
    [Authorize(Roles = "Admin,Vendor")]
    public class AudioController : Controller
    {
        private readonly AppDbContext _context;
        private readonly Cloudinary _cloudinary;
        private readonly UserManager<IdentityUser> _userManager;

        public AudioController(AppDbContext context, UserManager<IdentityUser> userManager, IConfiguration configuration)
        {
            _context = context;
            _userManager = userManager;

            // Cách tốt hơn: Đọc từ appsettings.json hoặc Environment cho linh hoạt
            var account = new Account(
                configuration["Cloudinary:CloudName"] ?? Environment.GetEnvironmentVariable("CLOUDINARY_CLOUD_NAME"),
                configuration["Cloudinary:ApiKey"] ?? Environment.GetEnvironmentVariable("CLOUDINARY_API_KEY"),
                configuration["Cloudinary:ApiSecret"] ?? Environment.GetEnvironmentVariable("CLOUDINARY_API_SECRET")
            );
            _cloudinary = new Cloudinary(account);
        }

        // --- 1. TRANG UPLOAD (GET) ---
        public async Task<IActionResult> Upload()
        {
            var user = await _userManager.GetUserAsync(User);

            // PHÂN QUYỀN: Chỉ hiện POI của chính Vendor đó tạo ra
            IQueryable<Poi> poiQuery = _context.Pois;
            if (!await _userManager.IsInRoleAsync(user, "Admin"))
            {
                // Giả sử bảng POI bác cũng đã thêm cột VendorId như bảng Tour
                poiQuery = poiQuery.Where(p => p.VendorId == user.Id);
            }

            var pois = await poiQuery.ToListAsync();
            ViewBag.Pois = new SelectList(pois, "Id", "Name");
            return View();
        }

        // --- 2. LOGIC UPLOAD (POST) ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(int poiId, IFormFile audioFile)
        {
            var user = await _userManager.GetUserAsync(User);

            // KIỂM TRA BẢO MẬT: Check xem POI này có phải của ông Vendor này không
            var targetPoi = await _context.Pois.FindAsync(poiId);
            if (targetPoi == null) return NotFound();

            if (!await _userManager.IsInRoleAsync(user, "Admin") && targetPoi.VendorId != user.Id)
            {
                return Forbid(); // Không cho phép upload "ké" vào POI người khác
            }

            if (audioFile != null && audioFile.Length > 0)
            {
                // Giới hạn file dưới 10MB cho chắc ăn
                if (audioFile.Length > 10 * 1024 * 1024)
                {
                    ModelState.AddModelError("", "File nhạc gì mà nặng thế bác? Dưới 10MB thôi nhé!");
                }
                else
                {
                    var uploadResult = new RawUploadResult();
                    using (var stream = audioFile.OpenReadStream())
                    {
                        var uploadParams = new RawUploadParams()
                        {
                            File = new FileDescription(audioFile.FileName, stream),
                            Folder = "smart_tour_audio",
                            PublicId = $"audio_{Guid.NewGuid()}" // Prefix để dễ quản lý trên Cloud
                        };
                        uploadResult = await _cloudinary.UploadAsync(uploadParams);
                    }

                    if (uploadResult.SecureUrl != null)
                    {
                        var audioEntry = new AudioFile
                        {
                            PoiId = poiId,
                            FileUrl = uploadResult.SecureUrl.ToString(),
                            LanguageId = 1, // Mặc định tiếng Việt hoặc bác làm thêm dropdown chọn ngôn ngữ
                            AudioType = "Narration",
                            Duration = 0,
                            VendorId = user.Id // Đóng dấu người upload
                        };

                        _context.AudioFiles.Add(audioEntry);
                        await _context.SaveChangesAsync();

                        TempData["Success"] = "Bắn nhạc lên mây thành công rồi bác ơi! 🚀";
                        return RedirectToAction("Index", "Poi");
                    }
                }
            }

            // Nếu lỗi, nạp lại danh sách POI cho View
            var pois = await _context.Pois
                .Where(p => User.IsInRole("Admin") || p.VendorId == user.Id)
                .ToListAsync();
            ViewBag.Pois = new SelectList(pois, "Id", "Name");
            return View();
        }
    }
}