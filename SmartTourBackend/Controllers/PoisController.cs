using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourBackend.Data;
using SmartTour.Shared.Models; // Bác nhớ check xem namespace chứa class Poi của bác tên gì nhé

namespace SmartTourBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController] // API nhả ra json
    public class PoisController : ControllerBase // Dùng controller base cho nhẹ
    {
        private readonly AppDbContext _context;

        public PoisController(AppDbContext context)
        {
            _context = context;
        }

        // --- 1. Lấy danh sách toàn bộ địa điểm (Hàm cũ của bác) ---
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Poi>>> GetPois()
        {
            return await _context.Pois
                .Include(p => p.AudioFiles)
                   .Include(p => p.PoiImages)
                .ToListAsync();
         
        }

        // --- 3. GOM TẤT CẢ KỊCH BẢN (MỌI NGÔN NGỮ) VÀO 1 URL ---
        // Link gọi: GET /api/pois/{poiId}/tts-all
        [HttpGet("{poiId}/tts-all")]
        public async Task<IActionResult> GetAllTtsScripts(int poiId)
        {
            // Tìm tất cả các bản dịch của cái POI này, lấy kèm luôn thông tin Ngôn ngữ
            var translations = await _context.PoiTranslations
                .Include(t => t.Language) // Join bảng Language để lấy mã code (vi, en, fr...)
                .Where(t => t.PoiId == poiId)
                .Select(t => new
                {
                    languageCode = t.Language.Code,
                    languageName = t.Language.Name,
                    title = t.Title,
                    ttsScript = t.TtsScript
                })
                .ToListAsync();

            // Nếu rỗng (chưa có bản dịch nào)
            if (!translations.Any())
            {
                return NotFound(new
                {
                    success = false,
                    message = "Địa điểm này chưa có kịch bản thuyết minh nào!"
                });
            }

            // Nếu có dữ liệu, trả về nguyên 1 danh sách (Array) cho App tự xử lý
            return Ok(new
            {
                success = true,
                poiId = poiId,
                totalLanguages = translations.Count, // Báo cho App biết có bao nhiêu thứ tiếng
                data = translations
            });
        }
    }
}