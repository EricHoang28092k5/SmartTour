using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourBackend.Data;
using SmartTour.Shared.Models;

namespace SmartTourCMS.Controllers
{
    public class PoiController : Controller
    {
        private readonly AppDbContext _context;
        public PoiController(AppDbContext context) => _context = context;

        // XEM DANH SÁCH
        public async Task<IActionResult> Index() => View(await _context.Pois.ToListAsync());

        // THÊM MỚI
        public IActionResult Create() => View();
        [HttpPost]
        public async Task<IActionResult> Create(Poi poi)
        {
            _context.Add(poi);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // SỬA (GET: Hiển thị form với dữ liệu cũ)
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var poi = await _context.Pois.FindAsync(id);
            if (poi == null) return NotFound();
            return View(poi);
        }

        // SỬA (POST: Lưu dữ liệu mới)
        [HttpPost]
        public async Task<IActionResult> Edit(int id, Poi poi)
        {
            if (id != poi.Id) return NotFound();
            _context.Update(poi);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // XÓA (Chơi kiểu xóa luôn không cần hỏi nhiều cho nó máu)
        public async Task<IActionResult> Delete(int id)
        {
            var poi = await _context.Pois.FindAsync(id);
            if (poi != null)
            {
                _context.Pois.Remove(poi);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }
    }
}