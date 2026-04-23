using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartTour.Shared.Models;
using SmartTourBackend.Data;
using SmartTourCMS.Models;
using System.Diagnostics;
using System.Linq;

namespace SmartTourCMS.Controllers
{
    [Authorize(Roles = "Admin,Vendor")]
    public class HomeController : Controller
    {
        private const int ActiveThresholdSeconds = 20;
        private readonly ILogger<HomeController> _logger;
        private readonly AppDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public HomeController(ILogger<HomeController> logger, AppDbContext context, UserManager<IdentityUser> userManager)
        {
            _logger = logger;
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");

            // --- 1. THỐNG KÊ SỐ LƯỢNG THEO QUYỀN ---
            if (isAdmin)
            {
                ViewBag.TotalPois = await _context.Pois.CountAsync(p => p.ApprovalStatus == "approved");
                ViewBag.TotalTours = await _context.Tours.CountAsync();
                ViewBag.TotalTranslations = await _context.PoiTranslations
                    .CountAsync(pt => _context.Pois.Any(p => p.Id == pt.PoiId && p.ApprovalStatus == "approved"));
                ViewBag.TotalLanguages = await _context.Languages.CountAsync();
            }
            else
            {
                ViewBag.TotalPois = await _context.Pois.CountAsync(p => p.VendorId == user.Id && p.ApprovalStatus == "approved");
                ViewBag.TotalTours = await _context.Tours.CountAsync(t => t.VendorId == user.Id);
                ViewBag.TotalTranslations = await _context.PoiTranslations
                    .CountAsync(pt => _context.Pois.Any(p => p.Id == pt.PoiId && p.VendorId == user.Id && p.ApprovalStatus == "approved"));
                ViewBag.TotalLanguages = await _context.Languages.CountAsync();
            }

            // --- 2. LẤY DỮ LIỆU THẬT CHO BIỂU ĐỒ VÀ TỔNG LƯỢT NGHE (THAY VÌ SỐ FAKE) ---
            var playLogQuery = _context.PlayLog.Include(l => l.Poi).AsQueryable();

            if (!isAdmin)
            {
                playLogQuery = playLogQuery.Where(l =>
                    l.Poi != null &&
                    l.Poi.ApprovalStatus == "approved" &&
                    (l.Poi.VendorId == user.Id ||
                     l.Poi.CreatedBy == user.Id ||
                     l.Poi.CreatedBy == user.Email ||
                     l.Poi.CreatedBy == user.UserName));
            }
            else
            {
                playLogQuery = playLogQuery.Where(l => l.Poi != null && l.Poi.ApprovalStatus == "approved");
            }

            // Lấy tổng lượt nghe thật thay vì gán cứng 1250 hay 450
            ViewBag.TotalVisits = await playLogQuery.CountAsync();
            ViewBag.ActivePoisWithPlays = await playLogQuery
                .Where(l => l.Poi != null)
                .Select(l => l.PoiId)
                .Distinct()
                .CountAsync();

            // Group dữ liệu để vẽ biểu đồ (Top 10 POI được nghe nhiều nhất)
            var chartData = await playLogQuery
                .GroupBy(l => l.Poi.Name)
                .Select(g => new
                {
                    PoiName = g.Key,
                    ListenCount = g.Count()
                })
                .OrderByDescending(x => x.ListenCount)
                .Take(10)
                .ToListAsync();

            ViewBag.ChartData = chartData;

            var onlineThreshold = DateTime.UtcNow.AddSeconds(-ActiveThresholdSeconds);
            if (isAdmin)
            {
                var devices = await GetDeviceStatusesSafeAsync(onlineThreshold);
                ViewBag.OnlineDevices = devices.Count(x => x.IsActive);
                ViewBag.DeviceStatuses = devices;
            }
            else
            {
                ViewBag.OnlineDevices = 0;
                ViewBag.DeviceStatuses = new List<DeviceStatusViewModel>();
            }

            // --- 3. LẤY 5 TOUR MỚI NHẤT (ĐÃ LỌC QUYỀN) ---
            var tourQuery = _context.Tours.AsQueryable();
            if (!isAdmin)
            {
                tourQuery = tourQuery.Where(t => t.VendorId == user.Id);
            }

            var recentTours = await tourQuery
                .Include(t => t.TourPois)
                .OrderByDescending(t => t.CreatedAt)
                .Take(5)
                .ToListAsync();

            return View(recentTours);
        }

        public IActionResult Privacy() => View();

        [HttpGet]
        [Route("api/cms-dashboard/poi-stats")]
        public async Task<IActionResult> GetDashboardPoiStats()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized(new { success = false, message = "Unauthorized" });

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            var isVendor = await _userManager.IsInRoleAsync(user, "Vendor");

            var query = _context.PlayLog.Include(l => l.Poi).AsQueryable();
            if (isVendor || !isAdmin)
            {
                query = query.Where(l =>
                    l.Poi != null &&
                    l.Poi.ApprovalStatus == "approved" &&
                    (l.Poi.VendorId == user.Id ||
                     l.Poi.CreatedBy == user.Id ||
                     l.Poi.CreatedBy == user.Email ||
                     l.Poi.CreatedBy == user.UserName));
            }
            else
            {
                query = query.Where(l => l.Poi != null && l.Poi.ApprovalStatus == "approved");
            }

            var data = await query
                .Where(l => l.Poi != null)
                .GroupBy(l => l.Poi.Name)
                .Select(g => new
                {
                    poiName = g.Key,
                    totalPlays = g.Count()
                })
                .OrderByDescending(x => x.totalPlays)
                .Take(10)
                .ToListAsync();

            return Ok(new { success = true, data });
        }

        [HttpGet]
        [Route("api/cms-dashboard/device-status")]
        public async Task<IActionResult> GetDeviceStatus()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized(new { success = false, message = "Unauthorized" });
            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            if (!isAdmin) return Forbid();

            var onlineThreshold = DateTime.UtcNow.AddSeconds(-ActiveThresholdSeconds);
            var devices = await GetDeviceStatusesSafeAsync(onlineThreshold);
            return Ok(new
            {
                success = true,
                thresholdSeconds = ActiveThresholdSeconds,
                onlineDevices = devices.Count(x => x.IsActive),
                data = devices
            });
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new SmartTourCMS.Models.ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }

        private async Task<List<DeviceStatusViewModel>> GetDeviceStatusesSafeAsync(DateTime onlineThresholdUtc)
        {
            try
            {
                return await _context.DevicePresences
                    .AsNoTracking()
                    .OrderByDescending(d => d.LastSeenUtc)
                    .Take(300)
                    .Select(d => new DeviceStatusViewModel
                    {
                        DeviceId = d.DeviceId,
                        IpAddress = d.IpAddress,
                        DeviceModel = d.DeviceModel,
                        Platform = d.Platform,
                        OsVersion = d.OsVersion,
                        AppVersion = d.AppVersion,
                        UserAgent = d.UserAgent,
                        LastSeenUtc = d.LastSeenUtc,
                        IsActive = d.LastSeenUtc >= onlineThresholdUtc
                    })
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                var pg = ex as PostgresException ?? ex.InnerException as PostgresException;
                if (pg?.SqlState == "42P01")
                {
                    _logger.LogWarning(
                        "Bảng DevicePresences chưa tồn tại. Chạy migration: dotnet ef database update --project SmartTourBackend");
                }
                else
                {
                    _logger.LogWarning(ex, "Không lấy được danh sách trạng thái thiết bị.");
                }

                return new List<DeviceStatusViewModel>();
            }
        }
    }
}