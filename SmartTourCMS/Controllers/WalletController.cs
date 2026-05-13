using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourAPI.Data;
using SmartTourAPI.Services;
using SmartTourCMS.Models;

namespace SmartTourCMS.Controllers;

[Authorize(Roles = "Vendor")]
public class WalletController : Controller
{
    private static readonly HttpClient Http = new();
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly VendorWalletService _wallet;

    public WalletController(AppDbContext db, IConfiguration config, VendorWalletService wallet)
    {
        _db = db;
        _config = config;
        _wallet = wallet;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var bal = await _wallet.GetBalanceVndAsync(userId);
        var min = long.TryParse(_config["PoiCreation:MinimumWalletTopUpVnd"], out var m) && m > 0 ? m : 20_000L;
        return View(new WalletIndexViewModel { BalanceVnd = bal, MinTopUpVnd = min });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartTopUp([FromForm] long amountVnd)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userId)) return RedirectToAction("Login", "Account");

        var apiSettings = ReadBackendApiSettings(_config);
        if (!apiSettings.IsConfigured)
        {
            TempData["Error"] = "Backend API chưa cấu hình (BackendApi:BaseUrl / InternalKey).";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{apiSettings.BaseUrl.TrimEnd('/')}/api/vendor/premium/wallet/create-topup-cms");
            request.Headers.Add("X-Internal-Key", apiSettings.InternalKey);
            request.Content = JsonContent.Create(new { amount = amountVnd, vendorUserId = userId });

            using var response = await Http.SendAsync(request);
            var raw = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                TempData["Error"] = $"Backend API lỗi ({(int)response.StatusCode}): {raw}";
                return RedirectToAction(nameof(Index));
            }

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var ok = root.TryGetProperty("success", out var successEl) && successEl.GetBoolean();
            if (!ok || !root.TryGetProperty("data", out var dataEl))
            {
                TempData["Error"] = "Backend API trả dữ liệu không hợp lệ.";
                return RedirectToAction(nameof(Index));
            }

            var orderId = dataEl.TryGetProperty("orderId", out var orderEl) ? orderEl.GetString() ?? string.Empty : string.Empty;
            var payUrl = dataEl.TryGetProperty("payUrl", out var payUrlEl) ? payUrlEl.GetString() ?? string.Empty : string.Empty;
            var deeplink = dataEl.TryGetProperty("deeplink", out var deeplinkEl) ? deeplinkEl.GetString() : null;
            var qrCodeUrl = dataEl.TryGetProperty("qrCodeUrl", out var qrCodeUrlEl) ? qrCodeUrlEl.GetString() : null;
            var checkoutUrl = !string.IsNullOrWhiteSpace(deeplink) ? deeplink : payUrl;
            var qrSource = !string.IsNullOrWhiteSpace(qrCodeUrl) ? qrCodeUrl : checkoutUrl;

            if (string.IsNullOrWhiteSpace(orderId))
            {
                TempData["Error"] = "Thiếu mã đơn từ Backend API.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(checkoutUrl))
            {
                var polled = await PollForCheckoutUrlsAsync(
                    apiSettings.BaseUrl,
                    apiSettings.InternalKey,
                    orderId,
                    userId,
                    HttpContext.RequestAborted);
                if (polled == null)
                {
                    TempData["Error"] = "Đã tạo đơn nhưng chưa nhận được link MoMo (timeout).";
                    return RedirectToAction(nameof(Index));
                }

                if (polled.Value.FailedMessage != null)
                {
                    TempData["Error"] = polled.Value.FailedMessage;
                    return RedirectToAction(nameof(Index));
                }

                payUrl = polled.Value.PayUrl ?? string.Empty;
                deeplink = polled.Value.Deeplink;
                qrCodeUrl = polled.Value.QrCodeUrl;
                checkoutUrl = !string.IsNullOrWhiteSpace(deeplink) ? deeplink : payUrl;
                qrSource = !string.IsNullOrWhiteSpace(qrCodeUrl) ? qrCodeUrl : checkoutUrl;
            }

            if (string.IsNullOrWhiteSpace(checkoutUrl))
            {
                TempData["Error"] = "Thiếu link thanh toán MoMo.";
                return RedirectToAction(nameof(Index));
            }

            var amount = dataEl.TryGetProperty("amount", out var aEl) && aEl.TryGetInt64(out var a) ? a : amountVnd;
            var vm = new PremiumCheckoutViewModel
            {
                PoiId = 0,
                PoiName = "Nạp tiền ví vendor",
                PackageName = "MoMo",
                PackageCode = "wallet_topup",
                Months = 0,
                AmountVnd = amount,
                OrderId = orderId,
                PayUrl = payUrl,
                Deeplink = deeplink,
                QrCodeUrl = qrCodeUrl,
                QrImageUrl = $"https://quickchart.io/qr?text={Uri.EscapeDataString(qrSource!)}&size=300",
                StatusApiUrl = Url.Action(nameof(GetTopUpStatus), "Wallet", new { orderId }, Request.Scheme) ?? string.Empty,
                ReturnUrl = Url.Action(nameof(TopUpReturn), "Wallet", new { orderId }, Request.Scheme) ?? string.Empty
            };
            return View("~/Views/Premium/Checkout.cshtml", vm);
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Không kết nối được Backend API: {ex.Message}";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetTopUpStatus(string orderId, bool forceProviderCheck = false)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return BadRequest(new { success = false, message = "Thiếu orderId." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new { success = false, message = "Chưa đăng nhập." });

        var order = await _db.VendorPremiumOrders.AsNoTracking().FirstOrDefaultAsync(x => x.OrderId == orderId);
        if (order == null) return NotFound(new { success = false, message = "Không tìm thấy giao dịch." });
        if (!string.Equals(order.VendorUserId, userId, StringComparison.Ordinal))
            return Forbid();

        var apiSettings = ReadBackendApiSettings(_config);
        if (!apiSettings.IsConfigured)
        {
            return Ok(new
            {
                success = true,
                data = new { orderId = order.OrderId, status = order.Status, paidAt = order.PaidAt }
            });
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{apiSettings.BaseUrl.TrimEnd('/')}/api/vendor/premium/order-status-cms");
            request.Headers.Add("X-Internal-Key", apiSettings.InternalKey);
            request.Content = JsonContent.Create(new
            {
                orderId = orderId.Trim(),
                vendorUserId = userId,
                forceProviderCheck
            });

            using var response = await Http.SendAsync(request);
            var raw = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode, new { success = false, message = raw });

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (!root.TryGetProperty("success", out var okEl) || !okEl.GetBoolean() ||
                !root.TryGetProperty("data", out var dataEl))
                return BadRequest(new { success = false, message = "Dữ liệu không hợp lệ." });

            return Ok(new { success = true, data = dataEl.Clone() });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet]
    public IActionResult TopUpReturn(string? orderId = null)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return RedirectToAction(nameof(Index));
        return Redirect($"/payment/return?orderId={Uri.EscapeDataString(orderId)}");
    }

    private readonly record struct PolledUrls(string? PayUrl, string? Deeplink, string? QrCodeUrl, string? FailedMessage);

    private static async Task<PolledUrls?> PollForCheckoutUrlsAsync(
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
            request.Content = JsonContent.Create(new
            {
                orderId,
                vendorUserId,
                forceProviderCheck = false
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
                return new PolledUrls(null, null, null, string.IsNullOrWhiteSpace(err) ? "Tạo thanh toán thất bại." : err);
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
}
