using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourBackend.Data;

namespace SmartTourBackend.Services;

public record AudioJobPayload(string? Script, string? LanguageCode, int PoiId, int? TranslationId);

public interface IAudioPipelineQueue
{
    Task<long> EnqueueAsync(string jobType, AudioJobPayload payload, CancellationToken cancellationToken = default);
}

public class AudioPipelineQueue : IAudioPipelineQueue
{
    private readonly AppDbContext _db;

    public AudioPipelineQueue(AppDbContext db)
    {
        _db = db;
    }

    public async Task<long> EnqueueAsync(string jobType, AudioJobPayload payload, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var job = new AudioPipelineJob
        {
            JobType = jobType,
            Status = "pending",
            PoiId = payload.PoiId,
            TranslationId = payload.TranslationId,
            PayloadJson = JsonSerializer.Serialize(payload),
            RetryCount = 0,
            MaxRetries = 5,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.AudioPipelineJobs.Add(job);
        await _db.SaveChangesAsync(cancellationToken);
        return job.Id;
    }
}

public class AudioPipelineWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AudioPipelineWorker> _logger;

    public AudioPipelineWorker(IServiceScopeFactory scopeFactory, ILogger<AudioPipelineWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AudioPipelineWorker loop error");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken token)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var voice = scope.ServiceProvider.GetRequiredService<IVoiceService>();

        var now = DateTime.UtcNow;
        var jobs = await db.AudioPipelineJobs
            .Where(j =>
                (j.Status == "pending" || j.Status == "retrying") &&
                (j.NextRetryAt == null || j.NextRetryAt <= now))
            .OrderBy(j => j.CreatedAt)
            .Take(10)
            .ToListAsync(token);

        foreach (var job in jobs)
        {
            if (token.IsCancellationRequested) break;
            await ProcessJobAsync(db, voice, job, token);
        }
    }

    private static async Task ProcessJobAsync(AppDbContext db, IVoiceService voice, AudioPipelineJob job, CancellationToken token)
    {
        job.Status = "processing";
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(token);

        try
        {
            var payload = JsonSerializer.Deserialize<AudioJobPayload>(job.PayloadJson);
            if (payload == null || string.IsNullOrWhiteSpace(payload.Script))
                throw new InvalidOperationException("Invalid audio job payload.");

            var lang = string.IsNullOrWhiteSpace(payload.LanguageCode) ? "en-US" : payload.LanguageCode!;
            var audioUrl = await voice.GenerateAndUploadAudio(payload.Script, lang);
            if (string.IsNullOrWhiteSpace(audioUrl))
                throw new InvalidOperationException("Voice service returned empty audio url.");

            if (job.TranslationId.HasValue)
            {
                var translation = await db.PoiTranslations.FirstOrDefaultAsync(t => t.Id == job.TranslationId.Value, token);
                if (translation != null)
                {
                    translation.AudioUrl = audioUrl;
                }
            }

            job.Status = "success";
            job.ProcessedAt = DateTime.UtcNow;
            job.UpdatedAt = DateTime.UtcNow;
            job.LastError = null;
            await db.SaveChangesAsync(token);
        }
        catch (Exception ex)
        {
            job.RetryCount++;
            job.LastError = ex.Message;
            job.UpdatedAt = DateTime.UtcNow;

            if (job.RetryCount >= job.MaxRetries)
            {
                job.Status = "dead_letter";
            }
            else
            {
                job.Status = "retrying";
                var backoffSeconds = Math.Min(300, (int)Math.Pow(2, job.RetryCount) * 5);
                job.NextRetryAt = DateTime.UtcNow.AddSeconds(backoffSeconds);
            }

            await db.SaveChangesAsync(token);
        }
    }
}
