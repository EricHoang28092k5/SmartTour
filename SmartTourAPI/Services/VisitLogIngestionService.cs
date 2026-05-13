using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourAPI.Data;

namespace SmartTourAPI.Services;

public sealed class VisitLogQueueItem
{
    public int PoiId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public double Lat { get; set; }
    public double Lng { get; set; }
    public VisitType VisitType { get; set; }
    public double? SpeedKmh { get; set; }
}

public interface IVisitLogIngestionService
{
    bool TryEnqueue(VisitLogQueueItem item, out string? reason);
}

public interface IVisitLogIngestionQueue
{
    ChannelReader<VisitLogQueueItem> Reader { get; }
}

/// <summary>
/// Hàng đợi bounded (1000), DropOldest khi đầy — nhiều writer, một reader (worker).
/// </summary>
public sealed class VisitLogIngestionService : IVisitLogIngestionService, IVisitLogIngestionQueue
{
    private const int ChannelCapacity = 1000;
    private readonly Channel<VisitLogQueueItem> _channel;
    private readonly ILogger<VisitLogIngestionService> _logger;

    public VisitLogIngestionService(ILogger<VisitLogIngestionService> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<VisitLogQueueItem>(new BoundedChannelOptions(ChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }

    public ChannelReader<VisitLogQueueItem> Reader => _channel.Reader;

    public bool TryEnqueue(VisitLogQueueItem item, out string? reason)
    {
        reason = null;
        if (!_channel.Writer.TryWrite(item))
        {
            reason = "channel_closed";
            _logger.LogWarning("Visit log channel closed; drop item poiId={PoiId}", item.PoiId);
            return false;
        }

        return true;
    }
}

/// <summary>
/// Đọc FIFO, gom tối đa 10 bản ghi trong cửa sổ 500ms sau item đầu, một VisitTime chung khi flush.
/// </summary>
public sealed class VisitLogWorker : BackgroundService
{
    private const int BatchSize = 10;
    private static readonly TimeSpan CollectWindow = TimeSpan.FromMilliseconds(500);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IVisitLogIngestionQueue _queue;
    private readonly ILogger<VisitLogWorker> _logger;

    public VisitLogWorker(
        IServiceScopeFactory scopeFactory,
        IVisitLogIngestionQueue queue,
        ILogger<VisitLogWorker> logger)
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
                var first = await reader.ReadAsync(stoppingToken);
                var batch = new List<VisitLogQueueItem>(BatchSize) { first };
                var windowEnd = DateTime.UtcNow + CollectWindow;

                while (batch.Count < BatchSize && DateTime.UtcNow < windowEnd)
                {
                    if (reader.TryRead(out var next))
                    {
                        batch.Add(next);
                        continue;
                    }

                    await Task.Delay(10, stoppingToken);
                }

                await FlushBatchAsync(batch, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "VisitLogWorker loop error");
            }
        }
    }

    private async Task FlushBatchAsync(List<VisitLogQueueItem> batch, CancellationToken token)
    {
        var visitTime = DateTime.UtcNow;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = batch.Select(i => new VisitLog
            {
                PoiId = i.PoiId,
                UserId = i.UserId ?? string.Empty,
                Lat = i.Lat,
                Lng = i.Lng,
                VisitTime = visitTime,
                VisitType = i.VisitType,
                SpeedKmh = i.SpeedKmh
            }).ToList();

            db.VisitLogs.AddRange(rows);
            await db.SaveChangesAsync(token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Visit log batch insert failed; falling back to per-row inserts");

            foreach (var item in batch)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    db.VisitLogs.Add(new VisitLog
                    {
                        PoiId = item.PoiId,
                        UserId = item.UserId ?? string.Empty,
                        Lat = item.Lat,
                        Lng = item.Lng,
                        VisitTime = visitTime,
                        VisitType = item.VisitType,
                        SpeedKmh = item.SpeedKmh
                    });
                    await db.SaveChangesAsync(token);
                }
                catch (Exception ex2)
                {
                    _logger.LogWarning(ex2, "Visit log row dropped poiId={PoiId}", item.PoiId);
                }
            }
        }
    }
}
