
using Microsoft.EntityFrameworkCore;

using SmartTourBackend.Data;
// Thêm dòng này lên đầu

var builder = WebApplication.CreateBuilder(args);

// 1. Cấu hình CORS (Mở cửa cho App Mobile)
builder.Services.AddCors(options => {
    options.AddPolicy("AllowAll", b => b.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

builder.Services.AddControllersWithViews();

// 2. Kết nối Database Neon
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

app.UseStaticFiles();

// 3. Kích hoạt CORS (Phải nằm giữa Routing và Authorization)
app.UseRouting();
app.UseCors("AllowAll");

app.UseAuthorization();

// 4. Map cả Controller cho Web (View) và API
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Poi}/{action=Index}/{id?}");

app.MapControllers(); // Dòng này để nhận diện API Controller

app.Run();
app.MapControllers(); // Thêm dòng này ngay trên app.Run();