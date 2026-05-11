using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourAPI.Data;

namespace SmartTourCMS.Controllers
{
    [Authorize(Roles = "Admin,Vendor")]
    /// <summary>
    /// Controller này giữ route cũ để tương thích điều hướng,
    /// nhưng nghiệp vụ upload audio thủ công đã bị tắt.
    /// Audio hiện được tạo theo translation/pipeline.
    /// </summary>
    public class AudioController : Controller
    {
        private readonly AppDbContext _context;
        private readonly Cloudinary _cloudinary;
        private readonly UserManager<IdentityUser> _userManager;

        public AudioController(AppDbContext context, UserManager<IdentityUser> userManager, IConfiguration configuration)
        {
            _context = context;
            _userManager = userManager;

            // Vẫn khởi tạo Cloudinary để không phá constructor cũ, dù luồng upload đã disable.
            var account = new Account(
                configuration["Cloudinary:CloudName"] ?? Environment.GetEnvironmentVariable("CLOUDINARY_CLOUD_NAME"),
                configuration["Cloudinary:ApiKey"] ?? Environment.GetEnvironmentVariable("CLOUDINARY_API_KEY"),
                configuration["Cloudinary:ApiSecret"] ?? Environment.GetEnvironmentVariable("CLOUDINARY_API_SECRET")
            );
            _cloudinary = new Cloudinary(account);
        }

        // --- 1. TRANG UPLOAD (GET) ---
        public IActionResult Upload()
        {
            TempData["Error"] = "Chức năng upload audio thủ công đã được tắt. Vui lòng dùng tính năng tạo audio tự động theo translation.";
            return RedirectToAction("Index", "Poi");
        }

        // --- 2. LOGIC UPLOAD (POST) ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Upload(int poiId, IFormFile audioFile)
        {
            TempData["Error"] = "Chức năng upload audio thủ công đã được tắt. Vui lòng dùng tính năng tạo audio tự động theo translation.";
            return RedirectToAction("Index", "Poi");
        }
    }
}