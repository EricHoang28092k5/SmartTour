using CloudinaryDotNet;
using DotNetEnv;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SmartTourBackend.Data;

Env.Load();
var builder = WebApplication.CreateBuilder(args);

// --- 1. KHAI BÁO DỊCH VỤ ---

builder.Services.AddControllersWithViews();

// Kết nối Database Neon
var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// Cấu hình Cloudinary
var cloudinaryAccount = new Account(
    Environment.GetEnvironmentVariable("CLOUDINARY_CLOUD_NAME"),
    Environment.GetEnvironmentVariable("CLOUDINARY_API_KEY"),
    Environment.GetEnvironmentVariable("CLOUDINARY_API_SECRET"));
builder.Services.AddSingleton(new Cloudinary(cloudinaryAccount));

// Cấu hình Identity (Quản lý User & Role)
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options => {
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// Cấu hình Cookie cho Identity (Để không bị đá ra ngoài)
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "SmartTour.Identity";
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

builder.Services.AddAuthorization();

var app = builder.Build();

// --- 2. CẤU HÌNH VẬN HÀNH ---

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// THỨ TỰ SINH TỬ: Authentication phải đứng TRƯỚC Authorization
app.UseAuthentication();
app.UseAuthorization();

// 3. ĐỊNH TUYẾN WEB
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// --- 4. SEED DATA (TẠO ACC ADMIN NẾU CHƯA CÓ) ---
// Đoạn này để bác cứu net nếu tài khoản cũ không vào được
using (var scope = app.Services.CreateScope())
{
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    if (!await roleManager.RoleExistsAsync("Admin")) await roleManager.CreateAsync(new IdentityRole("Admin"));
    if (!await roleManager.RoleExistsAsync("Vendor")) await roleManager.CreateAsync(new IdentityRole("Vendor"));

    var adminEmail = "admin@smarttour.com";
    var adminUser = await userManager.FindByEmailAsync(adminEmail);
    if (adminUser == null)
    {
        var newAdmin = new IdentityUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
        await userManager.CreateAsync(newAdmin, "Admin@123");
        await userManager.AddToRoleAsync(newAdmin, "Admin");
    }
}

app.Run();