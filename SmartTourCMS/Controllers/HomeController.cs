using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity; // Bổ sung thư viện này
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourBackend.Data;
using System.Diagnostics;
using System.Linq;

namespace SmartTourCMS.Controllers
{
    [Authorize(Roles = "Admin,Vendor")]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly AppDbContext _context;
        private readonly UserManager<IdentityUser> _userManager; // 1. Bơm thêm UserManager

        public HomeController(ILogger<HomeController> logger, AppDbContext context, UserManager<IdentityUser> userManager)
        {
            _logger = logger;
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            // 2. Chộp lấy ID của ông đang đăng nhập
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");

            // 3. Thống kê số lượng theo quyền (Dùng VendorId thay vì CreatedBy)
            if (isAdmin)
            {
                // Chúa tể: Đếm tất tần tật
                ViewBag.TotalPois = await _context.Pois.CountAsync();
                ViewBag.TotalTours = await _context.Tours.CountAsync();
                ViewBag.TotalTranslations = await _context.PoiTranslations.CountAsync();
                ViewBag.TotalLanguages = await _context.Languages.CountAsync();

                ViewBag.TotalVisits = 1250; // Giả lập dữ liệu tổng
            }
            else
            {
                // Vendor: Chỉ đếm đồ của mình bằng cột VendorId anh em mình đã tạo
                ViewBag.TotalPois = await _context.Pois.CountAsync(p => p.VendorId == user.Id);
                ViewBag.TotalTours = await _context.Tours.CountAsync(t => t.VendorId == user.Id);

                // Trích xuất số bản dịch: Đếm những bản dịch thuộc về các POI của ông Vendor này
                ViewBag.TotalTranslations = await _context.PoiTranslations
                    .CountAsync(pt => _context.Pois.Any(p => p.Id == pt.PoiId && p.VendorId == user.Id));

                // Ngôn ngữ hệ thống thì cứ để cho thấy hết
                ViewBag.TotalLanguages = await _context.Languages.CountAsync();

                ViewBag.TotalVisits = 450; // Giả lập lượt xem của riêng Vendor
            }

            // 4. Lấy 5 Tour mới nhất lên Dashboard (Lọc theo quyền)
            var tourQuery = _context.Tours.AsQueryable();
            if (!isAdmin)
            {
                tourQuery = tourQuery.Where(t => t.VendorId == user.Id); // Lọc gắt gao
            }

            var recentTours = await tourQuery
                .Include(t => t.TourPois)
                .OrderByDescending(t => t.CreatedAt)
                .Take(5)
                .ToListAsync();

            return View(recentTours);
        }

        public IActionResult Privacy() => View();

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new SmartTourCMS.Models.ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }
    }
}