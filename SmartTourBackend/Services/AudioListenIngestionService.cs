using System.Collections.Concurrent;
using System.Threading.Channels;
using SmartTour.Shared.Models;
using SmartTourBackend.Data;

namespace SmartTourBackend.Services;

public interface IAudioListenIngestionService
{
    bool TryEnqueue(int poiId, int durationSeconds, string deviceId, out string? reason);
}

public interface IAudioListenIngestionQueue
{
    ChannelReader<PoiAudioListenEvent> Reader { get; }
}

public class AudioListenIngestionService : IAudioListenIngestionService, IAudioListenIngestionQueue
{
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan KeyRetention = TimeSpan.FromMinutes(10);
    private const int MaxDedupKeys = 2_000_000;
    private const int CleanupEveryCalls = 20_000;

    private readonly ConcurrentDictionary<string, long> _lastAcceptedTicksByKey = new();
    private readonly Channel<PoiAudioListenEvent> _channel;
    private readonly ILogger<AudioListenIngestionService> _logger;
    private long _acceptCounter;

    public AudioListenIngestionService(ILogger<AudioListenIngestionService> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<PoiAudioListenEvent>(new BoundedChannelOptions(200_000)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });
    }

    public ChannelReader<PoiAudioListenEvent> Reader => _channel.Reader;

    public bool TryEnqueue(int poiId, int durationSeconds, string deviceId, out string? reason)
    {
        reason = null;
        var now = DateTime.UtcNow;
        var nowTicks = now.Ticks;
        var dedupKey = $"{deviceId}:{poiId}";

        var accepted = _lastAcceptedTicksByKey.AddOrUpdate(
            dedupKey,
            static (_, state) => state.nowTicks,
            static (_, oldTicks, state) =>
            {
                var windowStartTicks = state.nowTicks - state.windowTicks;
                return oldTicks >= windowStartTicks ? oldTicks : state.nowTicks;
            },
            (nowTicks: nowTicks, windowTicks: DuplicateWindow.Ticks));

        if (accepted != nowTicks)
        {
            reason = "duplicate_window_15s";
            return false;
        }

        var evt = new PoiAudioListenEvent
        {
            PoiId = poiId,
            DurationSeconds = durationSeconds,
            DeviceId = deviceId,
            CreatedAt = now
        };

        if (!_channel.Writer.TryWrite(evt))
        {
            _lastAcceptedTicksByKey.TryRemove(dedupKey, out _);
            reason = "server_busy_queue_full";
            return false;
        }

        MaybeCleanup(nowTicks);
        return true;
    }

    private void MaybeCleanup(long nowTicks)
    {
        var count = Interlocked.Increment(ref _acceptCounter);
        if (count % CleanupEveryCalls != 0)
            return;

        if (_lastAcceptedTicksByKey.Count > MaxDedupKeys)
        {
            _logger.LogWarning("Audio dedup key map reached {Count} entries.", _lastAcceptedTicksByKey.Count);
        }

        var minValidTicks = nowTicks - KeyRetention.Ticks;
        foreach (var pair in _lastAcceptedTicksByKey)
        {
            if (pair.Value < minValidTicks)
                _lastAcceptedTicksByKey.TryRemove(pair.Key, out _);
        }
    }
}

public class AudioListenIngestionWorker : BackgroundService
{
    private const int BatchSize = 1000;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(250);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAudioListenIngestionQueue _queue;
    private readonly ILogger<AudioListenIngestionWorker> _logger;

    public AudioListenIngestionWorker(
        IServiceScopeFactory scopeFactory,
        IAudioListenIngestionQueue queue,
        ILogger<AudioListenIngestionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = _queue.Reader;
        var batch = new List<PoiAudioListenEvent>(BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var hasData = await reader.WaitToReadAsync(stoppingToken);
                if (!hasData) break;

                var flushAt = DateTime.UtcNow + FlushInterval;
                while (DateTime.UtcNow <= flushAt && batch.Count < BatchSize && reader.TryRead(out var evt))
                {
                    batch.Add(evt);
                }

                if (batch.Count == 0 && reader.TryRead(out var first))
                {
                    batch.Add(first);
                }

                if (batch.Count > 0)
                {
                    await FlushBatchAsync(batch, stoppingToken);
                    batch.Clear();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AudioListenIngestionWorker loop error");
            }
        }
    }

    private async Task FlushBatchAsync(List<PoiAudioListenEvent> batch, CancellationToken token)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.PoiAudioListenEvents.AddRange(batch);
        await db.SaveChangesAsync(token);
    }
}
