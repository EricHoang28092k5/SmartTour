using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;

namespace SmartTourBackend.Data
{
    public class AppDbContext : IdentityDbContext<IdentityUser>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // --- CÁC BẢNG CŨ (PHẢI CÓ THÌ MỚI HẾT LỖI ĐỎ) ---
        public DbSet<Poi> Pois { get; set; }
        public DbSet<HeatmapEntry> HeatmapEntries { get; set; } // Lỗi ở ảnh 10d1a9 là đây!
        public DbSet<Language> Languages { get; set; }
        public DbSet<PoiTranslation> PoiTranslations { get; set; }
        public DbSet<AudioFile> AudioFiles { get; set; }
        public DbSet<Image> Images { get; set; }
        public DbSet<Tour> Tours { get; set; }
        public DbSet<TourPoi> TourPois { get; set; }
        public DbSet<PlayLog> PlayLog { get; set; }
        public DbSet<UserLocationLog> UserLocationLogs { get; set; }
        public DbSet<QrCode> QrCodes { get; set; }
        public DbSet<AppSetting> AppSettings { get; set; }
        public DbSet<PoiImage> PoiImages { get; set; }
        public DbSet<Food> Food { get; set; }
        public DbSet<Category> Category { get; set; }
        public DbSet<TourTranslation> TourTranslations { get; set; }
        public DbSet<RouteSession> RouteSessions { get; set; }
        public DbSet<RouteSessionPoi> RouteSessionPois { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Cấu hình cột AudioUrl mới
            modelBuilder.Entity<PoiTranslation>(entity => {
                entity.Property(e => e.AudioUrl).IsRequired(false);
            });

            // Giữ nguyên logic Route của project cũ
            modelBuilder.Entity<RouteSession>(entity => {
                entity.HasMany(e => e.RouteSessionPois).WithOne().HasForeignKey(p => p.RouteSessionId);
            });
            modelBuilder.Entity<RouteSessionPoi>(entity => {
                entity.Ignore(e => e.Poi);
            });
        }
    }
}
