using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourCMS.Models;

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

    // 1. DANH SÁCH NGƯỜI DÙNG
    public async Task<IActionResult> Index()
    {
        var users = await _userManager.Users.ToListAsync();
        return View(users);
    }

    // 2. TẠO VENDOR MỚI (Giao diện)
    public IActionResult CreateVendor() => View();




    // 3. KHÓA / MỞ KHÓA TÀI KHOẢN
    [HttpPost]
    public async Task<IActionResult> ToggleLock(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        if (user.LockoutEnd == null || user.LockoutEnd < DateTime.Now)
        {
            // Khóa vĩnh viễn (hoặc 100 năm cho máu)
            await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
        }
        else
        {
            // Mở khóa
            await _userManager.SetLockoutEndDateAsync(user, null);
        }

        return RedirectToAction(nameof(Index));
    }
    // 1. Hàm GET: Mở cái form lên và truyền ID của ông user bị đổi pass vào
    [HttpGet]
    public async Task<IActionResult> ResetPassword(string id)
    {
        if (string.IsNullOrEmpty(id)) return NotFound();

        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        var model = new ResetPasswordViewModel { UserId = user.Id };
        return View(model);
    }

    // 2. Hàm POST: Xử lý cục dữ liệu admin gửi lên
    [HttpPost]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.FindByIdAsync(model.UserId);
        if (user == null) return NotFound();

        // Ma thuật của Identity: Admin tạo Token reset pass và ép pass mới luôn
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);

        if (result.Succeeded)
        {
            TempData["Success"] = "Đã ép đổi mật khẩu thành công cho tài khoản này!";
            return RedirectToAction("Index"); // Xong thì quay lại bảng danh sách User
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        return View(model);
    }
    // 1. Hàm GET: Mở form tạo tài khoản
    [HttpGet]

    // 2. Hàm POST: Xử lý lưu vào Database và gán quyền
    [HttpPost]
    public async Task<IActionResult> CreateVendor(CreateVendorViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        // Tạo đối tượng User mới (Dùng Email làm UserName luôn cho dễ đăng nhập)
        // Lưu ý: Nếu bác dùng class ApplicationUser thì đổi IdentityUser thành ApplicationUser nhé
        var user = new Microsoft.AspNetCore.Identity.IdentityUser
        {
            UserName = model.Email,
            Email = model.Email
        };

        // Lưu user vào Database
        var result = await _userManager.CreateAsync(user, model.Password);

        if (result.Succeeded)
        {
            // --- ĐOẠN NÀY LÀ MA THUẬT PHÂN QUYỀN ---
            // Kiểm tra xem Role "Vendor" đã tồn tại trong DB chưa, chưa có thì tạo mới
            // (Bác cần có _roleManager được tiêm vào Constructor của Controller để chạy đoạn này)
            /* if (!await _roleManager.RoleExistsAsync("Vendor"))
            {
                await _roleManager.CreateAsync(new Microsoft.AspNetCore.Identity.IdentityRole("Vendor"));
            }

            // Gán Role Vendor cho tài khoản vừa tạo
            await _userManager.AddToRoleAsync(user, "Vendor");
            */
            // ----------------------------------------

            TempData["Success"] = "Đã tạo tài khoản Vendor thành công!";
            return RedirectToAction("Index"); // Xong thì quay về bảng danh sách
        }

        // Nếu mật khẩu quá yếu hoặc email đã tồn tại thì báo lỗi
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        return View(model);
    }
}