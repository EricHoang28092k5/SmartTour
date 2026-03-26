using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering; // Dùng cho cái Dropdown chọn Quán
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourBackend.Data;
using SmartTour.Shared.Models;
namespace SmartTourCMS.Controllers
{
    // Bắt buộc phải đăng nhập và có quyền Admin hoặc Vendor mới được vào
    [Authorize(Roles = "Admin,Vendor")]
    public class FoodController : Controller
    {
        private readonly AppDbContext _context;
        private readonly Cloudinary _cloudinary;
        private readonly UserManager<IdentityUser> _userManager;

        public FoodController(AppDbContext context, Cloudinary cloudinary, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _cloudinary = cloudinary;
            _userManager = userManager;
        }

        // --- 1. DANH SÁCH MÓN ĂN ---
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");

            // Kéo danh sách món ăn, lôi luôn thằng Bố (Poi) lên để lấy tên Quán
            var query = _context.Food.Include(f => f.Poi).AsQueryable();

            if (!isAdmin)
            {
                // LƯỚI BẢO MẬT: Vendor nào chỉ nhìn thấy Menu của Vendor đó
                query = query.Where(f => f.Poi.VendorId == user.Id);
            }

            return View(await query.ToListAsync());
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
    }
}