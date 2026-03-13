using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourBackend.Data;
using SmartTour.Shared.Models;

namespace SmartTourBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController] // Thuộc tính này cực quan trọng để máy biết đây là API
    public class PoisController : ControllerBase // Dùng ControllerBase cho nhẹ, vì không cần trả về View
    {
        private readonly AppDbContext _context;
        public PoisController(AppDbContext context) => _context = context;

        // GET: api/Pois
        [HttpGet]
        public async Task<IActionResult> GetPois()
        {
            var data = await _context.Pois.ToListAsync();
            return Ok(data); // Trả về JSON 200 OK
        }
    }
}