using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SmartTourCMS.Models;

namespace SmartTourCMS.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;

        public AccountController(SignInManager<IdentityUser> signInManager, UserManager<IdentityUser> userManager)
        {
            _signInManager = signInManager;
            _userManager = userManager;
        }

        // --- 1. ĐĂNG NHẬP (GET) ---
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            // Lưu lại cái link mà user định vào trước khi bị đá ra trang Login
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        // --- 2. ĐĂNG NHẬP (POST) ---
        [HttpPost]
        [ValidateAntiForgeryToken] // Chống tấn công giả mạo cho an toàn
        public async Task<IActionResult> Login(string username, string password, string? returnUrl = null)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ViewBag.Error = "Bác nhập thiếu tài khoản hoặc mật khẩu rồi!";
                return View();
            }

            // Thực hiện đăng nhập
            var result = await _signInManager.PasswordSignInAsync(username, password, isPersistent: true, lockoutOnFailure: false);

            if (result.Succeeded)
            {
                // Nếu có link cũ (ReturnUrl) thì quay lại đó, không thì về Home
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }
                return RedirectToAction("Index", "Home");
            }

            ViewBag.Error = "Tài khoản hoặc mật khẩu không đúng cụ ơi!";
            return View();
        }

        // --- 3. ĐĂNG XUẤT ---
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Login", "Account");
        }

        // --- 4. TRANG BÁO LỖI QUYỀN TRUY CẬP ---
        [HttpGet]
        public IActionResult AccessDenied()
        {
            // Trang này hiện lên khi Vendor cố tình vào trang chỉ dành cho Admin
            return View();
        }

        // --- 5. ĐỔI MẬT KHẨU (GET) ---
        [Authorize]
        [HttpGet]
        public IActionResult ChangePassword() => View();

        // --- 6. ĐỔI MẬT KHẨU (POST) ---
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login");

            var result = await _userManager.ChangePasswordAsync(user, model.OldPassword, model.NewPassword);

            if (result.Succeeded)
            {
                await _signInManager.SignOutAsync();
                TempData["Success"] = "Đổi mật khẩu thành công rồi bác, đăng nhập lại nhé!";
                return RedirectToAction("Login");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("", error.Description);
            }
            return View(model);
        }
    }
}