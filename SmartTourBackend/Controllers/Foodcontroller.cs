using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourBackend.Data;
using SmartTour.Shared.Models;
using System.Text.Json;

namespace SmartTourBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController] // Gắn mác này để hệ thống biết đây là API nhả JSON
    public class FoodsController : ControllerBase // API thì dùng ControllerBase cho nhẹ xe
    {
        private readonly AppDbContext _context;
        private static readonly HttpClient _httpClient = new HttpClient();

        public FoodsController(AppDbContext context)
        {
            _context = context;
        }

        // --- 1. LẤY TOÀN BỘ MÓN ĂN ---
        // Link gọi: GET /api/foods
        // Ứng dụng: Dùng cho màn hình "Khám phá Ẩm thực" trên App
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Food>>> GetAllFoods([FromQuery] string? lang = null)
        {
            var requestedLang = NormalizeTranslateLanguageCode(lang);
            var foods = await _context.Food
                .AsNoTracking()
                .Include(f => f.FoodTranslations)
                    .ThenInclude(ft => ft.Language)
                .ToListAsync();
            ApplyLocalizedFood(foods, requestedLang);
            return foods;
        }

        // --- 2. LẤY THỰC ĐƠN CỦA RIÊNG 1 QUÁN (POI) ---
        // Link gọi: GET /api/foods/menu/5 (Trong đó 5 là ID của Quán)
        // Ứng dụng: Khi khách bấm vào Quán A, App sẽ gọi link này để lấy Menu của đúng Quán A
        [HttpGet("menu/{poiId}")]
        public async Task<IActionResult> GetMenuByPoi(int poiId, [FromQuery] string? lang = null)
        {
            var requestedLang = NormalizeTranslateLanguageCode(lang);
            var menu = await _context.Food
                .Where(f => f.PoiId == poiId)
                .Include(f => f.FoodTranslations)
                    .ThenInclude(ft => ft.Language)
                .ToListAsync();

            // Nếu thiếu bản dịch theo ngôn ngữ đang chọn, tự tạo ngay để app đổi ngôn ngữ thấy kết quả tức thì.
            if (!string.Equals(requestedLang, "vi", StringComparison.OrdinalIgnoreCase) && menu.Count > 0)
            {
                var changed = false;
                foreach (var food in menu)
                {
                    if (food.FoodTranslations.Any(t => NormalizeTranslateLanguageCode(t.Language?.Code) == requestedLang))
                        continue;

                    var created = await CreateMissingTranslationAsync(food, requestedLang);
                    if (created) changed = true;
                }

                if (changed)
                {
                    await _context.SaveChangesAsync();
                    foreach (var food in menu)
                    {
                        await _context.Entry(food)
                            .Collection(f => f.FoodTranslations)
                            .Query()
                            .Include(t => t.Language)
                            .LoadAsync();
                    }
                }
            }

            ApplyLocalizedFood(menu, requestedLang);

            // Nếu quán chưa có món nào
            if (!menu.Any())
            {
                return Ok(new { success = true, data = new List<Food>(), message = "Quán này chưa lên thực đơn!" });
            }

            // Trả về danh sách món ăn cho App
            return Ok(new { success = true, data = menu });
        }

        private async Task<bool> CreateMissingTranslationAsync(Food food, string requestedLang)
        {
            var language = await _context.Languages
                .FirstOrDefaultAsync(l => NormalizeTranslateLanguageCode(l.Code) == requestedLang);

            if (language == null)
            {
                language = new Language
                {
                    Code = requestedLang,
                    Name = requestedLang.ToUpperInvariant()
                };
                _context.Languages.Add(language);
                await _context.SaveChangesAsync();
            }

            var exists = await _context.FoodTranslations
                .AnyAsync(t => t.FoodId == food.Id && t.LanguageId == language.Id);
            if (exists) return false;

            var name = await TranslateIfNeededAsync(food.Name ?? string.Empty, "vi", requestedLang);
            var desc = await TranslateIfNeededAsync(food.Description ?? string.Empty, "vi", requestedLang);

            _context.FoodTranslations.Add(new FoodTranslation
            {
                FoodId = food.Id,
                LanguageId = language.Id,
                Name = name,
                Description = desc,
                UpdatedAt = DateTime.UtcNow
            });
            return true;
        }

        private async Task<string> TranslateIfNeededAsync(string text, string sourceLang, string targetLang)
        {
            if (string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;
            if (string.Equals(sourceLang, targetLang, StringComparison.OrdinalIgnoreCase))
                return text;
            return await AutoTranslateAsync(text, sourceLang, targetLang);
        }

        private async Task<string> AutoTranslateAsync(string text, string sourceLang, string targetLang)
        {
            if (string.Equals(sourceLang, targetLang, StringComparison.OrdinalIgnoreCase))
                return text;
            try
            {
                var res = await _httpClient.GetStringAsync(
                    $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={Uri.EscapeDataString(sourceLang)}&tl={Uri.EscapeDataString(targetLang)}&dt=t&q={Uri.EscapeDataString(text)}");
                using var doc = JsonDocument.Parse(res);
                return doc.RootElement[0][0][0].GetString() ?? text;
            }
            catch
            {
                return text;
            }
        }

        private static void ApplyLocalizedFood(IEnumerable<Food> foods, string requestedLang)
        {
            foreach (var food in foods)
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

        private static string NormalizeTranslateLanguageCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "en";
            var normalized = code.Trim().ToLowerInvariant();
            var dashIndex = normalized.IndexOf('-');
            return dashIndex > 0 ? normalized[..dashIndex] : normalized;
        }
    }
}