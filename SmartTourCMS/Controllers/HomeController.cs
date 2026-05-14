using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartTour.Shared.Models;
using SmartTourAPI.Data;
using SmartTourCMS.Models;
using System.Diagnostics;
using System.Linq;

namespace SmartTourCMS.Controllers
{
    [Authorize(Roles = "Admin,Vendor")]
    /// <summary>
    /// Dashboard CMS:
    /// - Tổng hợp số liệu POI/Tour/Translation theo quyền
    /// - Cấp dữ liệu chart lượt nghe
    /// - Theo dõi thiết bị online/offline theo heartbeat
    /// </summary>
    public class HomeController : Controller
    {
        // Thiết bị được xem là online nếu heartbeat trong vòng 20 giây.
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
                // Vendor chỉ thấy log thuộc POI của mình (nhiều điều kiện để tương thích dữ liệu cũ).
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

            // Mốc online = hiện tại - ngưỡng heartbeat.
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
            // xác định danh tính để show là view dashboard
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized(new { success = false, message = "Unauthorized" });

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            var isVendor = await _userManager.IsInRoleAsync(user, "Vendor");
            //
            var query = _context.PlayLog.Include(l => l.Poi).AsQueryable();
            if (isVendor || !isAdmin)
            {
                query = query.Where(l =>
                    l.Poi != null &&
                    l.Poi.ApprovalStatus == "approved" &&          //chỉ lấy địa điểm đã duyệt
                    (l.Poi.VendorId == user.Id ||
                     l.Poi.CreatedBy == user.Id ||
                     l.Poi.CreatedBy == user.Email ||
                     l.Poi.CreatedBy == user.UserName));
                //kiểm tra cácc tầng để chắc rằng poi thuộc sỡ hữu người vendor đó
            }
            else
            {
                query = query.Where(l => l.Poi != null && l.Poi.ApprovalStatus == "approved"); //admin được đi hết

            }

            var data = await query
                .Where(l => l.Poi != null)
                .GroupBy(l => l.Poi.Name) //nhóm theo tên địa điểm
                .Select(g => new
                {
                    poiName = g.Key,
                    totalPlays = g.Count() //đếm số lượt nghe
                })
                .OrderByDescending(x => x.totalPlays) //sắp xếp từ cao xuống thấp
                .Take(10) //chỉ lấy 10 địa đi
                .ToListAsync();

            return Ok(new { success = true, data });
        }

        [HttpGet]
        [Route("api/cms-dashboard/device-status")]
        public async Task<IActionResult> GetDeviceStatus() // API để dashboard gọi lấy trạng thái thiết bị online/offline
        {
            var user = await _userManager.GetUserAsync(User); // Xác định danh tính để đảm bảo chỉ admin mới được xem trạng thái thiết bị. Vendor không được xem.
            if (user == null) return Unauthorized(new { success = false, message = "Unauthorized" }); // Nếu không xác định được user thì trả về lỗi 401 Unauthorized.
            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            if (!isAdmin) return Forbid(); // Nếu user không phải admin thì trả về lỗi 403 Forbidden vì họ không có quyền truy cập API này.

            var onlineThreshold = DateTime.UtcNow.AddSeconds(-ActiveThresholdSeconds); // Tính mốc thời gian để xác định thiết bị nào được xem là online (ví dụ: nếu ActiveThresholdSeconds = 20 thì mốc sẽ là thời điểm hiện tại trừ đi 20 giây, tức là những thiết bị có LastSeenUtc sau mốc này sẽ được xem là online).
            var devices = await GetDeviceStatusesSafeAsync(onlineThreshold); // Gọi hàm lấy trạng thái thiết bị một cách an toàn, tránh lỗi nếu bảng DevicePresences chưa tồn tại hoặc có vấn đề kết nối cơ sở dữ liệu. Hàm này sẽ trả về danh sách các thiết bị cùng với cờ IsActive đã được tính toán dựa trên mốc thời gian onlineThreshold.
            return Ok(new // Trả về kết quả dưới dạng JSON, bao gồm:
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
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier       // Trả về RequestId để tiện cho việc debug khi có lỗi xảy ra.
            });
        }

        private async Task<List<DeviceStatusViewModel>> GetDeviceStatusesSafeAsync(DateTime onlineThresholdUtc) // Hàm này sẽ cố gắng lấy danh sách trạng thái thiết bị từ cơ sở dữ liệu, nhưng sẽ bắt lỗi nếu có vấn đề (như bảng chưa tồn tại) và trả về một danh sách rỗng thay vì làm sập ứng dụng.
        {
            try
            {
                return await _context.DevicePresences // Truy cập vào bảng DevicePresences để lấy thông tin về các thiết bị đã kết nối.
                    .AsNoTracking()
                    .OrderByDescending(d => d.LastSeenUtc)
                    .Take(300) // Giới hạn để dashboard nhẹ, tránh render quá tải.
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
                        // IsActive là cờ tính động, không lưu cứng trong DB.
                        IsActive = d.LastSeenUtc >= onlineThresholdUtc
                    }) // Chuyển đổi dữ liệu từ entity DevicePresence sang view model DeviceStatusViewModel, đồng thời tính toán cờ IsActive dựa trên mốc thời gian onlineThresholdUtc.
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                var pg = ex as PostgresException ?? ex.InnerException as PostgresException;
                if (pg?.SqlState == "42P01")
                {
                    _logger.LogWarning(
                        "Bảng DevicePresences chưa tồn tại. Chạy migration: dotnet ef database update --project SmartTourAPI");
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