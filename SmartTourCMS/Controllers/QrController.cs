using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SmartTourCMS.Controllers
{
    [AllowAnonymous]
    /// <summary>
    /// Cầu nối QR -> Deep link mobile:
    /// nhận type/id từ QR web và render ra `smarttour://...`
    /// để app MAUI điều hướng đúng POI/Tour.
    /// </summary>
    public class QrController : Controller
    {
        [HttpGet]
        public IActionResult Open(string type, int id)
        {
            var normalized = (type ?? string.Empty).Trim().ToLowerInvariant();
            if ((normalized != "poi" && normalized != "tour") || id <= 0)
            {
                // Validation đầu vào để tránh sinh deep link rác.
                return BadRequest("QR không hợp lệ.");
            }

            ViewBag.Type = normalized;
            ViewBag.Id = id;
            ViewBag.DeepLink = $"smarttour://{normalized}/{id}";
            return View();
        }
    }
}

