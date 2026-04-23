using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
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

        var settings = ReadSettings(_config);
        if (!settings.IsConfigured)
        {
            TempData["Error"] = "MoMo chưa cấu hình. Vui lòng cập nhật appsettings hoặc biến môi trường.";
            return RedirectToAction(nameof(Index), new { poiId });
        }

        var packages = BuildPackages(_config);
        var package = packages.FirstOrDefault(x => string.Equals(x.Code, packageCode, StringComparison.OrdinalIgnoreCase));
        if (package == null)
        {
            TempData["Error"] = "Gói thanh toán không hợp lệ.";
            return RedirectToAction(nameof(Index), new { poiId });
        }

        var poi = await _db.Pois.FirstOrDefaultAsync(x => x.Id == poiId);
        if (poi == null)
        {
            TempData["Error"] = "Không tìm thấy POI.";
            return RedirectToAction(nameof(Index));
        }

        var isAdmin = User.IsInRole("Admin");
        if (!isAdmin && !string.Equals(poi.VendorId, userId, StringComparison.Ordinal))
            return Forbid();

        var requestId = $"CMSREQ{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var orderId = $"CMSPREM-{poi.Id}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        var orderInfo = $"Nang cap {package.Name} cho POI {poi.Name}";
        var extraData = JsonSerializer.Serialize(new PremiumExtraData
        {
            PoiId = poi.Id,
            VendorUserId = userId,
            Months = package.Months,
            PackageCode = package.Code
        });

        var rawSignature = BuildCreateSignatureString(
            settings.AccessKey,
            package.AmountVnd,
            extraData,
            settings.IpnUrl,
            orderId,
            orderInfo,
            settings.PartnerCode,
            settings.RedirectUrl,
            requestId,
            settings.RequestType);

        var signature = Sign(rawSignature, settings.SecretKey);
        var payload = new
        {
            partnerCode = settings.PartnerCode,
            partnerName = "SmartTour",
            storeId = "SmartTourCMS",
            requestId,
            amount = package.AmountVnd.ToString(),
            orderId,
            orderInfo,
            redirectUrl = settings.RedirectUrl,
            ipnUrl = settings.IpnUrl,
            lang = "vi",
            extraData,
            requestType = settings.RequestType,
            signature
        };

        var order = new VendorPremiumOrder
        {
            OrderId = orderId,
            RequestId = requestId,
            PoiId = poi.Id,
            VendorUserId = poi.VendorId ?? userId,
            Amount = package.AmountVnd,
            Status = "pending",
            Provider = "momo",
            CreatedAt = DateTime.UtcNow
        };

        _db.VendorPremiumOrders.Add(order);
        await _db.SaveChangesAsync();

        try
        {
            using var response = await Http.PostAsJsonAsync(settings.Endpoint, payload);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                order.Status = "failed";
                order.LastError = body;
                order.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                TempData["Error"] = "Không tạo được giao dịch MoMo. Vui lòng thử lại.";
                return RedirectToAction(nameof(Index), new { poiId });
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var resultCode = root.TryGetProperty("resultCode", out var resultCodeEl) ? resultCodeEl.GetInt32() : -1;
            var payUrl = root.TryGetProperty("payUrl", out var payUrlEl) ? payUrlEl.GetString() ?? string.Empty : string.Empty;
            if (resultCode != 0 || string.IsNullOrWhiteSpace(payUrl))
            {
                order.Status = "failed";
                order.LastError = body;
                order.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                TempData["Error"] = "MoMo không trả về link thanh toán hợp lệ.";
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
            order.Status = "failed";
            order.LastError = ex.Message;
            order.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            TempData["Error"] = "Không kết nối được MoMo. Vui lòng thử lại.";
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

    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [HttpPost("/payment/momo-ipn")]
    public async Task<IActionResult> MomoIpn([FromBody] JsonElement payload)
    {
        var settings = ReadSettings(_config);
        if (!settings.IsConfigured)
            return Ok(new { resultCode = 0, message = "ignored" });

        var orderId = GetString(payload, "orderId");
        var requestId = GetString(payload, "requestId");
        var resultCode = GetInt(payload, "resultCode");
        var signature = GetString(payload, "signature");
        var transId = GetLong(payload, "transId");
        var amount = GetString(payload, "amount");
        var message = GetString(payload, "message");
        var orderInfo = GetString(payload, "orderInfo");
        var orderType = GetString(payload, "orderType");
        var extraData = GetString(payload, "extraData");
        var payType = GetString(payload, "payType");
        var responseTime = GetString(payload, "responseTime");
        var partnerCode = GetString(payload, "partnerCode");

        if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(requestId))
            return Ok(new { resultCode = 0, message = "invalid payload" });

        var order = await _db.VendorPremiumOrders.FirstOrDefaultAsync(x => x.OrderId == orderId);
        if (order == null)
            return Ok(new { resultCode = 0, message = "order not found" });

        var rawIpnSignature = BuildIpnSignatureString(
            settings.AccessKey,
            amount,
            extraData,
            message,
            orderId,
            orderInfo,
            orderType,
            partnerCode,
            payType,
            requestId,
            responseTime,
            resultCode,
            transId);

        var expectedSignature = Sign(rawIpnSignature, settings.SecretKey);
        var isSignatureValid = !string.IsNullOrWhiteSpace(signature) &&
                               string.Equals(signature, expectedSignature, StringComparison.OrdinalIgnoreCase);

        order.RawIpnPayload = payload.GetRawText();
        order.UpdatedAt = DateTime.UtcNow;

        if (!isSignatureValid)
        {
            order.Status = "failed";
            order.LastError = "invalid_signature";
            await _db.SaveChangesAsync();
            return Ok(new { resultCode = 0, message = "signature invalid" });
        }

        if (resultCode == 0)
        {
            if (!string.Equals(order.Status, "paid", StringComparison.OrdinalIgnoreCase))
            {
                order.Status = "paid";
                order.PaidAt = DateTime.UtcNow;
                order.MoMoTransId = transId;

                var poi = await _db.Pois.FirstOrDefaultAsync(p => p.Id == order.PoiId);
                if (poi != null)
                {
                    var now = DateTime.UtcNow;
                    var start = poi.PremiumExpiresAt.HasValue && poi.PremiumExpiresAt > now
                        ? poi.PremiumExpiresAt.Value
                        : now;

                    var monthsToAdd = 1;
                    var packageCode = "month";
                    if (!string.IsNullOrWhiteSpace(extraData))
                    {
                        try
                        {
                            var extra = JsonSerializer.Deserialize<PremiumExtraData>(extraData);
                            if (extra != null)
                            {
                                if (extra.Months > 0) monthsToAdd = extra.Months;
                                if (!string.IsNullOrWhiteSpace(extra.PackageCode)) packageCode = extra.PackageCode;
                            }
                        }
                        catch
                        {
                            // keep default
                        }
                    }

                    poi.IsPremium = true;
                    poi.PremiumActivatedAt ??= now;
                    poi.PremiumExpiresAt = string.Equals(packageCode, "week", StringComparison.OrdinalIgnoreCase)
                        ? start.AddDays(7)
                        : start.AddMonths(monthsToAdd);
                }
            }
        }
        else
        {
            order.Status = "failed";
            order.LastError = message;
        }

        await _db.SaveChangesAsync();
        return Ok(new { resultCode = 0, message = "ok" });
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

    private static PremiumSettings ReadSettings(IConfiguration config)
    {
        var section = config.GetSection("MoMo");
        return new PremiumSettings
        {
            Endpoint = section["Endpoint"] ?? Environment.GetEnvironmentVariable("MOMO_ENDPOINT") ?? string.Empty,
            PartnerCode = section["PartnerCode"] ?? Environment.GetEnvironmentVariable("MOMO_PARTNER_CODE") ?? string.Empty,
            AccessKey = section["AccessKey"] ?? Environment.GetEnvironmentVariable("MOMO_ACCESS_KEY") ?? string.Empty,
            SecretKey = section["SecretKey"] ?? Environment.GetEnvironmentVariable("MOMO_SECRET_KEY") ?? string.Empty,
            RedirectUrl = section["RedirectUrl"] ?? Environment.GetEnvironmentVariable("MOMO_REDIRECT_URL") ?? string.Empty,
            IpnUrl = section["IpnUrl"] ?? Environment.GetEnvironmentVariable("MOMO_IPN_URL") ?? string.Empty,
            RequestType = section["RequestType"] ?? "captureWallet"
        };
    }

    private static string BuildCreateSignatureString(
        string accessKey,
        long amount,
        string extraData,
        string ipnUrl,
        string orderId,
        string orderInfo,
        string partnerCode,
        string redirectUrl,
        string requestId,
        string requestType)
    {
        return $"accessKey={accessKey}&amount={amount}&extraData={extraData}&ipnUrl={ipnUrl}&orderId={orderId}&orderInfo={orderInfo}&partnerCode={partnerCode}&redirectUrl={redirectUrl}&requestId={requestId}&requestType={requestType}";
    }

    private static string BuildIpnSignatureString(
        string accessKey,
        string amount,
        string extraData,
        string message,
        string orderId,
        string orderInfo,
        string orderType,
        string partnerCode,
        string payType,
        string requestId,
        string responseTime,
        int resultCode,
        long transId)
    {
        return $"accessKey={accessKey}&amount={amount}&extraData={extraData}&message={message}&orderId={orderId}&orderInfo={orderInfo}&orderType={orderType}&partnerCode={partnerCode}&payType={payType}&requestId={requestId}&responseTime={responseTime}&resultCode={resultCode}&transId={transId}";
    }

    private static string Sign(string rawData, string secretKey)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawData));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GetString(JsonElement payload, string key)
    {
        return payload.TryGetProperty(key, out var val) ? val.GetString() ?? string.Empty : string.Empty;
    }

    private static int GetInt(JsonElement payload, string key)
    {
        if (!payload.TryGetProperty(key, out var val)) return 0;
        if (val.ValueKind == JsonValueKind.Number && val.TryGetInt32(out var n)) return n;
        return int.TryParse(val.GetString(), out var parsed) ? parsed : 0;
    }

    private static long GetLong(JsonElement payload, string key)
    {
        if (!payload.TryGetProperty(key, out var val)) return 0;
        if (val.ValueKind == JsonValueKind.Number && val.TryGetInt64(out var n)) return n;
        return long.TryParse(val.GetString(), out var parsed) ? parsed : 0;
    }

    private sealed class PremiumSettings
    {
        public string Endpoint { get; init; } = string.Empty;
        public string PartnerCode { get; init; } = string.Empty;
        public string AccessKey { get; init; } = string.Empty;
        public string SecretKey { get; init; } = string.Empty;
        public string RedirectUrl { get; init; } = string.Empty;
        public string IpnUrl { get; init; } = string.Empty;
        public string RequestType { get; init; } = "captureWallet";
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(Endpoint) &&
            !string.IsNullOrWhiteSpace(PartnerCode) &&
            !string.IsNullOrWhiteSpace(AccessKey) &&
            !string.IsNullOrWhiteSpace(SecretKey) &&
            !string.IsNullOrWhiteSpace(RedirectUrl) &&
            !string.IsNullOrWhiteSpace(IpnUrl);
    }

    private sealed class PremiumExtraData
    {
        public int PoiId { get; set; }
        public string VendorUserId { get; set; } = string.Empty;
        public int Months { get; set; }
        public string PackageCode { get; set; } = string.Empty;
    }
}
