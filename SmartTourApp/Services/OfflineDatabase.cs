using SQLite;
using SmartTour.Shared.Models;

namespace SmartTourApp.Services
{
    /// <summary>
    /// OfflineDatabase — SQLite store chuyên dụng cho offline functionality.
    /// YC1: Thêm cache title theo ngôn ngữ.
    /// YC4: Thêm bảng OfflineFoodTranslation.
    /// </summary>
    public class OfflineDatabase : IDisposable
    {
        private readonly SQLiteConnection _db;
        private readonly object _lock = new();

        public OfflineDatabase()
        {
            var path = Path.Combine(FileSystem.AppDataDirectory, "offline_cache.db");
            _db = new SQLiteConnection(path);
            InitTables();
            CreateIndexes();
        }

        private void InitTables()
        {
            _db.CreateTable<OfflinePoiScript>();
            _db.CreateTable<OfflinePoiVersion>();
            _db.CreateTable<OfflinePlayLog>();
            _db.CreateTable<OfflineFoodTranslation>(); // YC4
        }

        private void CreateIndexes()
        {
            _db.Execute(
                "CREATE INDEX IF NOT EXISTS idx_script_poi_lang " +
                "ON OfflinePoiScript(PoiId, LanguageCode)");
            _db.Execute(
                "CREATE INDEX IF NOT EXISTS idx_version_poi " +
                "ON OfflinePoiVersion(PoiId)");
            _db.Execute(
                "CREATE INDEX IF NOT EXISTS idx_log_synced " +
                "ON OfflinePlayLog(IsSynced, RetryCount)");
            _db.Execute(
                "CREATE INDEX IF NOT EXISTS idx_food_poi_lang " +
                "ON OfflineFoodTranslation(PoiId, LanguageCode)");
        }

        // ══════════════════════════════════════════════════════════════
        // POI SCRIPTS
        // ══════════════════════════════════════════════════════════════

        public void UpsertPoiScript(OfflinePoiScript script)
        {
            lock (_lock)
            {
                var existing = _db.Table<OfflinePoiScript>()
                    .FirstOrDefault(x => x.PoiId == script.PoiId &&
                                         x.LanguageCode == script.LanguageCode);
                if (existing == null)
                    _db.Insert(script);
                else
                {
                    script.Id = existing.Id;
                    _db.Update(script);
                }
            }
        }

        public OfflinePoiScript? GetPoiScript(int poiId, string languageCode)
        {
            lock (_lock)
            {
                return _db.Table<OfflinePoiScript>()
                    .FirstOrDefault(x => x.PoiId == poiId && x.LanguageCode == languageCode);
            }
        }

        public OfflinePoiScript? GetPoiScriptByPrefix(int poiId, string langPrefix)
        {
            lock (_lock)
            {
                return _db.Table<OfflinePoiScript>()
                    .Where(x => x.PoiId == poiId)
                    .ToList()
                    .FirstOrDefault(x => x.LanguageCode
                        .StartsWith(langPrefix, StringComparison.OrdinalIgnoreCase));
            }
        }

        public OfflinePoiScript? GetAnyScriptForPoi(int poiId)
        {
            lock (_lock)
            {
                return _db.Table<OfflinePoiScript>()
                    .Where(x => x.PoiId == poiId)
                    .ToList()
                    .OrderBy(x => x.LanguageCode.StartsWith("en") ? 0 : 1)
                    .FirstOrDefault();
            }
        }

        public List<OfflinePoiScript> GetAllScriptsForPoi(int poiId)
        {
            lock (_lock)
            {
                return _db.Table<OfflinePoiScript>()
                    .Where(x => x.PoiId == poiId)
                    .ToList();
            }
        }

        // ══════════════════════════════════════════════════════════════
        // POI VERSION TRACKING
        // ══════════════════════════════════════════════════════════════

        public void UpsertPoiVersion(OfflinePoiVersion version)
        {
            lock (_lock)
            {
                var existing = _db.Table<OfflinePoiVersion>()
                    .FirstOrDefault(x => x.PoiId == version.PoiId);
                if (existing == null)
                    _db.Insert(version);
                else
                {
                    version.Id = existing.Id;
                    _db.Update(version);
                }
            }
        }

        public OfflinePoiVersion? GetPoiVersion(int poiId)
        {
            lock (_lock)
            {
                return _db.Table<OfflinePoiVersion>()
                    .FirstOrDefault(x => x.PoiId == poiId);
            }
        }

        // ══════════════════════════════════════════════════════════════
        // OFFLINE PLAY LOGS
        // ══════════════════════════════════════════════════════════════

        public void InsertOfflineLog(OfflinePlayLog log)
        {
            lock (_lock) { _db.Insert(log); }
        }

        public List<OfflinePlayLog> GetPendingPlayLogs()
        {
            lock (_lock)
            {
                return _db.Table<OfflinePlayLog>()
                    .Where(x => !x.IsSynced && x.RetryCount < 5)
                    .OrderBy(x => x.PlayedAt)
                    .Take(100)
                    .ToList();
            }
        }

        public void DeleteOfflineLog(int id)
        {
            lock (_lock) { _db.Delete<OfflinePlayLog>(id); }
        }

