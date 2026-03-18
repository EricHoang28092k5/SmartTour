using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourBackend.Data;

namespace SmartTourCMS.Controllers
{
    [Authorize(Roles = "Admin")]
    public class PoiController : Controller
    {
        private readonly AppDbContext _context;
        private readonly Cloudinary _cloudinary;

        public PoiController(AppDbContext context, Cloudinary cloudinary)
        {
            _context = context;
            _cloudinary = cloudinary;
        }

        // 1. XEM DANH SÁCH
        public async Task<IActionResult> Index()
        {
            var pois = await _context.Pois
                .Include(p => p.AudioFiles)
                .ToListAsync();
            return View(pois);
        }

        // 2. THÊM MỚI (Giao diện)
        public IActionResult Create() => View();

        // 3. THÊM MỚI (Xử lý lưu)
        [HttpPost]
        public async Task<IActionResult> Create(Poi poi, IFormFile imageFile)
        {
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

            _context.Add(poi);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // 4. SỬA (Giao diện GET - Load dữ liệu cũ lên Map)
        [HttpGet]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var poi = await _context.Pois.FindAsync(id);
            if (poi == null) return NotFound();
            return View(poi);
        }

        // 5. SỬA (Xử lý lưu POST - Gộp chung logic upload ảnh)
        [HttpPost]
        public async Task<IActionResult> Edit(int id, Poi poi, IFormFile? imageFile)
        {
            if (id != poi.Id) return NotFound();

            // Nếu có upload ảnh mới thì đẩy lên Cloudinary
            if (imageFile != null && imageFile.Length > 0)
            {
                var uploadParams = new ImageUploadParams()
                {
                    File = new FileDescription(imageFile.FileName, imageFile.OpenReadStream()),
                    Folder = "SmartTour/Pois"
                };
                var uploadResult = await _cloudinary.UploadAsync(uploadParams);

                // Cập nhật URL mới
                poi.ImageUrl = uploadResult.SecureUrl.ToString();
            }

            // Lưu mọi thay đổi (Tên, Tọa độ, Ảnh...) vào DB
            _context.Update(poi);
            await _context.SaveChangesAsync();

            TempData["success"] = "Cập nhật dữ liệu thành công rồi bác ơi!";
            return RedirectToAction(nameof(Index));
        }

        // 6. XÓA
        public async Task<IActionResult> Delete(int id)
        {
            var poi = await _context.Pois.FindAsync(id);
            if (poi != null)
            {
                _context.Pois.Remove(poi);
                await _context.SaveChangesAsync();
                TempData["success"] = "Đã xóa sạch địa điểm này!";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}