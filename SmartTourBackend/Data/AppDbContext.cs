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
        public DbSet<FoodTranslation> FoodTranslations { get; set; }
        public DbSet<Category> Category { get; set; }
        public DbSet<TourTranslation> TourTranslations { get; set; }
        public DbSet<RouteSession> RouteSessions { get; set; }
        public DbSet<RouteSessionPoi> RouteSessionPois { get; set; }
        public DbSet<DevicePresence> DevicePresences { get; set; }
        public DbSet<PoiAudioListenEvent> PoiAudioListenEvents { get; set; }
        public DbSet<AudioPipelineJob> AudioPipelineJobs { get; set; }
        public DbSet<ScriptChangeRequest> ScriptChangeRequests { get; set; }

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

            modelBuilder.Entity<FoodTranslation>(entity =>
            {
                entity.HasOne(ft => ft.Food)
                    .WithMany(f => f.FoodTranslations)
                    .HasForeignKey(ft => ft.FoodId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(ft => ft.Language)
                    .WithMany()
                    .HasForeignKey(ft => ft.LanguageId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(ft => new { ft.FoodId, ft.LanguageId }).IsUnique();
            });

            modelBuilder.Entity<DevicePresence>(entity =>
            {
                entity.HasIndex(e => e.DeviceId).IsUnique();
                entity.Property(e => e.DeviceId).HasMaxLength(128);
                entity.Property(e => e.IpAddress).HasMaxLength(128);
                entity.Property(e => e.UserAgent).HasMaxLength(512);
                entity.Property(e => e.DeviceModel).HasMaxLength(128);
                entity.Property(e => e.Platform).HasMaxLength(64);
                entity.Property(e => e.OsVersion).HasMaxLength(64);
                entity.Property(e => e.AppVersion).HasMaxLength(32);
            });

            modelBuilder.Entity<PoiAudioListenEvent>(entity =>
            {
                entity.ToTable("poi_audio_listen_events");
                entity.HasIndex(e => new { e.DeviceId, e.PoiId, e.DurationSeconds, e.CreatedAt });
                entity.HasIndex(e => new { e.DeviceId, e.PoiId, e.CreatedAt });
            });

            modelBuilder.Entity<AudioPipelineJob>(entity =>
            {
                entity.ToTable("audio_pipeline_jobs");
                entity.HasIndex(e => new { e.Status, e.NextRetryAt });
                entity.Property(e => e.Status).HasMaxLength(20);
                entity.Property(e => e.JobType).HasMaxLength(50);
            });

            modelBuilder.Entity<ScriptChangeRequest>(entity =>
            {
                entity.ToTable("script_change_requests");
                entity.HasIndex(e => new { e.PoiId, e.Status, e.CreatedAt });
                entity.Property(e => e.LanguageCode).HasMaxLength(20);
                entity.Property(e => e.Status).HasMaxLength(20);
            });

            modelBuilder.Entity<Poi>(entity =>
            {
                entity.Property(e => e.ApprovalStatus).HasMaxLength(20).HasDefaultValue("approved");
                entity.Property(e => e.ApprovedByUserId).HasMaxLength(128);
                entity.HasIndex(e => e.ApprovalStatus);
            });
        }
    }
}
