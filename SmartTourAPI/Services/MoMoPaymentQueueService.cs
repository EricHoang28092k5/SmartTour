using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using SmartTourAPI.Data;

namespace SmartTourAPI.Services;

public record MoMoCreatePaymentJob(string OrderId);

public interface IMoMoPaymentQueueService
{
    bool TryEnqueue(MoMoCreatePaymentJob job);
}

public interface IMoMoPaymentQueueReader
{
    ChannelReader<MoMoCreatePaymentJob> Reader { get; }
}

public class MoMoPaymentQueueService : IMoMoPaymentQueueService, IMoMoPaymentQueueReader
{
    private readonly Channel<MoMoCreatePaymentJob> _channel = Channel.CreateBounded<MoMoCreatePaymentJob>(
        new BoundedChannelOptions(100_000)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });

    public ChannelReader<MoMoCreatePaymentJob> Reader => _channel.Reader;

    public bool TryEnqueue(MoMoCreatePaymentJob job)
    {
        return _channel.Writer.TryWrite(job);
    }
}

public class MoMoPaymentWorker : BackgroundService
{
    private static readonly HttpClient Http = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly IMoMoPaymentQueueReader _queue;
    private readonly ILogger<MoMoPaymentWorker> _logger;

    public MoMoPaymentWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        IMoMoPaymentQueueReader queue,
        ILogger<MoMoPaymentWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = _queue.Reader;
        var concurrency = 20;
        using var semaphore = new SemaphoreSlim(concurrency, concurrency);
        var inFlight = new List<Task>(concurrency);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var hasData = await reader.WaitToReadAsync(stoppingToken);
                if (!hasData) break;

                while (reader.TryRead(out var job))
                {
                    await semaphore.WaitAsync(stoppingToken);
                    var task = ProcessOneAsync(job, stoppingToken).ContinueWith(t =>
                    {
                        if (t.IsFaulted && t.Exception is not null)
                        {
                            _logger.LogError(t.Exception, "MoMo worker task error for order {OrderId}", job.OrderId);
                        }
                        semaphore.Release();
                    }, CancellationToken.None);
                    inFlight.Add(task);
                    inFlight.RemoveAll(t => t.IsCompleted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MoMo payment worker loop error");
            }
        }

        await Task.WhenAll(inFlight);
    }

    private async Task ProcessOneAsync(MoMoCreatePaymentJob job, CancellationToken token)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = ReadSettings();
        if (!settings.IsConfigured)
            return;

        var order = await db.VendorPremiumOrders.FirstOrDefaultAsync(x => x.OrderId == job.OrderId, token);
        if (order == null)
            return;

        if (!string.Equals(order.Status, "queued", StringComparison.OrdinalIgnoreCase))
            return;

        order.Status = "creating_provider";
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(token);

        try
        {
            var durationMonths = ExtractDurationMonths(order.OrderId, settings.DefaultMonths);
            var orderInfo = durationMonths == 0
                ? $"Nang cap premium POI {order.PoiId} (goi tuan)"
                : $"Nang cap premium POI {order.PoiId} ({durationMonths} thang)";
            var extraDataRaw = JsonSerializer.Serialize(new PremiumExtraData(order.PoiId, order.VendorUserId, durationMonths));
            var extraData = Convert.ToBase64String(Encoding.UTF8.GetBytes(extraDataRaw));

            var signature = settings.UseLegacyAllInOne
                ? Sign(
                    BuildLegacyCreateSignatureString(
                        settings.PartnerCode,
                        settings.AccessKey,
                        order.RequestId,
                        order.Amount,
                        order.OrderId,
                        orderInfo,
                        settings.RedirectUrl,
                        settings.IpnUrl,
                        extraData),
                    settings.SecretKey)
                : Sign(
                    BuildCreateSignatureString(
                        settings.AccessKey,
                        order.Amount,
                        extraData,
                        settings.IpnUrl,
                        order.OrderId,
                        orderInfo,
                        settings.PartnerCode,
                        settings.RedirectUrl,
                        order.RequestId,
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
                    orderId = order.OrderId,
                    amount = order.Amount.ToString(),
                    orderInfo,
                    requestId = order.RequestId,
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
                    requestId = order.RequestId,
                    amount = order.Amount.ToString(),
                    orderId = order.OrderId,
                    orderInfo,
                    redirectUrl = settings.RedirectUrl,
                    ipnUrl = settings.IpnUrl,
                    lang = "vi",
                    extraData,
                    requestType = settings.RequestType,
                    signature
                };
            }

            using var response = await Http.PostAsJsonAsync(settings.Endpoint, payload, token);
            var body = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode)
            {
                order.Status = "failed";
                order.LastError = body;
                order.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(token);
                return;
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var resultCode = root.TryGetProperty("resultCode", out var resultCodeEl) ? resultCodeEl.GetInt32() : -1;
            var payUrl = root.TryGetProperty("payUrl", out var payUrlEl) ? payUrlEl.GetString() : null;
            var deeplink = root.TryGetProperty("deeplink", out var deeplinkEl) ? deeplinkEl.GetString() : null;
            var qrCodeUrl = root.TryGetProperty("qrCodeUrl", out var qrCodeUrlEl) ? qrCodeUrlEl.GetString() : null;

            if (resultCode != 0 || (string.IsNullOrWhiteSpace(payUrl) && string.IsNullOrWhiteSpace(deeplink) && string.IsNullOrWhiteSpace(qrCodeUrl)))
            {
                order.Status = "failed";
                order.LastError = body;
                order.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(token);
                return;
            }

            order.Status = "awaiting_payment";
            order.RawIpnPayload = body;
            order.LastError = null;
            order.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(token);
        }
        catch (Exception ex)
        {
            order.Status = "failed";
            order.LastError = ex.Message;
            order.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(token);
        }
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
            DefaultMonths = int.TryParse(section["DefaultMonths"], out var months) ? months : 1
        };
    }

    private static int ExtractDurationMonths(string orderId, int fallbackMonths)
    {
        var markerIndex = orderId.LastIndexOf("-M", StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            var raw = orderId[(markerIndex + 2)..];
            if (int.TryParse(raw, out var months))
                return Math.Max(0, months);
        }

        return Math.Max(0, fallbackMonths);
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

    private static string Sign(string rawData, string secretKey)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawData));
        return Convert.ToHexString(hash).ToLowerInvariant();
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
        public int DefaultMonths { get; init; } = 1;
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(Endpoint) &&
            !string.IsNullOrWhiteSpace(PartnerCode) &&
            !string.IsNullOrWhiteSpace(AccessKey) &&
            !string.IsNullOrWhiteSpace(SecretKey) &&
            !string.IsNullOrWhiteSpace(RedirectUrl) &&
            !string.IsNullOrWhiteSpace(IpnUrl);
    }
}

public record PremiumExtraData(int PoiId, string VendorUserId, int Months);
