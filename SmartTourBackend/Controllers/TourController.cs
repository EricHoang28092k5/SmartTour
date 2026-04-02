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
        public async Task<ActionResult<IEnumerable<Tour>>> GetTours()
        {
            // Lấy danh sách Tour (chưa cần lôi POI ra để giao diện danh sách load cho nhẹ)
            return await _context.Tours.ToListAsync();
        }

        // --- 2. LẤY CHI TIẾT 1 TOUR (KÉO THEO CẢ LỘ TRÌNH POI) ---
        // Link gọi: GET /api/tours/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Tour>> GetTour(int id)
        {
            var tour = await _context.Tours
                .Include(t => t.TourPois) // Móc vào bảng trung gian TourPoi
                    .ThenInclude(tp => tp.Poi) // Từ bảng trung gian, kéo thẳng thông tin POI ra
                .FirstOrDefaultAsync(t => t.Id == id);

            if (tour == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy Tour này!" });
            }

            return Ok(new { success = true, data = tour });
        }

        // --- 3. LẤY DANH SÁCH TOUR DO 1 VENDOR TẠO ---
        // Link gọi: GET /api/tours/vendor/{vendorId}
        [HttpGet("vendor/{vendorId}")]
        public async Task<ActionResult<IEnumerable<Tour>>> GetToursByVendor(string vendorId)
        {
            var tours = await _context.Tours
                .Where(t => t.VendorId == vendorId)
                .ToListAsync();

            if (!tours.Any())
            {
                return Ok(new { success = true, data = new List<Tour>(), message = "Vendor này chưa tạo Tour nào!" });
            }

            return Ok(new { success = true, data = tours });
        }
    }
}