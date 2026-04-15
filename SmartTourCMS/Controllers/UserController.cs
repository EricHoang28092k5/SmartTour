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
        // --- 1. DANH SÁCH NGƯỜI DÙNG (Bản nâng cấp) ---
        public async Task<IActionResult> Index(int page = 1, int pageSize = 10)
        {
            if (page < 1) page = 1;
            if (pageSize <= 0) pageSize = 10;
            pageSize = Math.Min(pageSize, 50);

            var totalUsers = await _userManager.Users.CountAsync();
            var totalPages = (int)Math.Ceiling(totalUsers / (double)pageSize);
            if (totalPages == 0) totalPages = 1;
            if (page > totalPages) page = totalPages;

            var users = await _userManager.Users
                .OrderBy(u => u.Email)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            var userRolesList = new List<UserWithRoleViewModel>();

            // Lặp qua từng ông để kiểm tra xem đang cầm thẻ bài gì
            foreach (var user in users)
            {
                // Lôi thẻ bài (Role) của ông này ra
                var roles = await _userManager.GetRolesAsync(user);

                // Kiểm tra xem ông này có đang bị khóa tài khoản không
                var isLocked = user.LockoutEnd != null && user.LockoutEnd > DateTimeOffset.UtcNow;

                userRolesList.Add(new UserWithRoleViewModel
                {
                    Id = user.Id,
                    UserName = user.UserName,
                    Email = user.Email,
                    IsLockedOut = isLocked,
                    Roles = roles
                });
            }

            ViewBag.CurrentPage = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalItems = totalUsers;

            return View(userRolesList);
        }
        // --- 2. TẠO VENDOR MỚI (GET) ---
        // --- 2. TẠO TÀI KHOẢN MỚI ĐA NĂNG (GET) ---
        [HttpGet]
        public IActionResult CreateVendor() // (Bác có thể đổi tên hàm thành CreateUser)
        {
            // Chỉ cho phép tạo 2 nhóm tài khoản vận hành.
            ViewBag.Roles = new List<string> { "Admin", "Vendor" };
            return View();
        }

        // --- 3. TẠO TÀI KHOẢN MỚI ĐA NĂNG (POST) ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateVendor(CreateVendorViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Roles = new List<string> { "Admin", "Vendor" };
                return View(model);
            }

            var allowedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Admin",
                "Vendor"
            };
            if (string.IsNullOrWhiteSpace(model.Role) || !allowedRoles.Contains(model.Role))
            {
                ModelState.AddModelError(nameof(model.Role), "Chỉ được tạo tài khoản Admin hoặc Vendor.");
                ViewBag.Roles = new List<string> { "Admin", "Vendor" };
                return View(model);
            }

            var user = new IdentityUser
            {
                UserName = model.Email,
                Email = model.Email,
                EmailConfirmed = true
            };

            var result = await _userManager.CreateAsync(user, model.Password);

            if (result.Succeeded)
            {
                // KIỂM TRA VÀ GẮN ĐÚNG CÁI THẺ BÀI MÀ ADMIN ĐÃ CHỌN
                if (!string.IsNullOrEmpty(model.Role))
                {
                    if (!await _roleManager.RoleExistsAsync(model.Role))
                    {
                        await _roleManager.CreateAsync(new IdentityRole(model.Role));
                    }
                    await _userManager.AddToRoleAsync(user, model.Role);
                }

                await _userManager.SetLockoutEnabledAsync(user, true);

                TempData["Success"] = $"Đã tạo tài khoản {model.Role} thành công!";
                return RedirectToAction("Index");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            ViewBag.Roles = new List<string> { "Admin", "Vendor" };
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