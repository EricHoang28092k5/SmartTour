using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity; // Bổ sung Identity để lấy thông tin User
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourBackend.Data;

namespace SmartTourCMS.Controllers
{
    [Authorize(Roles = "Admin,Vendor")]
    public class LogController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<IdentityUser> _userManager; // 1. Khai báo UserManager

        public LogController(AppDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // --- 1. Xem lịch sử di chuyển (Locations) ---
        public IActionResult Locations()
        {
            TempData["Error"] = "Chức năng lịch sử di chuyển đã được tắt theo cấu hình hệ thống.";
            return RedirectToAction(nameof(Plays));
        }

        // --- 2. Xem lịch sử nghe Audio (Plays) ---
        public async Task<IActionResult> Plays(int page = 1, int pageSize = 20)
        {
            if (page < 1) page = 1;
            if (pageSize <= 0) pageSize = 20;
            pageSize = Math.Min(pageSize, 100);

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            var isVendor = await _userManager.IsInRoleAsync(user, "Vendor");

            // Khởi tạo câu truy vấn cơ bản
            var query = _context.PlayLog.Include(l => l.Poi).AsQueryable();

            // Nếu user có role Vendor (kể cả lỡ gán thêm Admin), vẫn chỉ xem log POI của vendor đó.
            // Chỉ Admin thuần mới được xem toàn bộ.
            if (isVendor || !isAdmin)
            {
                query = query.Where(l =>
                    l.Poi != null &&
                    (l.Poi.VendorId == user.Id ||
                     l.Poi.CreatedBy == user.Id ||
                     l.Poi.CreatedBy == user.Email ||
                     l.Poi.CreatedBy == user.UserName));
            }

            var totalItems = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            if (totalPages == 0) totalPages = 1;
            if (page > totalPages) page = totalPages;

            var logs = await query
                .OrderByDescending(l => l.Time)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CurrentPage = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalItems = totalItems;

            return View(logs);
        }
    }
}