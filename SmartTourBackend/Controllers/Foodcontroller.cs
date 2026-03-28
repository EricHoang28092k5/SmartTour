using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourBackend.Data;
using SmartTour.Shared.Models;

namespace SmartTourBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController] // Gắn mác này để hệ thống biết đây là API nhả JSON
    public class FoodsController : ControllerBase // API thì dùng ControllerBase cho nhẹ xe
    {
        private readonly AppDbContext _context;

        public FoodsController(AppDbContext context)
        {
            _context = context;
        }

        // --- 1. LẤY TOÀN BỘ MÓN ĂN ---
        // Link gọi: GET /api/foods
        // Ứng dụng: Dùng cho màn hình "Khám phá Ẩm thực" trên App
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Food>>> GetAllFoods()
        {
            return await _context.Food.ToListAsync();
        }

        // --- 2. LẤY THỰC ĐƠN CỦA RIÊNG 1 QUÁN (POI) ---
        // Link gọi: GET /api/foods/menu/5 (Trong đó 5 là ID của Quán)
        // Ứng dụng: Khi khách bấm vào Quán A, App sẽ gọi link này để lấy Menu của đúng Quán A
        [HttpGet("menu/{poiId}")]
        public async Task<IActionResult> GetMenuByPoi(int poiId)
        {
            var menu = await _context.Food
                .Where(f => f.PoiId == poiId)
                .ToListAsync();

            // Nếu quán chưa có món nào
            if (!menu.Any())
            {
                return Ok(new { success = true, data = new List<Food>(), message = "Quán này chưa lên thực đơn!" });
            }

            // Trả về danh sách món ăn cho App
            return Ok(new { success = true, data = menu });
        }
    }
}