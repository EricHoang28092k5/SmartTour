using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourCMS.Models;

namespace SmartTourCMS.Controllers
{
    [Authorize(Roles = "Admin")] // Chỉ trùm cuối mới được vào đây
    /// <summary>
    /// Quản trị tài khoản vận hành:
    /// - Danh sách user + role + trạng thái lock
    /// - Tạo Admin/Vendor
    /// - Khóa/mở khóa và reset mật khẩu
    /// </summary>
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

            // Phân trang server-side để tránh load toàn bộ user khi dữ liệu lớn.
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

            // ASP.NET Identity lưu role ở bảng riêng => cần gọi GetRolesAsync cho từng user.
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
            //ViewBag.TotalUser = users.Count;
            return View(userRolesList);
        }
        // --- 2. TẠO VENDOR MỚI (GET) ---
        // --- 2. TẠO TÀI KHOẢN MỚI ĐA NĂNG (GET) ---
        [HttpGet]
        public IActionResult CreateVendor() 
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
            if (!ModelState.IsValid)    // kiểm tra xem có lỗi nào từ admin gửi lên không (như thiếu trường, định dạng email sai, password không khớp...)
            {
                ViewBag.Roles = new List<string> { "Admin", "Vendor" };
                return View(model);
            }

            var allowedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) // Chỉ cho phép tạo 2 nhóm tài khoản vận hành.
            {
                "Admin",
                "Vendor"
            };
            if (string.IsNullOrWhiteSpace(model.Role) || !allowedRoles.Contains(model.Role)) // Kiểm tra xem thẻ bài (Role) mà admin chọn có hợp lệ không.
            {
                ModelState.AddModelError(nameof(model.Role), "Chỉ được tạo tài khoản Admin hoặc Vendor.");
                ViewBag.Roles = new List<string> { "Admin", "Vendor" };
                return View(model);
            }

            var user = new IdentityUser // Tạo một user mới với email làm username và email.
            {
                UserName = model.Email,
                Email = model.Email,
                EmailConfirmed = true
            };

            var result = await _userManager.CreateAsync(user, model.Password); // Tạo user với mật khẩu đã nhập. ASP.NET Identity sẽ tự động hash mật khẩu này trước khi lưu vào database.

            if (result.Succeeded) // Nếu tạo user thành công
            {
                // KIỂM TRA VÀ GẮN ĐÚNG CÁI THẺ BÀI MÀ ADMIN ĐÃ CHỌN
                if (!string.IsNullOrEmpty(model.Role)) // Nếu admin đã chọn một thẻ bài (Role) nào đó
                {
                    if (!await _roleManager.RoleExistsAsync(model.Role)) // Kiểm tra xem thẻ bài đó đã tồn tại trong hệ thống chưa, nếu chưa thì tạo mới.
                    {
                        await _roleManager.CreateAsync(new IdentityRole(model.Role));
                    }
                    await _userManager.AddToRoleAsync(user, model.Role);
                }

                await _userManager.SetLockoutEnabledAsync(user, true);

                TempData["Success"] = $"Đã tạo tài khoản {model.Role} thành công!";
                return RedirectToAction("Index");
            }

            foreach (var error in result.Errors) // Nếu có lỗi gì đó trong quá trình tạo user (như email đã tồn tại, mật khẩu không đủ mạnh...), thì sẽ hiển thị lỗi đó cho admin biết.
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            ViewBag.Roles = new List<string> { "Admin", "Vendor" }; // Cần set lại ViewBag.Roles nếu có lỗi để dropdown list vẫn hiển thị đúng.
            return View(model);
        }

        // --- 4. KHÓA / MỞ KHÓA TÀI KHOẢN ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleLock(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            // Không cho tự khóa tài khoản hiện tại để tránh mất quyền quản trị.
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser != null && currentUser.Id == user.Id)
            {
                TempData["Error"] = "không thể khoá tài khoản của bạn!";
                return RedirectToAction(nameof(Index));
            }

            if (user.LockoutEnd == null || user.LockoutEnd < DateTimeOffset.UtcNow)
            {
                // Khóa 100 năm
                await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
                TempData["Success"] = $"Đã khóa  tài khoản {user.Email}!";
            }
            else
            {
                // Mở khóa
                await _userManager.SetLockoutEndDateAsync(user, null);
                TempData["Success"] = $"Đã mở cho tài khoản {user.Email}!";
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

            var model = new ResetPasswordViewModel { UserId = user.Id }; // Chỉ cần truyền UserId để biết đang đổi mật khẩu cho ai, còn mật khẩu mới sẽ do admin nhập vào form.
            return View(model);
        }

        // --- 6. ĐỔI MẬT KHẨU (POST) ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model); // Kiểm tra xem admin đã nhập mật khẩu mới hợp lệ chưa (như đủ mạnh, khớp với xác nhận mật khẩu...). Nếu không hợp lệ thì sẽ hiển thị lỗi và yêu cầu nhập lại.

            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user == null) return NotFound();

            var token = await _userManager.GeneratePasswordResetTokenAsync(user); // ASP.NET Identity sử dụng token để đảm bảo an toàn khi reset mật khẩu, tránh việc ai đó có thể lợi dụng chức năng này để đổi mật khẩu của người khác mà không được phép. Token này sẽ được tạo ra dựa trên thông tin của user và có thời hạn sử dụng nhất định.
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