        public void IncrementLogRetry(int id)
        {
            lock (_lock)
            {
                var log = _db.Find<OfflinePlayLog>(id);
                if (log == null) return;
                log.RetryCount++;
                _db.Update(log);
            }
        }

        public void PurgeOldSyncedLogs()
        {
            lock (_lock)
            {
                var cutoff = DateTime.UtcNow.AddDays(-7);
                _db.Execute(
                    "DELETE FROM OfflinePlayLog WHERE IsSynced = 1 AND PlayedAt < ?",
                    cutoff.ToString("O"));
            }
        }

        // ══════════════════════════════════════════════════════════════
        // YC4: FOOD TRANSLATIONS
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Lưu toàn bộ food translations của một ngôn ngữ cho một POI.
        /// </summary>
        public void UpsertFoodTranslations(int poiId, string languageCode, List<Food> foods)
        {
            lock (_lock)
            {
                _db.RunInTransaction(() =>
                {
                    // Xóa cũ trước
                    _db.Execute(
                        "DELETE FROM OfflineFoodTranslation WHERE PoiId = ? AND LanguageCode = ?",
                        poiId, languageCode);

                    // Insert mới
                    foreach (var food in foods)
                    {
                        _db.Insert(new OfflineFoodTranslation
                        {
                            FoodId = food.Id,
                            PoiId = poiId,
                            LanguageCode = languageCode,
                            Name = food.Name ?? "",
                            Description = food.Description ?? "",
                            Price = food.Price,
                            ImageUrl = food.ImageUrl ?? "",
                            LastSyncedAt = DateTime.UtcNow
                        });
                    }
                });
            }
        }

        /// <summary>
        /// Lấy food translations theo poiId + languageCode.
        /// </summary>
        public List<Food> GetFoodTranslations(int poiId, string languageCode)
        {
            lock (_lock)
            {
                return _db.Table<OfflineFoodTranslation>()
                    .Where(x => x.PoiId == poiId && x.LanguageCode == languageCode)
                    .ToList()
                    .Select(x => new Food
                    {
                        Id = x.FoodId,
                        Name = x.Name,
                        Description = x.Description,
                        Price = x.Price,
                        ImageUrl = x.ImageUrl,
                        PoiId = poiId
                    })
                    .ToList();
            }
        }

        /// <summary>
        /// Lấy food translations theo prefix ngôn ngữ.
        /// </summary>
        public List<Food> GetFoodTranslationsByPrefix(int poiId, string langPrefix)
        {
            lock (_lock)
            {
                var rows = _db.Table<OfflineFoodTranslation>()
                    .Where(x => x.PoiId == poiId)
                    .ToList()
                    .Where(x => x.LanguageCode.StartsWith(langPrefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                return rows.Select(x => new Food
                {
                    Id = x.FoodId,
                    Name = x.Name,
                    Description = x.Description,
                    Price = x.Price,
                    ImageUrl = x.ImageUrl,
                    PoiId = poiId
                }).ToList();
            }
        }

        // ══════════════════════════════════════════════════════════════
        // STATS
        // ══════════════════════════════════════════════════════════════

        public int GetCachedPoiCount() => _db.Table<OfflinePoiVersion>().Count();
        public int GetPendingLogCount() =>
            _db.Table<OfflinePlayLog>().Count(x => !x.IsSynced && x.RetryCount < 5);

        public void Dispose() => _db?.Close();
    }

    // ══════════════════════════════════════════════════════════════════
    // SQLite Models
    // ══════════════════════════════════════════════════════════════════

    [SQLite.Table("OfflinePoiScript")]
    public class OfflinePoiScript
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public int PoiId { get; set; }
        [MaxLength(10)]
        public string LanguageCode { get; set; } = "";
        [MaxLength(100)]
        public string LanguageName { get; set; } = "";
        [MaxLength(500)]
        public string Title { get; set; } = "";
        public string TtsScript { get; set; } = "";
        [MaxLength(1000)]
        public string AudioUrl { get; set; } = "";
        public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;
        public DateTime ServerUpdatedAt { get; set; }
    }

    [SQLite.Table("OfflinePoiVersion")]
    public class OfflinePoiVersion
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public int PoiId { get; set; }
        public DateTime LastSyncedAt { get; set; }
        public DateTime ServerUpdatedAt { get; set; }
        public int TrackCount { get; set; }
    }

    [SQLite.Table("OfflinePlayLog")]
    public class OfflinePlayLog
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public int PoiId { get; set; }
        public DateTime PlayedAt { get; set; }
        public double Lat { get; set; }
        public double Lng { get; set; }
        public int DurationListened { get; set; }
        [MaxLength(128)]
        public string DeviceId { get; set; } = "";
        [MaxLength(128)]
        public string UserId { get; set; } = "";
        public bool IsSynced { get; set; } = false;
        public int RetryCount { get; set; } = 0;
    }

    /// <summary>YC4: Cache food translations theo ngôn ngữ để dùng offline.</summary>
    [SQLite.Table("OfflineFoodTranslation")]
    public class OfflineFoodTranslation
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public int FoodId { get; set; }
        public int PoiId { get; set; }
        [MaxLength(10)]
        public string LanguageCode { get; set; } = "";
        [MaxLength(500)]
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal Price { get; set; }
        [MaxLength(1000)]
        public string ImageUrl { get; set; } = "";
        public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;
    }
}
