using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourBackend.Data;
using SmartTourCMS.Models;

namespace SmartTourCMS.Controllers;

[Authorize(Roles = "Admin,Vendor")]
public class PremiumController : Controller
{
    private static readonly HttpClient Http = new();
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public PremiumController(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? poiId = null)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userId)) return RedirectToAction("Login", "Account");

        var isAdmin = User.IsInRole("Admin");
        var poisQuery = _db.Pois.AsNoTracking().Where(x => x.ApprovalStatus == "approved");
        if (!isAdmin)
            poisQuery = poisQuery.Where(x => x.VendorId == userId);

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
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePayment(int poiId, string packageCode)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userId)) return RedirectToAction("Login", "Account");

        var apiSettings = ReadBackendApiSettings(_config);
        if (!apiSettings.IsConfigured)
        {
            TempData["Error"] = "Backend API chưa cấu hình cho Premium payment.";
            return RedirectToAction(nameof(Index), new { poiId });
        }

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

        var isAdmin = User.IsInRole("Admin");
        if (!isAdmin && !string.Equals(poi.VendorId, userId, StringComparison.Ordinal))
            return Forbid();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{apiSettings.BaseUrl.TrimEnd('/')}/api/vendor/premium/create-payment-cms");
            request.Headers.Add("X-Internal-Key", apiSettings.InternalKey);
            request.Content = JsonContent.Create(new CmsCreatePremiumPaymentRequest
            {
                PoiId = poi.Id,
                VendorUserId = poi.VendorId ?? userId,
                Months = package.Months,
                Amount = (int)package.AmountVnd
            });

            using var response = await Http.SendAsync(request);
            var raw = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                TempData["Error"] = $"Backend API lỗi ({(int)response.StatusCode}): {raw}";
                return RedirectToAction(nameof(Index), new { poiId });
            }

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var ok = root.TryGetProperty("success", out var successEl) && successEl.GetBoolean();
            if (!ok || !root.TryGetProperty("data", out var dataEl))
            {
                TempData["Error"] = "Backend API trả dữ liệu không hợp lệ.";
                return RedirectToAction(nameof(Index), new { poiId });
            }

            var orderId = dataEl.TryGetProperty("orderId", out var orderEl) ? orderEl.GetString() ?? string.Empty : string.Empty;
            var payUrl = dataEl.TryGetProperty("payUrl", out var payUrlEl) ? payUrlEl.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(payUrl))
            {
                TempData["Error"] = "Thiếu dữ liệu thanh toán từ Backend API.";
                return RedirectToAction(nameof(Index), new { poiId });
            }

            var vm = new PremiumCheckoutViewModel
            {
                PoiId = poi.Id,
                PoiName = poi.Name,
                PackageName = package.Name,
                PackageCode = package.Code,
                Months = package.Months,
                AmountVnd = package.AmountVnd,
                OrderId = orderId,
                PayUrl = payUrl,
                QrImageUrl = $"https://quickchart.io/qr?text={Uri.EscapeDataString(payUrl)}&size=300"
            };
            return View("Checkout", vm);
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Không kết nối được Backend API premium: {ex.Message}";
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

        var poi = await _db.Pois.AsNoTracking().FirstOrDefaultAsync(x => x.Id == order.PoiId);
        var isPaid = string.Equals(order.Status, "paid", StringComparison.OrdinalIgnoreCase);
        var message = isPaid
            ? "Thanh toán thành công. POI đã được nâng cấp Premium."
            : "Giao dịch đang chờ xác nhận từ MoMo hoặc chưa thành công.";

        return View("Return", new PremiumReturnViewModel
        {
            OrderId = order.OrderId,
            IsPaid = isPaid,
            Message = message,
            Poi = poi
        });
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

    private sealed class CmsCreatePremiumPaymentRequest
    {
        public int PoiId { get; set; }
        public int Months { get; set; }
        public int Amount { get; set; }
        public string VendorUserId { get; set; } = string.Empty;
    }
}
