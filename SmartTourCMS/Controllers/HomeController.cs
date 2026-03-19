using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourBackend.Data;
using System.Diagnostics;
using System.Linq;

namespace SmartTourCMS.Controllers;

[Authorize(Roles = "Admin,Vendor")]
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly AppDbContext _context;

    public HomeController(ILogger<HomeController> logger, AppDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var userRole = User.IsInRole("Admin") ? "Admin" : "Vendor";
        var userName = User.Identity?.Name;

        // 1. Thống kê số lượng theo quyền
        if (userRole == "Admin")
        {
            ViewBag.TotalPois = await _context.Pois.CountAsync();
            ViewBag.TotalTours = await _context.Tours.CountAsync();
            ViewBag.TotalTranslations = await _context.PoiTranslations.CountAsync();
            ViewBag.TotalLanguages = await _context.Languages.CountAsync();

            // Giả lập dữ liệu từ App (sau này bác query từ bảng VisitLogs)
            ViewBag.TotalVisits = 1250;
        }
        else
        {
            // Vendor chỉ thấy hàng của mình (Giả sử có cột CreatedBy)
            ViewBag.TotalPois = await _context.Pois.CountAsync(p => p.CreatedBy == userName);
            ViewBag.TotalTours = await _context.Tours.CountAsync(t => t.CreatedBy == userName);
            ViewBag.TotalTranslations = await _context.PoiTranslations
                .CountAsync(pt => _context.Pois.Any(p => p.Id == pt.PoiId && p.CreatedBy == userName));
            ViewBag.TotalLanguages = await _context.Languages.CountAsync();

            ViewBag.TotalVisits = 450; // Lượt ghé thăm riêng của Vendor này
        }

        // 2. Lấy 5 Tour mới nhất (Lọc theo quyền)
        var tourQuery = _context.Tours.AsQueryable();
        if (userRole == "Vendor")
        {
            tourQuery = tourQuery.Where(t => t.CreatedBy == userName);
        }

        var recentTours = await tourQuery
            .Include(t => t.TourPois)
            .OrderByDescending(t => t.CreatedAt)
            .Take(5)
            .ToListAsync();

        return View(recentTours);
    }

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new SmartTourCMS.Models.ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }
}