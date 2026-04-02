using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using SmartTourBackend.Data; // Nhớ check lại using này cho khớp với DbContext của bác
using SmartTour.Shared.Models;

namespace SmartTourCMS.Controllers
{
    // Bùa chú: Chỉ Trùm cuối (Admin) mới được quyền thêm/sửa Danh mục
    [Authorize(Roles = "Admin")]
    public class CategoryController : Controller
    {
        private readonly AppDbContext _context;
        private readonly Cloudinary _cloudinary;

        public CategoryController(AppDbContext context, Cloudinary cloudinary)
        {
            _context = context;
            _cloudinary = cloudinary;
        }

        // --- 1. HIỂN THỊ DANH SÁCH ---
        public async Task<IActionResult> Index() {

            List<Category> category = await _context.Category.ToListAsync();
            return View(category);
        }

        // --- 2. TẠO MỚI (GET) ---
        public IActionResult Create() => View();

        // --- 3. TẠO MỚI (POST) - CÓ UP ẢNH ICON LÊN CLOUDINARY ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        // --- 3. TẠO MỚI (POST) - CÓ UP ẢNH ICON LÊN CLOUDINARY ---
        [HttpPost]
        public async Task<IActionResult> Create(Category category, IFormFile? iconFile)
        {
            if (ModelState.IsValid)
            {
                // Nếu Admin có chọn file ảnh Icon
                if (iconFile != null && iconFile.Length > 0)
                {
                    var uploadParams = new ImageUploadParams()
                    {
                        File = new FileDescription(iconFile.FileName, iconFile.OpenReadStream()),
                        Folder = "SmartTour/Categories" // Lưu vào thư mục này trên Cloudinary cho gọn
                    };
                    var uploadResult = await _cloudinary.UploadAsync(uploadParams);
                    category.IconUrl = uploadResult.SecureUrl.ToString();
                }

                // Nếu không nhập mã màu thì gán mặc định màu xám
                if (string.IsNullOrEmpty(category.ColorCode))
                {
                    category.ColorCode = "#808080";
                }

                _context.Category.Add(category);
                await _context.SaveChangesAsync();

                TempData["success"] = "Đã thêm Danh mục mới thành công!";
                return RedirectToAction(nameof(Index));
            }
            return View(category);
        }
    }
}