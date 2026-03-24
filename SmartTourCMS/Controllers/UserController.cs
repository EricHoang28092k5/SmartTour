using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourCMS.Models;

namespace SmartTourCMS.Controllers
{
    [Authorize(Roles = "Admin")] // Chỉ trùm cuối mới được vào đây
    public class UserController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public UserController(UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
        }

        // --- 1. DANH SÁCH NGƯỜI DÙNG ---
        public async Task<IActionResult> Index()
        {
            var users = await _userManager.Users.ToListAsync();
            return View(users);
        }

        // --- 2. TẠO VENDOR MỚI (GET) ---
        [HttpGet]
        public IActionResult CreateVendor() => View();

        // --- 3. TẠO VENDOR MỚI (POST) ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateVendor(CreateVendorViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = new IdentityUser
            {
                UserName = model.Email,
                Email = model.Email,
                EmailConfirmed = true // Mặc định cho phép login luôn, khỏi bắt xác thực email lằng nhằng
            };

            var result = await _userManager.CreateAsync(user, model.Password);

            if (result.Succeeded)
            {
                // MA THUẬT PHÂN QUYỀN (ĐÃ ĐƯỢC MỞ KHÓA)
                if (!await _roleManager.RoleExistsAsync("Vendor"))
                {
                    await _roleManager.CreateAsync(new IdentityRole("Vendor"));
                }

                // Gắn thẻ ngành "Vendor" cho thanh niên này
                await _userManager.AddToRoleAsync(user, "Vendor");

                // Bật tính năng cho phép khóa tài khoản
                await _userManager.SetLockoutEnabledAsync(user, true);

                TempData["Success"] = "Đã tạo tài khoản Vendor thành công!";
                return RedirectToAction("Index");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        // --- 4. KHÓA / MỞ KHÓA TÀI KHOẢN ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleLock(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            // CHỐNG BÓP DÁI: Ngăn Admin tự bấm nhầm khóa chính mình
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser != null && currentUser.Id == user.Id)
            {
                TempData["Error"] = "Bác ơi cất chìa khóa đi, tự khóa mình là hệ thống mồ côi luôn đấy!";
                return RedirectToAction(nameof(Index));
            }

            if (user.LockoutEnd == null || user.LockoutEnd < DateTimeOffset.UtcNow)
            {
                // Khóa 100 năm
                await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
                TempData["Success"] = $"Đã khóa mỗm tài khoản {user.Email}!";
            }
            else
            {
                // Mở khóa
                await _userManager.SetLockoutEndDateAsync(user, null);
                TempData["Success"] = $"Đã ân xá cho tài khoản {user.Email}!";
            }

            return RedirectToAction(nameof(Index));
        }

        // --- 5. ĐỔI MẬT KHẨU (GET) ---
        [HttpGet]
        public async Task<IActionResult> ResetPassword(string id)
        {
            if (string.IsNullOrEmpty(id)) return NotFound();

            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            var model = new ResetPasswordViewModel { UserId = user.Id };
            return View(model);
        }

        // --- 6. ĐỔI MẬT KHẨU (POST) ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user == null) return NotFound();

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);

            if (result.Succeeded)
            {
                TempData["Success"] = "Đã ép đổi mật khẩu thành công!";
                return RedirectToAction("Index");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }
    }
}