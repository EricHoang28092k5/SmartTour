using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourBackend.Data;

namespace SmartTourBackend.Controllers;

[ApiController]
[Route("api/vendor/premium")]
[Authorize(Roles = "Vendor,Admin")]
public class VendorPremiumController : ControllerBase
{
    private static readonly HttpClient Http = new();
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public VendorPremiumController(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    [HttpPost("status")]
    public async Task<IActionResult> GetStatus([FromBody] PremiumStatusRequest dto)
    {
        if (dto.PoiId <= 0) return BadRequest(new { success = false, message = "Invalid poiId." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized(new { success = false, message = "Unauthenticated." });

        var poi = await _db.Pois.FirstOrDefaultAsync(p => p.Id == dto.PoiId);
        if (poi == null) return NotFound(new { success = false, message = "POI not found." });

        var isAdmin = User.IsInRole("Admin");
        if (!isAdmin && !string.Equals(poi.VendorId, userId, StringComparison.Ordinal))
            return Forbid();

        var now = DateTime.UtcNow;
        var isActive = poi.IsPremium && poi.PremiumExpiresAt.HasValue && poi.PremiumExpiresAt.Value > now;
        return Ok(new
        {
            success = true,
            data = new
            {
                poiId = poi.Id,
                isPremium = isActive,
                premiumActivatedAt = poi.PremiumActivatedAt,
                premiumExpiresAt = poi.PremiumExpiresAt,
                remainingDays = isActive ? Math.Max(0, (int)Math.Ceiling((poi.PremiumExpiresAt!.Value - now).TotalDays)) : 0
            }
        });
    }

    [HttpPost("create-payment")]
    public async Task<IActionResult> CreatePayment([FromBody] CreatePremiumPaymentRequest dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized(new { success = false, message = "Unauthenticated." });

        var isAdmin = User.IsInRole("Admin");
        return await CreatePaymentCore(dto.PoiId, dto.Months, dto.Amount, userId, isAdmin);
    }

    // CMS proxy endpoint: dùng internal key để gọi backend API từ web MVC
    [AllowAnonymous]
    [HttpPost("create-payment-cms")]
    public async Task<IActionResult> CreatePaymentFromCms([FromBody] CmsCreatePremiumPaymentRequest dto)
    {
        if (dto.PoiId <= 0) return BadRequest(new { success = false, message = "Invalid poiId." });
        if (string.IsNullOrWhiteSpace(dto.VendorUserId))
            return BadRequest(new { success = false, message = "Invalid vendorUserId." });

        var internalKey = Request.Headers["X-Internal-Key"].FirstOrDefault() ?? string.Empty;
        var expected = _config["Admin:ApiKey"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(expected) || !string.Equals(internalKey, expected, StringComparison.Ordinal))
            return Unauthorized(new { success = false, message = "Invalid internal key." });

        return await CreatePaymentCore(dto.PoiId, dto.Months, dto.Amount, dto.VendorUserId.Trim(), false);
    }

    private async Task<IActionResult> CreatePaymentCore(int poiId, int months, int amountRaw, string actorUserId, bool actorIsAdmin)
    {
        if (poiId <= 0) return BadRequest(new { success = false, message = "Invalid poiId." });

        var settings = ReadSettings();
        if (!settings.IsConfigured)
            return BadRequest(new { success = false, message = $"MoMo is not configured. Missing: {string.Join(", ", settings.GetMissingFields())}" });

        var poi = await _db.Pois.FirstOrDefaultAsync(p => p.Id == poiId);
        if (poi == null) return NotFound(new { success = false, message = "POI not found." });
        if (!actorIsAdmin && !string.Equals(poi.VendorId, actorUserId, StringComparison.Ordinal))
            return Forbid();

        var amount = amountRaw > 0 ? amountRaw : settings.DefaultAmount;
        var durationMonths = months;
        if (durationMonths < 0) durationMonths = settings.DefaultMonths;
        var requestId = $"REQ{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var orderId = $"PREM-{poiId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        var orderInfo = durationMonths == 0
            ? $"Nang cap premium POI {poiId} (goi tuan)"
            : $"Nang cap premium POI {poiId} ({durationMonths} thang)";
        var extraDataRaw = JsonSerializer.Serialize(new PremiumExtraData(poiId, actorUserId, durationMonths));
        var extraData = Convert.ToBase64String(Encoding.UTF8.GetBytes(extraDataRaw));

        var signature = settings.UseLegacyAllInOne
            ? Sign(
                BuildLegacyCreateSignatureString(
                    settings.PartnerCode,
                    settings.AccessKey,
                    requestId,
                    amount,
                    orderId,
                    orderInfo,
                    settings.RedirectUrl,
                    settings.IpnUrl,
                    extraData),
                settings.SecretKey)
            : Sign(
                BuildCreateSignatureString(
                    settings.AccessKey,
                    amount,
                    extraData,
                    settings.IpnUrl,
                    orderId,
                    orderInfo,
                    settings.PartnerCode,
                    settings.RedirectUrl,
                    requestId,
                    settings.RequestType),
                settings.SecretKey);

        object payload;
        if (settings.UseLegacyAllInOne)
        {
            payload = new
            {
                accessKey = settings.AccessKey,
                partnerCode = settings.PartnerCode,
                requestType = settings.RequestType,
                notifyUrl = settings.IpnUrl,
                returnUrl = settings.RedirectUrl,
                orderId,
                amount = amount.ToString(),
                orderInfo,
                requestId,
                extraData,
                signature
            };
        }
        else
        {
            payload = new
            {
                partnerCode = settings.PartnerCode,
                partnerName = "SmartTour",
                storeId = "SmartTour",
                requestId,
                amount = amount.ToString(),
                orderId,
                orderInfo,
                redirectUrl = settings.RedirectUrl,
                ipnUrl = settings.IpnUrl,
                lang = "vi",
                extraData,
                requestType = settings.RequestType,
                signature
            };
        }

        var order = new VendorPremiumOrder
        {
            OrderId = orderId,
            RequestId = requestId,
            PoiId = poiId,
            VendorUserId = poi.VendorId ?? actorUserId,
            Amount = amount,
            Provider = "momo",
            Status = "pending",
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
                return BadRequest(new { success = false, message = "MoMo create-payment failed.", providerResponse = body });
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var resultCode = root.TryGetProperty("resultCode", out var resultCodeEl) ? resultCodeEl.GetInt32() : -1;
            var payUrl = root.TryGetProperty("payUrl", out var payUrlEl) ? payUrlEl.GetString() : null;

            if (resultCode != 0 || string.IsNullOrWhiteSpace(payUrl))
            {
                order.Status = "failed";
                order.LastError = body;
                order.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                return BadRequest(new { success = false, message = "MoMo did not return payUrl.", providerResponse = body });
            }

            return Ok(new
            {
                success = true,
                data = new
                {
                    orderId,
                    requestId,
                    amount,
                    payUrl
                }
            });
        }
        catch (Exception ex)
        {
            order.Status = "failed";
            order.LastError = ex.Message;
            order.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return BadRequest(new { success = false, message = "Unable to call MoMo.", error = ex.Message });
        }
    }

    [AllowAnonymous]
    [HttpPost("momo-ipn")]
    public async Task<IActionResult> HandleMomoIpn([FromBody] JsonElement payload)
    {
        var settings = ReadSettings();
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
            return Ok(new { resultCode = 0, message = "missing order/request id" });

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

                var durationMonths = settings.DefaultMonths;
                if (!string.IsNullOrWhiteSpace(extraData))
                {
                    try
                    {
                        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(extraData));
                        var extra = JsonSerializer.Deserialize<PremiumExtraData>(decoded);
                        if (extra is not null)
                            durationMonths = extra.Months;
                    }
                    catch
                    {
                        // Fallback mặc định nếu không giải mã được extraData
                    }
                }

                poi.IsPremium = true;
                poi.PremiumActivatedAt ??= now;
                poi.PremiumExpiresAt = durationMonths == 0
                    ? start.AddDays(7)
                    : start.AddMonths(Math.Max(1, durationMonths));
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

    private PremiumSettings ReadSettings()
    {
        var section = _config.GetSection("MoMo");
        return new PremiumSettings
        {
            Endpoint = section["Endpoint"] ?? Environment.GetEnvironmentVariable("MOMO_ENDPOINT") ?? string.Empty,
            PartnerCode = section["PartnerCode"] ?? Environment.GetEnvironmentVariable("MOMO_PARTNER_CODE") ?? string.Empty,
            AccessKey = section["AccessKey"] ?? Environment.GetEnvironmentVariable("MOMO_ACCESS_KEY") ?? string.Empty,
            SecretKey = section["SecretKey"] ?? Environment.GetEnvironmentVariable("MOMO_SECRET_KEY") ?? string.Empty,
            RedirectUrl = section["RedirectUrl"] ?? Environment.GetEnvironmentVariable("MOMO_REDIRECT_URL") ?? string.Empty,
            IpnUrl = section["IpnUrl"] ?? Environment.GetEnvironmentVariable("MOMO_IPN_URL") ?? string.Empty,
            RequestType = section["RequestType"] ?? "captureWallet",
            UseLegacyAllInOne = bool.TryParse(section["UseLegacyAllInOne"], out var useLegacy) && useLegacy,
            DefaultAmount = int.TryParse(section["DefaultAmount"], out var amount) ? amount : 49000,
            DefaultMonths = int.TryParse(section["DefaultMonths"], out var months) ? months : 1
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

    // Theo mẫu tài liệu All-In-One legacy: partnerCode&accessKey&requestId...
    private static string BuildLegacyCreateSignatureString(
        string partnerCode,
        string accessKey,
        string requestId,
        long amount,
        string orderId,
        string orderInfo,
        string returnUrl,
        string notifyUrl,
        string extraData)
    {
        return $"partnerCode={partnerCode}&accessKey={accessKey}&requestId={requestId}&amount={amount}&orderId={orderId}&orderInfo={orderInfo}&returnUrl={returnUrl}&notifyUrl={notifyUrl}&extraData={extraData}";
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
        public bool UseLegacyAllInOne { get; init; }
        public int DefaultAmount { get; init; } = 49000;
        public int DefaultMonths { get; init; } = 1;
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(Endpoint) &&
            !string.IsNullOrWhiteSpace(PartnerCode) &&
            !string.IsNullOrWhiteSpace(AccessKey) &&
            !string.IsNullOrWhiteSpace(SecretKey) &&
            !string.IsNullOrWhiteSpace(RedirectUrl) &&
            !string.IsNullOrWhiteSpace(IpnUrl);

        public IEnumerable<string> GetMissingFields()
        {
            if (string.IsNullOrWhiteSpace(Endpoint)) yield return "MoMo:Endpoint";
            if (string.IsNullOrWhiteSpace(PartnerCode)) yield return "MoMo:PartnerCode";
            if (string.IsNullOrWhiteSpace(AccessKey)) yield return "MoMo:AccessKey";
            if (string.IsNullOrWhiteSpace(SecretKey)) yield return "MoMo:SecretKey";
            if (string.IsNullOrWhiteSpace(RedirectUrl)) yield return "MoMo:RedirectUrl";
            if (string.IsNullOrWhiteSpace(IpnUrl)) yield return "MoMo:IpnUrl";
        }
    }
}

public class PremiumStatusRequest
{
    public int PoiId { get; set; }
}

public class CreatePremiumPaymentRequest
{
    public int PoiId { get; set; }
    public int Months { get; set; } = 1;
    public int Amount { get; set; }
}

public class CmsCreatePremiumPaymentRequest
{
    public int PoiId { get; set; }
    public int Months { get; set; } = 1;
    public int Amount { get; set; }
    public string VendorUserId { get; set; } = string.Empty;
}

public record PremiumExtraData(int PoiId, string VendorUserId, int Months);
