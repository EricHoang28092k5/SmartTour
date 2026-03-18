using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using SmartTourBackend.Data;

Env.Load();
var builder = WebApplication.CreateBuilder(args);

// --- 1. KHAI BÁO DỊCH VỤ (PHẢI NẰM TRƯỚC BUILDER.BUILD) ---

builder.Services.AddControllersWithViews();

// Kết nối Database
var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// Cấu hình Authentication & Cookie (Gộp chung bản Full vào đây)
builder.Services.AddAuthentication("CookieAuth")
    .AddCookie("CookieAuth", config =>
    {
        config.Cookie.Name = "Admin.Cookie";
        config.LoginPath = "/Account/Login";         // Trang đăng nhập
        config.AccessDeniedPath = "/Account/AccessDenied"; // Trang báo lỗi phân quyền
        config.ExpireTimeSpan = TimeSpan.FromHours(8);     // Cho login 8 tiếng đi ngủ cho sướng
    });

// --- DÒNG CHIA CẮT SINH TỬ ---
var app = builder.Build();

// --- 2. CẤU HÌNH VẬN HÀNH (PHẢI NẰM SAU BUILDER.BUILD) ---

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// THỨ TỰ QUAN TRỌNG: Authentication trước -> Authorization sau
app.UseAuthentication();
app.UseAuthorization();

// 3. ĐỊNH TUYẾN WEB
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}"); // Để Home/Index cho chuyên nghiệp bác ạ

app.Run();