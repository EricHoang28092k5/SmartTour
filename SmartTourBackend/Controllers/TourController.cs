using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourBackend.Data;
using SmartTour.Shared.Models;

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

        // --- 1. LẤY DANH SÁCH TẤT CẢ CÁC TOUR ---
        // Link gọi: GET /api/tours
        [HttpGet]
        public async Task<IActionResult> GetTours()
        {
            var tours = await _context.Tours
                .Include(t => t.TourPois)
                    .ThenInclude(tp => tp.Poi)
                // Dùng bùa Select để nhào nặn lại JSON, chống lỗi vòng lặp
                .Select(t => new
                {
                    id = t.Id,
                    name = t.Name,
                    description = t.Description,
                    createdAt = t.CreatedAt,
                    vendorId = t.VendorId,
                    // Ép POI ra thành mảng theo đúng thứ tự OrderIndex
                    pois = t.TourPois.OrderBy(tp => tp.OrderIndex).Select(tp => new
                    {
                        poiId = tp.Poi.Id,
                        name = tp.Poi.Name,
                        lat = tp.Poi.Lat,
                        lng = tp.Poi.Lng,
                        orderIndex = tp.OrderIndex
                    }).ToList()
                })
                .ToListAsync();

            return Ok(new { success = true, data = tours });
        }

        // --- 2. LẤY CHI TIẾT 1 TOUR ---
        // Link gọi: GET /api/tours/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetTour(int id)
        {
            var tour = await _context.Tours
                .Include(t => t.TourPois)
                    .ThenInclude(tp => tp.Poi)
                .Where(t => t.Id == id)
                .Select(t => new
                {
                    id = t.Id,
                    name = t.Name,
                    description = t.Description,
                    createdAt = t.CreatedAt,
                    vendorId = t.VendorId,
                    pois = t.TourPois.OrderBy(tp => tp.OrderIndex).Select(tp => new
                    {
                        poiId = tp.Poi.Id,
                        name = tp.Poi.Name,
                        lat = tp.Poi.Lat,
                        lng = tp.Poi.Lng,
                        orderIndex = tp.OrderIndex
                    }).ToList()
                })
                .FirstOrDefaultAsync();

            if (tour == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy Tour này!" });
            }

            return Ok(new { success = true, data = tour });
        }

        // --- 3. LẤY DANH SÁCH TOUR DO 1 VENDOR TẠO ---
        // Link gọi: GET /api/tours/vendor/{vendorId}
        [HttpGet("vendor/{vendorId}")]
        public async Task<IActionResult> GetToursByVendor(string vendorId)
        {
            var tours = await _context.Tours
                .Include(t => t.TourPois)
                    .ThenInclude(tp => tp.Poi)
                .Where(t => t.VendorId == vendorId)
                .Select(t => new
                {
                    id = t.Id,
                    name = t.Name,
                    description = t.Description,
                    createdAt = t.CreatedAt,
                    pois = t.TourPois.OrderBy(tp => tp.OrderIndex).Select(tp => new
                    {
                        poiId = tp.Poi.Id,
                        name = tp.Poi.Name,
                        orderIndex = tp.OrderIndex
                    }).ToList()
                })
                .ToListAsync();

            if (!tours.Any())
            {
                return Ok(new { success = true, data = new List<object>(), message = "Vendor này chưa tạo Tour nào!" });
            }

            return Ok(new { success = true, data = tours });
        }
    }
}