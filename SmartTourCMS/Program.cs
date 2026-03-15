using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using SmartTourBackend.Data;
Env.Load();
var builder = WebApplication.CreateBuilder(args);

// 1. Thêm dịch vụ giao diện MVC (Cái này cu có rồi)
builder.Services.AddControllersWithViews();

// 2. KẾT NỐI DATABASE (Phải có cái này nó mới lấy được dữ liệu POI từ Neon)
var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

var app = builder.Build();

// Cấu hình môi trường
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles(); // Phải để trước UseRouting
app.UseRouting();
app.UseAuthorization();

// 3. ĐỊNH TUYẾN WEB (Thay vì MapControllers, dùng cái này để chạy View)
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Poi}/{action=Index}/{id?}");

app.Run();