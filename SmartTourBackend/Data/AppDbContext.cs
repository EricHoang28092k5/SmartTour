using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;

namespace SmartTourBackend.Data // Thêm dòng này vào để định danh "hộ khẩu"
{
    public class AppDbContext : DbContext
    {
        // 1. PHẢI CÓ Constructor này thì nó mới nhận được Connection String từ Neon
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }
        // Đăng ký toàn bộ 12 bảng lên Neon
        public DbSet<User> Users { get; set; }
        public DbSet<Poi> Pois { get; set; }
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
    }
}