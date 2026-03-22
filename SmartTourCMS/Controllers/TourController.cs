using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourBackend.Data;

namespace SmartTourCMS.Controllers
{
    [Authorize(Roles = "Admin,Vendor")]
    public class TourController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public TourController(AppDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // 1. Trang danh sách Tour (Đã lọc theo Vendor)
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");

            var tours = new List<Tour>();

            if (await _userManager.IsInRoleAsync(user, "Admin"))
            {
                tours = await _context.Tours.ToListAsync();
            }
            else
            {
                tours = await _context.Tours.Where(t => t.VendorId == user.Id).ToListAsync();
            }

            return View(tours);
        }

        // 2. Trang Tạo mới (GET)
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            ViewBag.Pois = await _context.Pois.ToListAsync();
            return View();
        }

        // 3. Logic Tạo mới (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Tour tour, int[] selectedPoiIds)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser != null)
            {
                tour.VendorId = currentUser.Id; // Gán chủ sở hữu
            }

            if (ModelState.IsValid)
            {
                _context.Tours.Add(tour);
                await _context.SaveChangesAsync();

                if (selectedPoiIds != null)
                {
                    int order = 1;
                    foreach (var poiId in selectedPoiIds)
                    {
                        _context.TourPois.Add(new TourPoi { TourId = tour.Id, PoiId = poiId, OrderIndex = order++ });
                    }
                    await _context.SaveChangesAsync();
                }
                return RedirectToAction(nameof(Index));
            }
            ViewBag.Pois = await _context.Pois.ToListAsync();
            return View(tour);
        }

        // 4. Chi tiết
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

        // 5. Sửa (GET)
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var tour = await _context.Tours.Include(t => t.TourPois).FirstOrDefaultAsync(t => t.Id == id);
            if (tour == null) return NotFound();

            // KIỂM TRA QUYỀN: Nếu không phải Admin và cũng không phải chủ Tour thì đuổi ra
            var user = await _userManager.GetUserAsync(User);
            if (!await _userManager.IsInRoleAsync(user, "Admin") && tour.VendorId != user.Id)
            {
                return Forbid();
            }

            ViewBag.Pois = await _context.Pois.ToListAsync();
            ViewBag.SelectedPoiIds = tour.TourPois.Select(tp => tp.PoiId).ToList();
            return View(tour);
        }

        // 6. Logic Sửa (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Tour tour, int[] selectedPoiIds)
        {
            if (id != tour.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    // Đảm bảo CreatedAt chuẩn UTC để không bị lỗi Database
                    tour.CreatedAt = DateTime.SpecifyKind(tour.CreatedAt, DateTimeKind.Utc);

                    _context.Update(tour);

                    // Xóa và cập nhật lại POI trung gian
                    var oldPois = _context.TourPois.Where(tp => tp.TourId == id);
                    _context.TourPois.RemoveRange(oldPois);

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

        // 7. Xóa
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var tour = await _context.Tours.FindAsync(id);
            if (tour == null) return NotFound();

            // KIỂM TRA QUYỀN XÓA
            var user = await _userManager.GetUserAsync(User);
            if (!await _userManager.IsInRoleAsync(user, "Admin") && tour.VendorId != user.Id)
            {
                return Forbid();
            }

            var tourPois = _context.TourPois.Where(tp => tp.TourId == id);
            _context.TourPois.RemoveRange(tourPois);

            _context.Tours.Remove(tour);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}