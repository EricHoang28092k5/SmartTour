using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SmartTourCMS.Controllers
{
    [AllowAnonymous]
    public class QrController : Controller
    {
        [HttpGet]
        public IActionResult Open(string type, int id)
        {
            var normalized = (type ?? string.Empty).Trim().ToLowerInvariant();
            if ((normalized != "poi" && normalized != "tour") || id <= 0)
            {
                return BadRequest("QR không hợp lệ.");
            }

            ViewBag.Type = normalized;
            ViewBag.Id = id;
            ViewBag.DeepLink = $"smarttour://{normalized}/{id}";
            return View();
        }
    }
}

