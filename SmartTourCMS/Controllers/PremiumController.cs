using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourAPI.Data;
using SmartTourCMS.Models;
using SmartTour.Shared.Models;

namespace SmartTourCMS.Controllers;

[Authorize(Roles = "Admin,Vendor")]
/// <summary>
/// Luồng Premium trên CMS:
/// - Hiển thị POI đã duyệt thuộc tài khoản đăng nhập (vendor: của vendor; admin: POI do chính admin tạo — VendorId = Id admin)
/// - Tạo thanh toán qua backend proxy (internal key)
/// - Poll trạng thái order và hiển thị trang return
/// </summary>
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
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePayment(int poiId, string packageCode)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userId)) return RedirectToAction("Login", "Account");

        // CMS không gọi MoMo trực tiếp: gọi backend API để tập trung verify/signature một nơi.
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

        if (!string.Equals(poi.VendorId, userId, StringComparison.Ordinal))
            return Forbid();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{apiSettings.BaseUrl.TrimEnd('/')}/api/vendor/premium/create-payment-cms");
            // Internal key giúp backend phân biệt call nội bộ từ CMS.
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
            var deeplink = dataEl.TryGetProperty("deeplink", out var deeplinkEl) ? deeplinkEl.GetString() : null;
            var qrCodeUrl = dataEl.TryGetProperty("qrCodeUrl", out var qrCodeUrlEl) ? qrCodeUrlEl.GetString() : null;
            // Ưu tiên deeplink (mobile wallet), fallback payUrl (web checkout).
            var checkoutUrl = !string.IsNullOrWhiteSpace(deeplink) ? deeplink : payUrl;
            var qrSource = !string.IsNullOrWhiteSpace(qrCodeUrl) ? qrCodeUrl : checkoutUrl;
            var vendorUserIdForOrder = poi.VendorId ?? userId;
            if (string.IsNullOrWhiteSpace(orderId))
            {
                TempData["Error"] = "Thiếu dữ liệu thanh toán từ Backend API.";
                return RedirectToAction(nameof(Index), new { poiId });
            }

            if (string.IsNullOrWhiteSpace(checkoutUrl))
            {
                // API trả 202 + đơn xếp hàng: link MoMo do worker ghi sau — poll order-status cho tới khi có payUrl/deeplink.
                var polled = await PollForPremiumCheckoutUrlsAsync(
                    apiSettings.BaseUrl,
                    apiSettings.InternalKey,
                    orderId,
                    vendorUserIdForOrder,
                    HttpContext.RequestAborted);
                if (polled == null)
                {
                    TempData["Error"] =
                        "Đã tạo đơn nhưng chưa nhận được link thanh toán từ MoMo (timeout). Kiểm tra API đang chạy, cấu hình MoMo và log worker.";
                    return RedirectToAction(nameof(Index), new { poiId });
                }

                if (polled.Value.FailedMessage != null)
                {
                    TempData["Error"] = polled.Value.FailedMessage;
                    return RedirectToAction(nameof(Index), new { poiId });
                }

                payUrl = polled.Value.PayUrl ?? string.Empty;
                deeplink = polled.Value.Deeplink;
                qrCodeUrl = polled.Value.QrCodeUrl;
                checkoutUrl = !string.IsNullOrWhiteSpace(deeplink) ? deeplink : payUrl;
                qrSource = !string.IsNullOrWhiteSpace(qrCodeUrl) ? qrCodeUrl : checkoutUrl;
            }

            if (string.IsNullOrWhiteSpace(checkoutUrl))
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
                Deeplink = deeplink,
                QrCodeUrl = qrCodeUrl,
                QrImageUrl = $"https://quickchart.io/qr?text={Uri.EscapeDataString(qrSource!)}&size=300",
                StatusApiUrl = Url.Action(nameof(GetPaymentStatus), "Premium", new { orderId }, Request.Scheme) ?? string.Empty,
                ReturnUrl = Url.Action(nameof(PaymentReturn), "Premium", new { orderId }, Request.Scheme) ?? $"/payment/return?orderId={Uri.EscapeDataString(orderId)}"
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
            Poi = poi,
            PaidAt = order.PaidAt,
            StatusApiUrl = Url.Action(nameof(GetPaymentStatus), "Premium", new { orderId = order.OrderId }, Request.Scheme) ?? string.Empty
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

    private readonly record struct PolledUrls(string? PayUrl, string? Deeplink, string? QrCodeUrl, string? FailedMessage);

    private static async Task<PolledUrls?> PollForPremiumCheckoutUrlsAsync(
        string baseUrl,
        string internalKey,
        string orderId,
        string vendorUserId,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 60;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{baseUrl.TrimEnd('/')}/api/vendor/premium/order-status-cms");
            request.Headers.Add("X-Internal-Key", internalKey);
            request.Content = JsonContent.Create(new CmsPremiumOrderStatusRequest
            {
                OrderId = orderId,
                VendorUserId = vendorUserId,
                ForceProviderCheck = false
            });

            using var response = await Http.SendAsync(request, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                await Task.Delay(500, cancellationToken);
                continue;
            }

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (!root.TryGetProperty("success", out var okEl) || !okEl.GetBoolean() ||
                !root.TryGetProperty("data", out var dataEl))
            {
                await Task.Delay(500, cancellationToken);
                continue;
            }

            var status = dataEl.TryGetProperty("status", out var stEl) ? stEl.GetString() ?? string.Empty : string.Empty;
            if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                var err = dataEl.TryGetProperty("lastError", out var le) ? le.GetString() : null;
                return new PolledUrls(null, null, null, string.IsNullOrWhiteSpace(err) ? "Tạo thanh toán MoMo thất bại." : err);
            }

            var payUrl = dataEl.TryGetProperty("payUrl", out var pEl) ? pEl.GetString() : null;
            var deeplink = dataEl.TryGetProperty("deeplink", out var dEl) ? dEl.GetString() : null;
            var qrCodeUrl = dataEl.TryGetProperty("qrCodeUrl", out var qEl) ? qEl.GetString() : null;
            if (!string.IsNullOrWhiteSpace(deeplink) || !string.IsNullOrWhiteSpace(payUrl))
                return new PolledUrls(payUrl, deeplink, qrCodeUrl, null);

            await Task.Delay(500, cancellationToken);
        }

        return null;
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

    private sealed class CmsPremiumOrderStatusRequest
    {
        public string OrderId { get; set; } = string.Empty;
        public string VendorUserId { get; set; } = string.Empty;
        public bool ForceProviderCheck { get; set; }
    }
}
