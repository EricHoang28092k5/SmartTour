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
        // 1. XEM DANH SÁCH
        public async Task<IActionResult> Index() => View(await _context.Pois.ToListAsync());

        // 2. THÊM MỚI (Giao diện)
        public IActionResult Create() => View();

        // 3. THÊM MỚI (Xử lý lưu)
        [HttpPost]
        public async Task<IActionResult> Create(Poi poi)
        {
            _context.Add(poi);
            await _context.SaveChangesAsync();
            TempData["success"] = "Thêm mới địa điểm thành công rồi cu ơi!";
            return RedirectToAction(nameof(Index));
        }

        // 4. SỬA (Giao diện hiển thị dữ liệu cũ)
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var poi = await _context.Pois.FindAsync(id);
            if (poi == null) return NotFound();
            return View(poi);
        }

        // 5. SỬA (Xử lý lưu dữ liệu mới)
        [HttpPost]
        public async Task<IActionResult> Edit(int id, Poi poi)
        {
            if (id != poi.Id) return NotFound();

            _context.Update(poi);
            await _context.SaveChangesAsync();
            TempData["success"] = "Cập nhật dữ liệu thành công!";
            return RedirectToAction(nameof(Index));
        }

        // 6. XÓA (Xử lý xóa luôn)
        public async Task<IActionResult> Delete(int id)
        {
            var poi = await _context.Pois.FindAsync(id);
            if (poi != null)
            {
                _context.Pois.Remove(poi);
                await _context.SaveChangesAsync();
                TempData["success"] = "Đã xóa sạch địa điểm này!";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}