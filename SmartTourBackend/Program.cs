using DotNetEnv;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Microsoft.AspNetCore.RateLimiting;
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

builder.Services.Configure<JwtOptions>(opts =>
{
    opts.Issuer = builder.Configuration["Jwt:Issuer"]
        ?? Environment.GetEnvironmentVariable("JWT_ISSUER")
        ?? "SmartTour";
    opts.Audience = builder.Configuration["Jwt:Audience"]
        ?? Environment.GetEnvironmentVariable("JWT_AUDIENCE")
        ?? "SmartTourClients";
    opts.SecretKey = builder.Configuration["Jwt:SecretKey"]
        ?? Environment.GetEnvironmentVariable("JWT_SECRET")
        ?? "change-this-super-long-secret-key-min-32-bytes";
    if (int.TryParse(builder.Configuration["Jwt:ExpiresMinutes"]
        ?? Environment.GetEnvironmentVariable("JWT_EXPIRES_MINUTES"), out var expires))
    {
        opts.ExpiresMinutes = expires;
    }
});

var jwtIssuer = builder.Configuration["Jwt:Issuer"]
    ?? Environment.GetEnvironmentVariable("JWT_ISSUER")
    ?? "SmartTour";
var jwtAudience = builder.Configuration["Jwt:Audience"]
    ?? Environment.GetEnvironmentVariable("JWT_AUDIENCE")
    ?? "SmartTourClients";
var jwtSecret = builder.Configuration["Jwt:SecretKey"]
    ?? Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? "change-this-super-long-secret-key-min-32-bytes";
var jwtKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = jwtKey,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.AddPolicy("VendorOrAdmin", policy => policy.RequireRole("Vendor", "Admin"));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("AudioListenPolicy", context =>
    {
        var key = context.User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(key))
            key = context.Connection.RemoteIpAddress?.ToString();
        if (string.IsNullOrWhiteSpace(key))
            key = "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: key,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromSeconds(10),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });
});

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
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAudioPipelineQueue, AudioPipelineQueue>();
builder.Services.AddSingleton<IAdminKeyValidator, AdminKeyValidator>();
builder.Services.AddSingleton<RequestMetrics>();
builder.Services.AddHostedService<AudioPipelineWorker>();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.UseCors("AllowAll");
app.UseRateLimiter();

app.Use(async (context, next) =>
{
    var requestId = context.Request.Headers["X-Request-Id"].FirstOrDefault()
        ?? Guid.NewGuid().ToString("N");
    context.Response.Headers["X-Request-Id"] = requestId;
    await next();
});

app.Use(async (context, next) =>
{
    var metrics = context.RequestServices.GetRequiredService<RequestMetrics>();
    var started = DateTime.UtcNow;
    await next();
    var elapsed = (long)(DateTime.UtcNow - started).TotalMilliseconds;
    var endpoint = context.Request.Path.Value ?? "/";
    metrics.Record(endpoint, context.Response.StatusCode, elapsed);
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(name: "default", pattern: "{controller=Poi}/{action=Index}/{id?}");
app.MapControllers();
app.MapGet("/api/metrics", (RequestMetrics metrics) =>
{
    return Results.Text(metrics.ToOpenMetrics(), "text/plain; version=0.0.4");
});

app.MapGet("/api/health", async (AppDbContext db) =>
{
    try
    {
        var dbOk = await db.Database.CanConnectAsync();
        return Results.Ok(new
        {
            status = dbOk ? "Healthy" : "Unhealthy",
            checks = new { database = dbOk ? "Healthy" : "Unhealthy" }
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            status = "Unhealthy",
            checks = new { database = "Unhealthy" },
            error = ex.Message
        }, statusCode: 503);
    }
});

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