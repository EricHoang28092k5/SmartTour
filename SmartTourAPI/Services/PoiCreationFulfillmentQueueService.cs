using System.Threading.Channels;

namespace SmartTourAPI.Services;

public record PoiCreationFulfillmentJob(string OrderId);

public interface IPoiCreationFulfillmentQueueService
{
    bool TryEnqueue(PoiCreationFulfillmentJob job);
}

public interface IPoiCreationFulfillmentQueueReader
{
    ChannelReader<PoiCreationFulfillmentJob> Reader { get; }
}

public class PoiCreationFulfillmentQueueService : IPoiCreationFulfillmentQueueService, IPoiCreationFulfillmentQueueReader
{
    private readonly Channel<PoiCreationFulfillmentJob> _channel = Channel.CreateBounded<PoiCreationFulfillmentJob>(
        new BoundedChannelOptions(10_000)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });

    public ChannelReader<PoiCreationFulfillmentJob> Reader => _channel.Reader;

    public bool TryEnqueue(PoiCreationFulfillmentJob job) => _channel.Writer.TryWrite(job);
}

public class PoiCreationFulfillmentWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPoiCreationFulfillmentQueueReader _queue;
    private readonly ILogger<PoiCreationFulfillmentWorker> _logger;

    public PoiCreationFulfillmentWorker(
        IServiceScopeFactory scopeFactory,
        IPoiCreationFulfillmentQueueReader queue,
        ILogger<PoiCreationFulfillmentWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = _queue.Reader;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var hasData = await reader.WaitToReadAsync(stoppingToken);
                if (!hasData) break;

                while (reader.TryRead(out var job))
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var processor = scope.ServiceProvider.GetRequiredService<PoiCreationFulfillmentProcessor>();
                        await processor.FulfillAsync(job.OrderId, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Poi creation fulfillment error for order {OrderId}", job.OrderId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Poi creation fulfillment worker loop error");
            }
        }
    }
}
