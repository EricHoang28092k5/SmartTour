using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourBackend.Data; // Nhớ đổi namespace cho đúng với project của bác

namespace SmartTourCMS.Controllers
{
    public class LogController : Controller
    {
        private readonly AppDbContext _context;

        public LogController(AppDbContext context)
        {
            _context = context;
        }

        // 1. Xem lịch sử di chuyển (Locations)
        public async Task<IActionResult> Locations()
        {
            var logs = await _context.UserLocationLogs
                .OrderByDescending(l => l.Timestamp)
                .Take(100) // Lấy 100 bản ghi mới nhất cho đỡ nặng máy
                .ToListAsync();

            return View(logs);
        }

        // 2. Xem lịch sử nghe Audio (Plays)
        public async Task<IActionResult> Plays()
        {
            var logs = await _context.PlayLog
                .Include(l => l.Poi) // Lấy thêm thông tin địa điểm để hiện tên cho oai
                .OrderByDescending(l => l.Time)
                .Take(100)
                .ToListAsync();

            return View(logs);
        }
    }
}