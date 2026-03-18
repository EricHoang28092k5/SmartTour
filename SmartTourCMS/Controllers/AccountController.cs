using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using SmartTourBackend.Data; // Đảm bảo đúng namespace của AppDbContext

namespace SmartTourCMS.Controllers
{
    public class AccountController : Controller
    {
        private readonly AppDbContext _context;

        public AccountController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Login() => View();

        [HttpPost]
        public async Task<IActionResult> Login(string username, string password)
        {
            // Tìm user dựa trên Email hoặc Name (tùy bác chọn làm Username)
            // Ở đây em check theo Name dựa trên ảnh Model của bác
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Name == username && u.PasswordHash == password);

            if (user != null)
            {
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, user.Name),
                    new Claim(ClaimTypes.Email, user.Email ?? ""),
                    new Claim(ClaimTypes.Role, user.Role) // Lấy "Admin" hoặc "Vendor" từ DB
                };

                var identity = new ClaimsIdentity(claims, "CookieAuth");
                var principal = new ClaimsPrincipal(identity);

                await HttpContext.SignInAsync("CookieAuth", principal);
                return RedirectToAction("Index", "Home");
            }

            ViewBag.Error = "Tài khoản hoặc mật khẩu không đúng cu ơi!";
            return View();
        }

        public async Task<IActionResult> Logout()
        {
            // Lệnh xóa sạch Cookie đăng nhập
            await HttpContext.SignOutAsync("CookieAuth");

            // Đuổi về trang Login cho nó chuyên nghiệp
            return RedirectToAction("Login", "Account");
        }

        [HttpGet]
        public IActionResult AccessDenied() => View();
    }
}