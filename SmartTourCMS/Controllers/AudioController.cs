using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Mvc;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SmartTourBackend.Data; // Chỗ này bác check lại tên AppDbContext nằm ở đâu
using SmartTour.Shared.Models;

namespace SmartTourCMS.Controllers
{
    public class AudioController : Controller
    {
        private readonly AppDbContext _context;
        private readonly Cloudinary _cloudinary;

        public AudioController(AppDbContext context, IConfiguration config)
        {
            _context = context;
            // Khởi tạo tài khoản Cloudinary
            var account = new Account(
                config["CloudinarySettings:CloudName"],
                config["CloudinarySettings:ApiKey"],
                config["CloudinarySettings:ApiSecret"]
            );
            _cloudinary = new Cloudinary(account);
        }

        public IActionResult Upload()
        {
            ViewBag.Pois = new SelectList(_context.Pois, "Id", "Name");
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Upload(int poiId, IFormFile audioFile)
        {
            if (audioFile != null && audioFile.Length > 0)
            {
                // 1. Đẩy file lên Cloudinary
                var uploadResult = new RawUploadResult();
                using (var stream = audioFile.OpenReadStream())
                {
                    var uploadParams = new RawUploadParams()
                    {
                        File = new FileDescription(audioFile.FileName, stream),
                        Folder = "smart_tour_audio", // Tên thư mục trên Cloud
                        PublicId = Guid.NewGuid().ToString() // Tên file ngẫu nhiên
                    };
                    uploadResult = await _cloudinary.UploadAsync(uploadParams);
                }

                // 2. Nếu upload thành công, lưu link vào Database
                if (uploadResult.SecureUrl != null)
                {
                    var audioEntry = new AudioFile
                    {
                        PoiId = poiId,
                        FileUrl = uploadResult.SecureUrl.ToString(), // Link https://res.cloudinary.com/...
                        LanguageId = 1,
                        AudioType = "Narration",
                        Duration = 0 // Bác có thể tự tính duration sau
                    };

                    _context.AudioFiles.Add(audioEntry);
                    await _context.SaveChangesAsync();

                    return RedirectToAction("Index", "Poi");
                }
            }

            ViewBag.Pois = new SelectList(_context.Pois, "Id", "Name");
            return View();
        }
    }
}