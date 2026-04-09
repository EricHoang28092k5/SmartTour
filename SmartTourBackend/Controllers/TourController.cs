using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourBackend.Data;

namespace SmartTourBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ToursController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ToursController(AppDbContext context)
        {
            _context = context;
        }

        // ==============================================================
        // 1. LẤY DANH SÁCH TOUR (Hiển thị ngoài màn hình chính của App)
        // ==============================================================
        // Link gọi: GET /api/tours
        [HttpGet]
        public async Task<IActionResult> GetTours()
        {
            var tours = await _context.Tours
                .Include(t => t.TourTranslations) // Kéo đống bản dịch lên
                .Include(t => t.TourPois)
                    .ThenInclude(tp => tp.Poi)
                .Select(t => new
                {
                    id = t.Id,
                    name = t.Name, // Tên gốc tiếng Việt
                    description = t.Description,
                    createdAt = t.CreatedAt,
                    vendorId = t.VendorId,

                    // Gói đống bản dịch vào một mảng cho App nó tự chọn ngôn ngữ
                    translations = t.TourTranslations.Select(tr => new {
                        lang = tr.LanguageCode,
                        name = tr.Name,
                        description = tr.Description
                    }).ToList(),

                    // Trả luôn danh sách POI để app/web không cần gọi thêm endpoint chi tiết
                    pois = t.TourPois
                        .OrderBy(tp => tp.OrderIndex)
                        .Select(tp => new
                        {
                            poiId = tp.PoiId,
                            name = tp.Poi != null ? tp.Poi.Name : string.Empty,
                            lat = tp.Poi != null ? tp.Poi.Lat : 0,
                            lng = tp.Poi != null ? tp.Poi.Lng : 0,
                            orderIndex = tp.OrderIndex
                        }).ToList()
                })
                .ToListAsync();

            return Ok(new { success = true, data = tours });
        }

        // ==============================================================
        // 2. LẤY CHI TIẾT 1 TOUR (Bấm vào xem chi tiết, hiện cả bản đồ)
        // ==============================================================
        // Link gọi: GET /api/tours/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetTour(int id)
        {
            var tour = await _context.Tours
                .Include(t => t.TourTranslations)
                .Include(t => t.TourPois)
                    .ThenInclude(tp => tp.Poi) // Móc lốp lấy tọa độ POI
                .Where(t => t.Id == id)
                .Select(t => new
                {
                    id = t.Id,
                    name = t.Name,
                    description = t.Description,
                    createdAt = t.CreatedAt,
                    vendorId = t.VendorId,

                    translations = t.TourTranslations.Select(tr => new {
                        lang = tr.LanguageCode,
                        name = tr.Name,
                        description = tr.Description
                    }).ToList(),

                    // Ép danh sách POI (điểm đến) ra theo đúng thứ tự 1, 2, 3...
                    pois = t.TourPois.OrderBy(tp => tp.OrderIndex).Select(tp => new
                    {
                        poiId = tp.Poi.Id,
                        name = tp.Poi.Name, // Cảnh báo: Nếu mảng POI mày cũng có dịch thì tự nhét thêm translations vào đây nhé!
                        lat = tp.Poi.Lat,
                        lng = tp.Poi.Lng,
                        orderIndex = tp.OrderIndex
                    }).ToList()
                })
                .FirstOrDefaultAsync();

            if (tour == null)
            {
                return NotFound(new { success = false, message = "Đéo tìm thấy Tour này!" });
            }

            return Ok(new { success = true, data = tour });
        }

        // ==============================================================
        // 3. LẤY DANH SÁCH TOUR CỦA 1 VENDOR (Dành cho App quản lý)
        // ==============================================================
        // Link gọi: GET /api/tours/vendor/{vendorId}
        [HttpGet("vendor/{vendorId}")]
        public async Task<IActionResult> GetToursByVendor(string vendorId)
        {
            var tours = await _context.Tours
                .Include(t => t.TourTranslations)
                .Where(t => t.VendorId == vendorId)
                .Select(t => new
                {
                    id = t.Id,
                    name = t.Name,
                    translations = t.TourTranslations.Select(tr => new {
                        lang = tr.LanguageCode,
                        name = tr.Name
                    }).ToList()
                })
                .ToListAsync();

            return Ok(new { success = true, data = tours });
        }
    }
}