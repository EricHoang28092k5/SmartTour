using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourAPI.Data;
using SmartTourAPI.Services;
using SmartTourCMS.Models;
using SmartTour.Shared.Models;

namespace SmartTourCMS.Controllers;

[Authorize(Roles = "Admin,Vendor")]
/// <summary>
/// Luồng Premium trên CMS:
/// - Admin: áp dụng gói Premium trực tiếp lên POI (không MoMo)
/// - Vendor: mua Premium trừ ví qua Backend API
/// </summary>
public class PremiumController : Controller
{
    private static readonly HttpClient Http = new();
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly VendorWalletService _wallet;

    public PremiumController(AppDbContext db, IConfiguration config, VendorWalletService wallet)
    {
        _db = db;
        _config = config;
        _wallet = wallet;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? poiId = null)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userId)) return RedirectToAction("Login", "Account");

        // Chỉ POI của tài khoản hiện tại (vendor: của vendor; admin: POI admin tự tạo — cùng VendorId = Id admin).
        var poisQuery = _db.Pois.AsNoTracking()
            .Where(x => x.ApprovalStatus == "approved" && x.VendorId == userId);

        var pois = await poisQuery
            .OrderBy(x => x.Name)
            .Select(x => new PremiumPoiRow
            {
                PoiId = x.Id,
                PoiName = x.Name,
                IsPremium = x.IsPremium && x.PremiumExpiresAt.HasValue && x.PremiumExpiresAt > DateTime.UtcNow,
                PremiumExpiresAt = x.PremiumExpiresAt
            })
            .ToListAsync();

        var vm = new PremiumPageViewModel
        {
            Pois = pois,
            Packages = BuildPackages(_config),
            SelectedPoiId = poiId
        };
        if (User.IsInRole("Vendor"))
            ViewBag.WalletBalanceVnd = await _wallet.GetBalanceVndAsync(userId);
        ViewBag.IsAdminPremium = User.IsInRole("Admin");
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePayment(int poiId, string packageCode)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userId)) return RedirectToAction("Login", "Account");

        var packages = BuildPackages(_config);
        var package = packages.FirstOrDefault(x => string.Equals(x.Code, packageCode, StringComparison.OrdinalIgnoreCase));
        if (package == null)
        {
            TempData["Error"] = "Gói thanh toán không hợp lệ.";
            return RedirectToAction(nameof(Index), new { poiId });
        }

        var poi = await _db.Pois.AsNoTracking().FirstOrDefaultAsync(x => x.Id == poiId);
        if (poi == null)
        {
            TempData["Error"] = "Không tìm thấy POI.";
            return RedirectToAction(nameof(Index));
        }

        if (!string.Equals(poi.VendorId, userId, StringComparison.Ordinal))
            return Forbid();

        if (User.IsInRole("Admin"))
        {
            var poiTracked = await _db.Pois.FirstOrDefaultAsync(x => x.Id == poiId, HttpContext.RequestAborted);
            if (poiTracked == null)
            {
                TempData["Error"] = "Không tìm thấy POI.";
                return RedirectToAction(nameof(Index));
            }

            ApplyPremiumPackageMonths(poiTracked, package.Months);
            poiTracked.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(HttpContext.RequestAborted);
            TempData["Success"] =
                $"Đã áp dụng gói {package.Name} cho POI. Premium hiệu lực đến {poiTracked.PremiumExpiresAt:dd/MM/yyyy HH:mm} (UTC).";
            return RedirectToAction(nameof(Index), new { poiId });
        }

        var apiSettings = ReadBackendApiSettings(_config);
        if (!apiSettings.IsConfigured)
        {
            TempData["Error"] = "Backend API chưa cấu hình cho thanh toán từ ví.";
            return RedirectToAction(nameof(Index), new { poiId });
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{apiSettings.BaseUrl.TrimEnd('/')}/api/vendor/premium/purchase-premium-wallet-cms");
            request.Headers.Add("X-Internal-Key", apiSettings.InternalKey);
            request.Content = JsonContent.Create(new
            {
                poiId = poi.Id,
                vendorUserId = poi.VendorId ?? userId,
                months = package.Months,
                priceVnd = package.AmountVnd
            });

            using var response = await Http.SendAsync(request);
            var raw = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                TempData["Error"] = string.IsNullOrWhiteSpace(raw)
                    ? $"Thanh toán thất bại ({(int)response.StatusCode})."
                    : raw;
                return RedirectToAction(nameof(Index), new { poiId });
            }

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var ok = root.TryGetProperty("success", out var successEl) && successEl.GetBoolean();
            if (!ok)
            {
                var msg = root.TryGetProperty("message", out var mEl) ? mEl.GetString() : "Thanh toán thất bại.";
                TempData["Error"] = msg;
                return RedirectToAction(nameof(Index), new { poiId });
            }

            TempData["Error"] = null;
            TempData["Success"] = $"Đã mua gói {package.Name} bằng ví. Số dư còn lại có thể xem tại mục Ví vendor.";
            return RedirectToAction(nameof(Index), new { poiId });
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Không kết nối được Backend API: {ex.Message}";
            return RedirectToAction(nameof(Index), new { poiId });
        }
    }

    [AllowAnonymous]
    [HttpGet("/payment/return")]
    public async Task<IActionResult> PaymentReturn(string? orderId = null)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return View("Return", new PremiumReturnViewModel { Message = "Không tìm thấy mã đơn hàng." });

        var order = await _db.VendorPremiumOrders.AsNoTracking().FirstOrDefaultAsync(x => x.OrderId == orderId);
        if (order == null)
            return View("Return", new PremiumReturnViewModel { OrderId = orderId, Message = "Không tìm thấy giao dịch." });

        var isPaid = string.Equals(order.Status, "paid", StringComparison.OrdinalIgnoreCase);
        var kind = string.IsNullOrWhiteSpace(order.OrderKind) ? "premium" : order.OrderKind.Trim();
        string message;
        if (isPaid)
        {
            message = string.Equals(kind, "wallet_topup", StringComparison.OrdinalIgnoreCase)
                ? "Thanh toán thành công. Số dư ví đã được cộng."
                : "Thanh toán thành công. POI đã được nâng cấp Premium.";
        }
        else
        {
            message = "Giao dịch đang chờ xác nhận từ MoMo hoặc chưa thành công.";
        }

        var poi = order.PoiId > 0
            ? await _db.Pois.AsNoTracking().FirstOrDefaultAsync(x => x.Id == order.PoiId)
            : null;

        var isWalletTopup = string.Equals(kind, "wallet_topup", StringComparison.OrdinalIgnoreCase);
        var continueController = isWalletTopup ? "Wallet" : "Premium";
        var continueLabel = isWalletTopup ? "Về Ví vendor" : "Về trang Premium";

        return View("Return", new PremiumReturnViewModel
        {
            OrderId = order.OrderId,
            IsPaid = isPaid,
            Message = message,
            Poi = poi,
            PaidAt = order.PaidAt,
            StatusApiUrl = Url.Action(nameof(GetPaymentStatus), "Premium", new { orderId = order.OrderId }, Request.Scheme) ?? string.Empty,
            ContinueController = continueController,
            ContinueLabel = continueLabel
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetPaymentStatus(string orderId, bool forceProviderCheck = false)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return BadRequest(new { success = false, message = "Thiếu orderId." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new { success = false, message = "Chưa đăng nhập." });

        var order = await _db.VendorPremiumOrders.AsNoTracking().FirstOrDefaultAsync(x => x.OrderId == orderId);
        if (order == null) return NotFound(new { success = false, message = "Không tìm thấy giao dịch." });

        var isAdmin = User.IsInRole("Admin");
        if (!isAdmin && !string.Equals(order.VendorUserId, userId, StringComparison.Ordinal))
            return Forbid();

        var apiSettings = ReadBackendApiSettings(_config);
        if (!apiSettings.IsConfigured)
        {
            return Ok(new
            {
                success = true,
                data = new
                {
                    orderId = order.OrderId,
                    status = order.Status,
                    paidAt = order.PaidAt
                }
            });
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{apiSettings.BaseUrl.TrimEnd('/')}/api/vendor/premium/order-status-cms");
            // forceProviderCheck=true cho phép backend query MoMo realtime thay vì chỉ đọc DB local.
            request.Headers.Add("X-Internal-Key", apiSettings.InternalKey);
            request.Content = JsonContent.Create(new CmsPremiumOrderStatusRequest
            {
                OrderId = orderId.Trim(),
                VendorUserId = order.VendorUserId,
                ForceProviderCheck = forceProviderCheck
            });

            using var response = await Http.SendAsync(request);
            var raw = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode, new { success = false, message = "Backend API status lỗi.", providerResponse = raw });
            }

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var ok = root.TryGetProperty("success", out var successEl) && successEl.GetBoolean();
            if (!ok || !root.TryGetProperty("data", out var dataEl))
                return BadRequest(new { success = false, message = "Backend API trả dữ liệu không hợp lệ." });

            return Ok(new { success = true, data = dataEl.Clone() });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = $"Không kết nối được Backend API premium: {ex.Message}" });
        }
    }

    /// <summary>Cùng quy tắc gia hạn như sau thanh toán MoMo / ví (tuần = Months 0 → +7 ngày).</summary>
    private static void ApplyPremiumPackageMonths(Poi poi, int durationMonths)
    {
        var now = DateTime.UtcNow;
        var start = poi.PremiumExpiresAt.HasValue && poi.PremiumExpiresAt > now
            ? poi.PremiumExpiresAt.Value
            : now;
        poi.IsPremium = true;
        poi.PremiumActivatedAt ??= now;
        poi.PremiumExpiresAt = durationMonths == 0
            ? start.AddDays(7)
            : start.AddMonths(Math.Max(1, durationMonths));
    }

    private static List<PremiumPackageOption> BuildPackages(IConfiguration config)
    {
        var weekly = ParseAmount(config["MoMo:PackagePrice:Weekly"], 59000);
        var monthly = ParseAmount(config["MoMo:PackagePrice:Monthly"], 199000);
        var yearly = ParseAmount(config["MoMo:PackagePrice:Yearly"], 1990000);

        return
        [
            new PremiumPackageOption
            {
                Code = "week",
                Name = "Gói tuần",
                Months = 0,
                AmountVnd = weekly,
                Description = "Hiệu lực 7 ngày, phù hợp test nhanh chiến dịch."
            },
            new PremiumPackageOption
            {
                Code = "month",
                Name = "Gói tháng",
                Months = 1,
                AmountVnd = monthly,
                Description = "Hiệu lực 1 tháng, phù hợp vận hành thường xuyên."
            },
            new PremiumPackageOption
            {
                Code = "year",
                Name = "Gói năm",
                Months = 12,
                AmountVnd = yearly,
                Description = "Hiệu lực 12 tháng, tiết kiệm chi phí dài hạn."
            }
        ];
    }

    private static long ParseAmount(string? raw, long fallback)
    {
        return long.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }

    private static BackendApiSettings ReadBackendApiSettings(IConfiguration config)
    {
        var section = config.GetSection("BackendApi");
        return new BackendApiSettings
        {
            BaseUrl = section["BaseUrl"] ?? string.Empty,
            InternalKey = section["InternalKey"] ?? string.Empty
        };
    }

    private sealed class BackendApiSettings
    {
        public string BaseUrl { get; init; } = string.Empty;
        public string InternalKey { get; init; } = string.Empty;
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(BaseUrl) &&
            !string.IsNullOrWhiteSpace(InternalKey);
    }

    private sealed class CmsPremiumOrderStatusRequest
    {
        public string OrderId { get; set; } = string.Empty;
        public string VendorUserId { get; set; } = string.Empty;
        public bool ForceProviderCheck { get; set; }
    }
}
