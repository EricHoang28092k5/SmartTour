using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;

namespace SmartTourBackend.Data
{
    public class AppDbContext : IdentityDbContext<IdentityUser>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        // ─── Existing tables ───
        public DbSet<Poi> Pois { get; set; }
        public DbSet<HeatmapEntry> HeatmapEntries { get; set; }
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

        // ─── 🔥 Route Tracking tables ───
        public DbSet<RouteSession> RouteSessions { get; set; }
        public DbSet<RouteSessionPoi> RouteSessionPois { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ── RouteSession ──
            modelBuilder.Entity<RouteSession>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.DeviceId)
                    .HasMaxLength(128)
                    .IsRequired();

                entity.Property(e => e.PoiSequence)
                    .HasMaxLength(1024); // chuỗi "1,2,3,..." tối đa ~100 POIs

                entity.Property(e => e.Status)
                    .HasMaxLength(20)
                    .HasDefaultValue("completed");

                // Index để query theo device và thời gian
                entity.HasIndex(e => e.DeviceId);
                entity.HasIndex(e => e.EndedAt);
                entity.HasIndex(e => e.PoiSequence); // để GROUP BY nhanh

                // Relationship với RouteSessionPoi
                entity.HasMany(e => e.RouteSessionPois)
                    .WithOne()
                    .HasForeignKey(p => p.RouteSessionId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Không map navigation properties từ Shared model (Ignore trong SQLite)
                entity.Ignore(e => e.RouteSessionPois); // handled by HasMany above

                entity.ToTable("RouteSessions");
            });

            // ── RouteSessionPoi ──
            modelBuilder.Entity<RouteSessionPoi>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.TriggerType)
                    .HasMaxLength(20)
                    .IsRequired();

                // Index để analytics query
                entity.HasIndex(e => e.PoiId);
                entity.HasIndex(e => e.RouteSessionId);
                entity.HasIndex(e => e.TriggerType);

                entity.Ignore(e => e.Poi);

                entity.ToTable("RouteSessionPois");
            });
        }
    }
}
