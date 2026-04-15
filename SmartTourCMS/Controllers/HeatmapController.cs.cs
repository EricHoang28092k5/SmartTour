using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourBackend.Data; // Tự sửa namespace nếu của mày khác

namespace SmartTourCMS.Controllers
{
    [Authorize(Roles = "Admin")]
    public class HeatmapController : Controller
    {
        private readonly AppDbContext _context;

        public HeatmapController(AppDbContext context)
        {
            _context = context;
        }

        // 1. Trả về Giao diện trang Bản đồ (Cái View ở dưới)
        public IActionResult Index()
        {
            return View();
        }

        // 2. API: Lấy TẤT CẢ địa điểm (Để cắm cọc xanh)
        [HttpGet]
        [Route("api/cms-pois")]
        public async Task<IActionResult> GetAllPois()
        {
            try
            {
                var pois = await _context.Pois
                    .Select(p => new { id = p.Id, name = p.Name, latitude = p.Lat, longitude = p.Lng })
                    .ToListAsync();
                return Ok(new { success = true, data = pois });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        // 3. API: Chỉ lấy địa điểm ĐÃ CÓ TƯƠNG TÁC (Để vẽ Heatmap đỏ rực)
        [HttpGet]
        [Route("api/cms-heatmap")]
        public async Task<IActionResult> GetHeatmapData()
        {
            try
            {
                // Thay 'PoiInteractions' bằng tên bảng Log của mày
                var heatmapData = await (
                    from log in _context.HeatmapEntries
                    join poi in _context.Pois on log.PoiId equals poi.Id
                    group log by new { poi.Id, poi.Lat, poi.Lng } into g
                    select new
                    {
                        poiId = g.Key.Id,
                        lat = g.Key.Lat,
                        lng = g.Key.Lng,
                        sum = g.Count()
                    }
                ).ToListAsync();

                return Ok(new { success = true, data = heatmapData });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }
    }
}