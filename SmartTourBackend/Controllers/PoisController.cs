using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourBackend.Data;
using SmartTour.Shared.Models;

namespace SmartTourBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PoisController : ControllerBase
    {
        private readonly AppDbContext _context;

        public PoisController(AppDbContext context)
        {
            _context = context;
        }

        // --- 1. Lấy danh sách toàn bộ địa điểm ---
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Poi>>> GetPois([FromQuery] string? lang = null)
        {
            var requestedLang = NormalizeTranslateLanguageCode(lang);
            var pois = await _context.Pois
                .AsNoTracking()
                .Include(p => p.AudioFiles)
                .Include(p => p.PoiImages)
                .Include(p => p.Foods)
                    .ThenInclude(f => f.FoodTranslations)
                        .ThenInclude(ft => ft.Language)
                .ToListAsync();

            foreach (var poi in pois)
            {
                if (poi.Foods == null) continue;
                foreach (var food in poi.Foods)
                {
                    var translation = food.FoodTranslations?
                        .FirstOrDefault(t => NormalizeTranslateLanguageCode(t.Language?.Code) == requestedLang)
                        ?? food.FoodTranslations?.FirstOrDefault(t => NormalizeTranslateLanguageCode(t.Language?.Code) == "en")
                        ?? food.FoodTranslations?.FirstOrDefault();

                    if (translation != null)
                    {
                        food.Name = string.IsNullOrWhiteSpace(translation.Name) ? food.Name : translation.Name;
                        food.Description = string.IsNullOrWhiteSpace(translation.Description) ? food.Description : translation.Description;
                    }
                }
            }

            return pois;
        }

        // --- 2. Lấy tất cả kịch bản TTS theo ngôn ngữ ---
        [HttpGet("{poiId}/tts-all")]
        public async Task<IActionResult> GetAllTtsScripts(int poiId)
        {
            var translations = await _context.PoiTranslations
                .Include(t => t.Language)
                .Where(t => t.PoiId == poiId)
                .Select(t => new
                {
                    languageCode = t.Language.Code,
                    languageName = t.Language.Name,
                    title = t.Title,
                    ttsScript = t.TtsScript
                })
                .ToListAsync();

            if (!translations.Any())
            {
                return NotFound(new
                {
                    success = false,
                    message = "Địa điểm này chưa có kịch bản thuyết minh nào!"
                });
            }

            return Ok(new
            {
                success = true,
                poiId = poiId,
                totalLanguages = translations.Count,
                data = translations
            });
        }

        // --- 3. 🔥 Nhận thống kê thời gian nghe audio từ app ---
        // POST /api/pois/playlog
        [HttpPost("playlog")]
        public async Task<IActionResult> PostPlayLog([FromBody] PlayLogDto dto)
        {
            if (dto == null || dto.PoiId <= 0)
                return BadRequest(new { success = false, message = "Dữ liệu không hợp lệ." });

            // Validate POI exists
            var poiExists = await _context.Pois.AnyAsync(p => p.Id == dto.PoiId);
            if (!poiExists)
                return NotFound(new { success = false, message = "Không tìm thấy địa điểm." });

            var log = new PlayLog
            {
                PoiId = dto.PoiId,
                Time = dto.Time == default ? DateTime.UtcNow : DateTime.SpecifyKind(dto.Time, DateTimeKind.Utc),
                Lat = dto.Lat,
                Lng = dto.Lng,
                DurationListened = dto.DurationListened,
                // Anonymous — no user/device tracking required
                DeviceId = string.Empty,
                UserId = string.Empty
            };

            _context.PlayLog.Add(log);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                logId = log.Id,
                poiId = log.PoiId,
                durationListened = log.DurationListened
            });
        }

        // --- 4. 🔥 Thống kê tổng thời gian nghe theo POI (dashboard) ---
        // GET /api/pois/stats
        [HttpGet("stats")]
        public async Task<IActionResult> GetListenStats()
        {
            var stats = await _context.PlayLog
                .Join(_context.Pois,
                    l => l.PoiId,
                    p => p.Id,
                    (l, p) => new { l, p }) // <--- INNER JOIN
                .GroupBy(x => new { x.p.Id, x.p.Name })
                .Select(g => new
                {
                    poiId = g.Key.Id,
                    poiName = g.Key.Name,
                    totalPlays = g.Count(),
                    totalSecondsListened = g.Sum(x => x.l.DurationListened),
                    avgSecondsPerPlay = g.Average(x => x.l.DurationListened),
                    lastPlayedAt = g.Max(x => x.l.Time)
                })
                .OrderByDescending(x => x.totalSecondsListened)
                .ToListAsync();

            return Ok(new { success = true, data = stats });
        }

        // --- 5. 🔥 Thống kê cho 1 POI cụ thể ---
        // GET /api/pois/{poiId}/stats
        [HttpGet("{poiId}/stats")]
        public async Task<IActionResult> GetPoiStats(int poiId)
        {
            var logs = await _context.PlayLog
                .Where(l => l.PoiId == poiId)
                .OrderByDescending(l => l.Time)
                .Take(100)
                .ToListAsync();

            if (!logs.Any())
                return Ok(new { success = true, poiId, totalPlays = 0, totalSeconds = 0, data = Array.Empty<object>() });

            return Ok(new
            {
                success = true,
                poiId,
                totalPlays = logs.Count,
                totalSeconds = logs.Sum(l => l.DurationListened),
                avgSeconds = logs.Average(l => l.DurationListened),
                data = logs.Select(l => new
                {
                    l.Id,
                    l.Time,
                    l.DurationListened,
                    l.Lat,
                    l.Lng
                })
            });
        }

        private static string NormalizeTranslateLanguageCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "en";
            var normalized = code.Trim().ToLowerInvariant();
            var dashIndex = normalized.IndexOf('-');
            return dashIndex > 0 ? normalized[..dashIndex] : normalized;
        }
    }

    // DTO nhận từ app — không yêu cầu UserId / DeviceId
    public class PlayLogDto
    {
        public int PoiId { get; set; }
        public DateTime Time { get; set; }
        public double Lat { get; set; }
        public double Lng { get; set; }
        public int DurationListened { get; set; }
    }

}
