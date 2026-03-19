using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

    [HttpPost]
    public async Task<IActionResult> CreateVendor(string email, string password)
    {
        var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await _userManager.CreateAsync(user, password);

        if (result.Succeeded)
        {
            // Gán quyền Vendor ngay và luôn
            await _userManager.AddToRoleAsync(user, "Vendor");
            return RedirectToAction(nameof(Index));
        }

        foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
        return View();
    }

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
}