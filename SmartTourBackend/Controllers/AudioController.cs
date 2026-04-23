using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourBackend.Data;
using SmartTourBackend.Services;

namespace SmartTourBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AudioController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IAudioPipelineQueue _queue;

        public AudioController(AppDbContext context, IAudioPipelineQueue queue)
        {
            _context = context;
            _queue = queue;
        }

        // GET: /api/audio/poi/47
        [HttpGet("poi/{poiId:int}")]
        public async Task<IActionResult> GetPoiAudios(int poiId)
        {
            var rows = await _context.PoiTranslations
                .Include(t => t.Language)
                .Where(t => t.PoiId == poiId)
                .Select(t => new
                {
                    translationId = t.Id,
                    languageCode = t.Language.Code,
                    languageName = t.Language.Name,
                    title = t.Title,
                    ttsScript = t.TtsScript,
                    audioUrl = t.AudioUrl
                })
                .ToListAsync();

            if (!rows.Any())
                return NotFound(new { success = false, message = "POI chưa có bản dịch." });

            return Ok(new
            {
                success = true,
                poiId,
                total = rows.Count,
                withAudio = rows.Count(x => !string.IsNullOrWhiteSpace(x.audioUrl)),
                data = rows
            });
        }

        // POST: /api/audio/poi/47/regenerate?onlyMissing=true
        [HttpPost("poi/{poiId:int}/regenerate")]
        public async Task<IActionResult> RegeneratePoiAudios(int poiId, [FromQuery] bool onlyMissing = true)
        {
            var translations = await _context.PoiTranslations
                .Include(t => t.Language)
                .Where(t => t.PoiId == poiId)
                .ToListAsync();

            if (!translations.Any())
                return NotFound(new { success = false, message = "POI chưa có bản dịch để tạo audio." });

            var candidates = onlyMissing
                ? translations.Where(t => string.IsNullOrWhiteSpace(t.AudioUrl)).ToList()
                : translations;

            var queuedJobs = new List<long>();
            foreach (var t in candidates)
            {
                var script = string.IsNullOrWhiteSpace(t.TtsScript) ? t.Description : t.TtsScript!;
                if (string.IsNullOrWhiteSpace(script)) continue;
                var voiceLang = ResolveVoiceLanguageCode(t.Language?.Code);
                var jobId = await _queue.EnqueueAsync(
                    "full_regenerate",
                    new AudioJobPayload(script, voiceLang, poiId, t.Id));
                queuedJobs.Add(jobId);
            }

            return Ok(new
            {
                success = true,
                poiId,
                processed = candidates.Count,
                queued = queuedJobs.Count,
                jobIds = queuedJobs
            });
        }

        // POST: /api/audio/translation/123/generate
        [HttpPost("translation/{translationId:int}/generate")]
        public async Task<IActionResult> GenerateForTranslation(int translationId)
        {
            var t = await _context.PoiTranslations
                .Include(x => x.Language)
                .FirstOrDefaultAsync(x => x.Id == translationId);

            if (t == null)
                return NotFound(new { success = false, message = "Không tìm thấy bản dịch." });

            var script = string.IsNullOrWhiteSpace(t.TtsScript) ? t.Description : t.TtsScript!;
            if (string.IsNullOrWhiteSpace(script))
                return BadRequest(new { success = false, message = "Bản dịch không có nội dung để tạo audio." });

            var voiceLang = ResolveVoiceLanguageCode(t.Language?.Code);
            var jobId = await _queue.EnqueueAsync(
                "tts_only",
                new AudioJobPayload(script, voiceLang, t.PoiId, t.Id));

            return Ok(new
            {
                success = true,
                translationId = t.Id,
                languageCode = t.Language?.Code,
                jobId
            });
        }

        private static string ResolveVoiceLanguageCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "vi-VN";
            var normalized = code.Trim().ToLowerInvariant();
            if (normalized.Contains('-'))
            {
                var parts = normalized.Split('-', 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 2) return $"{parts[0]}-{parts[1].ToUpperInvariant()}";
            }

            return normalized switch
            {
                "vi" => "vi-VN",
                "en" => "en-US",
                "fr" => "fr-FR",
                "ja" => "ja-JP",
                "ko" => "ko-KR",
                "zh" => "zh-CN",
                _ => "en-US"
            };
        }
    }
}
