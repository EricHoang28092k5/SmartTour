using SQLite;
using SmartTour.Shared.Models;

namespace SmartTourApp.Services
{
    /// <summary>
    /// OfflineDatabase — SQLite store chuyên dụng cho offline functionality.
    ///
    /// Tables:
    ///   OfflinePoiScript  — TTS scripts + audio URLs cho từng POI × ngôn ngữ
    ///   OfflinePoiVersion — Version tracking để detect stale data
    ///   OfflinePlayLog    — Play logs chờ sync lên server
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

        // ══════════════════════════════════════════════════════════════
        // INIT
        // ══════════════════════════════════════════════════════════════

        private void InitTables()
        {
            _db.CreateTable<OfflinePoiScript>();
            _db.CreateTable<OfflinePoiVersion>();
            _db.CreateTable<OfflinePlayLog>();
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
        }

        // ══════════════════════════════════════════════════════════════
        // POI SCRIPTS (Yêu cầu 1 & 4)
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

        /// <summary>Tìm script chính xác theo poiId + languageCode.</summary>
        public OfflinePoiScript? GetPoiScript(int poiId, string languageCode)
        {
            lock (_lock)
            {
                return _db.Table<OfflinePoiScript>()
                    .FirstOrDefault(x => x.PoiId == poiId &&
                                         x.LanguageCode == languageCode);
            }
        }

        /// <summary>Tìm script theo prefix ngôn ngữ (vi → vi-VN, vi-vn...).</summary>
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

        /// <summary>Lấy bất kỳ script nào của POI (fallback cuối).</summary>
        public OfflinePoiScript? GetAnyScriptForPoi(int poiId)
        {
            lock (_lock)
            {
                // Ưu tiên tiếng Anh làm fallback, sau đó bất kỳ
                return _db.Table<OfflinePoiScript>()
                    .Where(x => x.PoiId == poiId)
                    .ToList()
                    .OrderBy(x => x.LanguageCode.StartsWith("en") ? 0 : 1)
                    .FirstOrDefault();
            }
        }

        /// <summary>Lấy tất cả script của một POI (tất cả ngôn ngữ).</summary>
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
        // POI VERSION TRACKING (Yêu cầu 1)
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
        // OFFLINE PLAY LOGS (Yêu cầu 5)
        // ══════════════════════════════════════════════════════════════

        public void InsertOfflineLog(OfflinePlayLog log)
        {
            lock (_lock)
            {
                _db.Insert(log);
            }
        }

        /// <summary>Lấy các log chưa sync (tối đa 100 records, retry < 5).</summary>
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
            lock (_lock)
            {
                _db.Delete<OfflinePlayLog>(id);
            }
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

        /// <summary>Xóa logs đã sync cũ hơn 7 ngày.</summary>
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
        // STATS
        // ══════════════════════════════════════════════════════════════

        public int GetCachedPoiCount() =>
            _db.Table<OfflinePoiVersion>().Count();

        public int GetPendingLogCount() =>
            _db.Table<OfflinePlayLog>()
               .Count(x => !x.IsSynced && x.RetryCount < 5);

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

        /// <summary>POI ID — foreign key logic.</summary>
        public int PoiId { get; set; }

        /// <summary>Language code: vi, en, ja, zh, ko (hoặc vi-VN, en-US...).</summary>
        [MaxLength(10)]
        public string LanguageCode { get; set; } = "";

        [MaxLength(100)]
        public string LanguageName { get; set; } = "";

        [MaxLength(500)]
        public string Title { get; set; } = "";

        /// <summary>Văn bản thuyết minh — dùng cho TTS khi offline.</summary>
        public string TtsScript { get; set; } = "";

        /// <summary>Cloudinary URL — có thể rỗng nếu chưa generate.</summary>
        [MaxLength(1000)]
        public string AudioUrl { get; set; } = "";

        /// <summary>Thời điểm data này được sync từ server về.</summary>
        public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;

        /// <summary>UpdatedAt trên server lúc sync — dùng để version check.</summary>
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
}
