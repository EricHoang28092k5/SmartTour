using DotNetEnv;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartTourBackend.Data;
using SmartTourBackend.Services;
using CloudinaryDotNet;

var builder = WebApplication.CreateBuilder(args);
Env.Load();

builder.Services.AddCors(options => {
    options.AddPolicy("AllowAll", b => b.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});
builder.Services.AddControllersWithViews();

// DÙNG LẠI KẾT NỐI POSTGRESQL CỦA BẠN MÀY
var dbConnectionString = NormalizeDbConnectionString(
    ReadDbConnectionFromDotEnv()
    ?? Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("DefaultConnection"));

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(dbConnectionString));

builder.Services.AddIdentity<IdentityUser, IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// Cloudinary + VoiceService cho API audio.
// Quan trọng: khi chạy EF design-time (migration), có thể thiếu Cloudinary env.
// Không được để app crash tại startup vì EF cần tạo DbContext trước.
var cloudName = Environment.GetEnvironmentVariable("CLOUDINARY_CLOUD_NAME") ?? builder.Configuration["Cloudinary:CloudName"];
var apiKey = Environment.GetEnvironmentVariable("CLOUDINARY_API_KEY") ?? builder.Configuration["Cloudinary:ApiKey"];
var apiSecret = Environment.GetEnvironmentVariable("CLOUDINARY_API_SECRET") ?? builder.Configuration["Cloudinary:ApiSecret"];
var hasCloudinaryConfig =
    !string.IsNullOrWhiteSpace(cloudName) &&
    !string.IsNullOrWhiteSpace(apiKey) &&
    !string.IsNullOrWhiteSpace(apiSecret);

if (!hasCloudinaryConfig)
{
    Console.WriteLine("[Startup Warning] Missing Cloudinary config. EF migration can still run, but upload audio/image will fail until config is set.");
}

var cloudinaryAccount = hasCloudinaryConfig
    ? new Account(cloudName, apiKey, apiSecret)
    : new Account("placeholder-cloud", "placeholder-key", "placeholder-secret");
builder.Services.AddSingleton(new Cloudinary(cloudinaryAccount));
builder.Services.AddScoped<IVoiceService, VoiceService>();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(name: "default", pattern: "{controller=Poi}/{action=Index}/{id?}");
app.MapControllers();

app.Run();

static string? NormalizeDbConnectionString(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return raw;
    raw = raw.Trim().Trim('"').Trim();
    try
    {
        var csb = new NpgsqlConnectionStringBuilder(raw);
        var host = (csb.Host ?? string.Empty).Trim().Trim('"').Trim();
        if (host.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(host, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            {
                csb.Host = uri.Host;
                if (uri.Port > 0) csb.Port = uri.Port;
            }
            else
            {
                var plain = host["tcp://".Length..];
                var parts = plain.Split(':', 2);
                csb.Host = parts[0];
                if (parts.Length == 2 && int.TryParse(parts[1], out var port))
                    csb.Port = port;
            }
        }

        if (csb.Host.Contains('/'))
            csb.Host = csb.Host.Split('/')[0];

        csb.Host = csb.Host.Trim().Trim('"');
        return csb.ConnectionString;
    }
    catch
    {
        return raw;
    }
}

static string? ReadDbConnectionFromDotEnv()
{
    try
    {
        var envPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".env");
        if (!File.Exists(envPath))
            envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
        if (!File.Exists(envPath)) return null;

        foreach (var line in File.ReadAllLines(envPath))
        {
            if (!line.StartsWith("DB_CONNECTION_STRING=", StringComparison.Ordinal)) continue;
            var value = line["DB_CONNECTION_STRING=".Length..].Trim();
            return value.Trim().Trim('"');
        }
    }
    catch
    {
        // ignore and fallback
    }

    return null;
}