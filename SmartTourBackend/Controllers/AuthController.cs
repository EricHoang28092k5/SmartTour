using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SmartTourBackend.Models;

namespace SmartTourBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public AuthController(UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
        }

        // --- 1. ĐĂNG KÝ TÀI KHOẢN (Cho khách trên Mobile App) ---
        // POST: api/auth/register
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var userExists = await _userManager.FindByEmailAsync(model.Email);
            if (userExists != null)
                return BadRequest(new { success = false, message = "Email này đã được đăng ký!" });

            var user = new IdentityUser
            {
                Email = model.Email,
                UserName = model.Email,
                SecurityStamp = Guid.NewGuid().ToString()
            };

            var result = await _userManager.CreateAsync(user, model.Password);

            if (!result.Succeeded)
                return BadRequest(new { success = false, message = "Lỗi tạo tài khoản!", errors = result.Errors });

            // Tự động gắn mác "User" cho người mới
            if (!await _roleManager.RoleExistsAsync("User"))
                await _roleManager.CreateAsync(new IdentityRole("User"));

            await _userManager.AddToRoleAsync(user, "User");

            return Ok(new { success = true, message = "Tạo tài khoản thành công! Bác có thể đăng nhập ngay." });
        }

        // --- 2. ĐĂNG NHẬP ---
        // POST: api/auth/login
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var user = await _userManager.FindByEmailAsync(model.Email);

            if (user != null && await _userManager.CheckPasswordAsync(user, model.Password))
            {
                // Kiểm tra xem có bị Admin khóa mõm không
                if (user.LockoutEnd != null && user.LockoutEnd > DateTimeOffset.UtcNow)
                    return Unauthorized(new { success = false, message = "Tài khoản của bác đã bị khóa!" });

                var userRoles = await _userManager.GetRolesAsync(user);

                return Ok(new
                {
                    success = true,
                    message = "Đăng nhập thành công!",
                    data = new
                    {
                        userId = user.Id,
                        email = user.Email,
                        roles = userRoles
                    }
                });
            }

            return Unauthorized(new { success = false, message = "Email hoặc mật khẩu không chính xác!" });
        }

        // --- 3. LẤY THÔNG TIN CÁ NHÂN (PROFILE) ---
        // GET: api/auth/user/{id}
        [HttpGet("user/{id}")]
        public async Task<IActionResult> GetUserProfile(string id)
        {
            var user = await _userManager.FindByIdAsync(id);

            if (user == null)
                return NotFound(new { success = false, message = "Không tìm thấy tài khoản này!" });

            var roles = await _userManager.GetRolesAsync(user);

            return Ok(new
            {
                success = true,
                data = new
                {
                    userId = user.Id,
                    email = user.Email,
                    userName = user.UserName,
                    phoneNumber = user.PhoneNumber ?? "Chưa cập nhật",
                    roles = roles
                }
            });
        }

        // --- 4. LẤY DANH SÁCH TOÀN BỘ KHÁCH HÀNG (USER) ---
        // GET: api/auth/customers
        [HttpGet("customers")]
        public async Task<IActionResult> GetAllCustomers()
        {
            // Chỉ lấy những người cầm thẻ "User"
            var customers = await _userManager.GetUsersInRoleAsync("User");

            if (!customers.Any())
                return Ok(new { success = true, data = new List<object>(), message = "Chưa có khách hàng nào đăng ký!" });

            var result = customers.Select(c => new
            {
                userId = c.Id,
                email = c.Email,
                userName = c.UserName,
                phoneNumber = c.PhoneNumber ?? "Chưa cập nhật"
            });

            return Ok(new
            {
                success = true,
                totalCount = customers.Count,
                data = result
            });
        }
    }
}