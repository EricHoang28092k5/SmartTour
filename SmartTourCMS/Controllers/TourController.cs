using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourBackend.Data;
// Nhớ đổi namespace cho đúng với project CMS của bác
namespace SmartTourCMS.Controllers
{
    [Authorize(Roles = "Admin,Vendor")]
    public class TourController : Controller
    {
        private readonly AppDbContext _context;

        public TourController(AppDbContext context)
        {
            _context = context;
        }

        // 1. Trang danh sách Tour
        public async Task<IActionResult> Index()
        {
            var tours = await _context.Tours
                .Include(t => t.TourPois) // Lấy luôn danh sách POI bên trong
                .ThenInclude(tp => tp.Poi)
                .ToListAsync();
            return View(tours);
        }
        // Trong TourController.cs
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            // Lấy danh sách POI để hiển thị checkbox
            var pois = await _context.Pois.ToListAsync();
            ViewBag.Pois = pois;
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> Create(Tour tour, int[] selectedPoiIds)
        {
            if (ModelState.IsValid)
            {
                // 1. Lưu Tour trước
                _context.Tours.Add(tour);
                await _context.SaveChangesAsync();

                // 2. Lưu các địa điểm được chọn vào bảng trung gian TourPoi
                if (selectedPoiIds != null)
                {
                    int order = 1;
                    foreach (var poiId in selectedPoiIds)
                    {
                        var tourPoi = new TourPoi
                        {
                            TourId = tour.Id,
                            PoiId = poiId,
                            OrderIndex = order++ // Tự tăng thứ tự
                        };
                        _context.TourPois.Add(tourPoi);
                    }
                    await _context.SaveChangesAsync();
                }
                return RedirectToAction(nameof(Index));
            }
            return View(tour);
        }
        // 1. TRANG CHI TIẾT (XEM)
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var tour = await _context.Tours
                .Include(t => t.TourPois.OrderBy(tp => tp.OrderIndex))
                .ThenInclude(tp => tp.Poi)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (tour == null) return NotFound();
            return View(tour);
        }

        // 2. TRANG SỬA (GET)
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var tour = await _context.Tours
                .Include(t => t.TourPois)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (tour == null) return NotFound();

            var allPois = await _context.Pois.ToListAsync();
            ViewBag.Pois = allPois;
            // Lấy danh sách ID các POI đã được chọn sẵn
            ViewBag.SelectedPoiIds = tour.TourPois.Select(tp => tp.PoiId).ToList();

            return View(tour);
        }

        // 3. LOGIC SỬA (POST)
        [HttpPost]
        public async Task<IActionResult> Edit(int id, Tour tour, int[] selectedPoiIds)
        {
            if (id != tour.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    // Ép kiểu lại trước khi lưu
                    tour.CreatedAt = DateTime.SpecifyKind(tour.CreatedAt, DateTimeKind.Utc);
                    _context.Update(tour);
                    await _context.SaveChangesAsync();
                    _context.Update(tour);
                    await _context.SaveChangesAsync();

                    // Xóa hết các POI cũ của Tour này trong bảng trung gian
                    var oldPois = _context.TourPois.Where(tp => tp.TourId == id);
                    _context.TourPois.RemoveRange(oldPois);

                    // Thêm lại đống POI mới được tích
                    if (selectedPoiIds != null)
                    {
                        int order = 1;
                        foreach (var poiId in selectedPoiIds)
                        {
                            _context.TourPois.Add(new TourPoi { TourId = id, PoiId = poiId, OrderIndex = order++ });
                        }
                    }
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Tours.Any(e => e.Id == tour.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(tour);
        }

        // 4. LOGIC XÓA (Nhanh gọn lẹ)
        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var tour = await _context.Tours.FindAsync(id);
            if (tour != null)
            {
                // EF Core sẽ tự động xóa các dòng trong TourPois nếu bác cấu hình Delete Cascade
                // Nếu không, bác nên xóa thủ công TourPois trước
                var tourPois = _context.TourPois.Where(tp => tp.TourId == id);
                _context.TourPois.RemoveRange(tourPois);

                _context.Tours.Remove(tour);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }
    }
}