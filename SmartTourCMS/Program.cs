using CloudinaryDotNet;
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

// Cấu hình Cloudinary (Bốc từ dưới lên đây nè bác!)
var cloudinaryAccount = new Account(
    Environment.GetEnvironmentVariable("CLOUDINARY_CLOUD_NAME"),
    Environment.GetEnvironmentVariable("CLOUDINARY_API_KEY"),
    Environment.GetEnvironmentVariable("CLOUDINARY_API_SECRET")
);
builder.Services.AddSingleton(new Cloudinary(cloudinaryAccount));

// Cấu hình Authentication & Cookie
builder.Services.AddAuthentication("CookieAuth")
    .AddCookie("CookieAuth", config =>
    {
        config.Cookie.Name = "Admin.Cookie";
        config.LoginPath = "/Account/Login";
        config.AccessDeniedPath = "/Account/AccessDenied";
        config.ExpireTimeSpan = TimeSpan.FromHours(8);
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

// THỨ TỰ QUAN TRỌNG
app.UseAuthentication();
app.UseAuthorization();

// 3. ĐỊNH TUYẾN WEB
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();