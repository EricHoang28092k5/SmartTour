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
        public async Task<IActionResult> Locations()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");

            // BẢO MẬT: Lịch sử di chuyển GPS là dữ liệu nhạy cảm của hệ thống.
            // Vendor không có quyền xem cái này, chỉ Admin mới được thấy.
            if (!isAdmin)
            {
                TempData["Error"] = "Bác là Vendor, không có quyền xem nhật ký di chuyển tổng của hệ thống đâu nhé!";
                return RedirectToAction("Index", "Home"); // Đá về trang chủ
            }

            var logs = await _context.UserLocationLogs
                .OrderByDescending(l => l.Timestamp)
                .Take(100) // Lấy 100 bản ghi mới nhất cho đỡ nặng máy
                .ToListAsync();

            return View(logs);
        }

        // --- 2. Xem lịch sử nghe Audio (Plays) ---
        public async Task<IActionResult> Plays()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");

            // Khởi tạo câu truy vấn cơ bản
            var query = _context.PlayLog.Include(l => l.Poi).AsQueryable();

            if (!isAdmin)
            {
                // PHÂN QUYỀN VENDOR: 
                // Chỉ lấy những lượt nghe nhạc diễn ra tại các Địa điểm (POI) do chính tay ông Vendor này tạo ra.
                query = query.Where(l => l.Poi.VendorId == user.Id);
            }

            var logs = await query
                .OrderByDescending(l => l.Time)
                .Take(100)
                .ToListAsync();

            return View(logs);
        }
    }
}