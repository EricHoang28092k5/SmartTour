
using Microsoft.AspNetCore.Mvc;
using SmartTourBackend.Data;
using SmartTour.Shared.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SmartTourCMS.Controllers
{
    public class PoiController : Controller
    {
        private readonly AppDbContext _context;

        public PoiController(AppDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            var pois = _context.Pois.ToList();
            return View(pois);
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(Poi poi)
        {
            if (ModelState.IsValid)
            {
                _context.Add(poi);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(poi);
        }
    }
}