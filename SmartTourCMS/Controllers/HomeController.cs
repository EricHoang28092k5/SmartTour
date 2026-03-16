using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourBackend.Data;
using SmartTourCMS.Models;
using System.Diagnostics;
namespace SmartTourCMS.Controllers;

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
        // Đếm số lượng để hiện lên các thẻ thống kê
        ViewBag.TotalPois = await _context.Pois.CountAsync();
        ViewBag.TotalTours = await _context.Tours.CountAsync();
        ViewBag.TotalTranslations = await _context.PoiTranslations.CountAsync();
        ViewBag.TotalLanguages = await _context.Languages.CountAsync();

        // Lấy 5 Tour mới nhất để hiện ở bảng Dashboard
        var recentTours = await _context.Tours
            .Include(t => t.TourPois)
            .OrderByDescending(t => t.CreatedAt)
            .Take(5)
            .ToListAsync();

        return View(recentTours);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new SmartTourCMS.Models.ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }

}
