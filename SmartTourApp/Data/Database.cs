using SmartTour.Shared.Models;
using SQLite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SmartTourApp.Data
{
    public class Database
    {
        private readonly SQLiteConnection db;
        private readonly object locker = new();

        public Database()
        {
            var path = Path.Combine(FileSystem.AppDataDirectory, "tour.db");
            db = new SQLiteConnection(path);

            db.CreateTable<Poi>();
            db.CreateTable<PlayLog>();
            db.CreateTable<UserLocationLog>();
            db.CreateTable<AppSetting>();
            db.CreateTable<SyncOutbox>();
        }

        // ======================
        // POI
        // ======================

        public List<Poi> GetPois()
        {
            lock (locker)
                return db.Table<Poi>().ToList();
        }

        public void AddPoi(Poi poi)
        {
            lock (locker)
            {
                var existing = db.Find<Poi>(poi.Id);

                if (existing == null)
                    db.Insert(poi);
                else
                    db.Update(poi);

                db.Insert(new SyncOutbox
                {
                    Type = "POI_UPSERT",
                    Payload = System.Text.Json.JsonSerializer.Serialize(poi),
                    IsSynced = false,
                    RetryCount = 0,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        public void AddPois(IEnumerable<Poi> pois)
        {
            lock (locker)
            {
                db.RunInTransaction(() =>
                {
                    foreach (var poi in pois)
                    {
                        var existing = db.Find<Poi>(poi.Id);

                        if (existing == null)
                            db.Insert(poi);
                        else
                            db.Update(poi);
                    }
                });
            }
        }

        // ======================
        // SETTINGS (FIX ERROR HERE)
        // ======================

        public string GetSetting(string key)
        {
            lock (locker)
            {
                var item = db.Table<AppSetting>()
                    .FirstOrDefault(x => x.SettingKey == key);

                return item?.SettingValue ?? "";
            }
        }

        public void SaveSetting(string key, string value)
        {
            lock (locker)
            {
                var existing = db.Table<AppSetting>()
                    .FirstOrDefault(x => x.SettingKey == key);

                if (existing == null)
                {
                    db.Insert(new AppSetting
                    {
                        SettingKey = key,
                        SettingValue = value
                    });
                }
                else
                {
                    existing.SettingValue = value;
                    db.Update(existing);
                }
            }
        }

        // ======================
        // LOGS (FIX AddLog ERROR)
        // ======================

        public void AddLog(PlayLog log)
        {
            lock (locker)
                db.Insert(log);
        }

        public List<PlayLog> GetLogs()
        {
            lock (locker)
                return db.Table<PlayLog>().ToList();
        }

        // ======================
        // OUTBOX
        // ======================

        public List<SyncOutbox> GetOutboxItems()
        {
            lock (locker)
            {
                return db.Table<SyncOutbox>()
                    .Where(x => !x.IsSynced && x.RetryCount < 5)
                    .OrderBy(x => x.CreatedAt)
                    .ToList();
            }
        }

        public void MarkOutboxSynced(int id)
        {
            lock (locker)
            {
                var item = db.Find<SyncOutbox>(id);
                if (item == null) return;

                item.IsSynced = true;
                db.Update(item);
            }
        }

        public void IncreaseRetry(int id)
        {
            lock (locker)
            {
                var item = db.Find<SyncOutbox>(id);
                if (item == null) return;

                item.RetryCount++;
                db.Update(item);
            }
        }

        // ======================
        // LOCATION
        // ======================

        public void AddLocation(UserLocationLog log)
        {
            lock (locker)
                db.Insert(log);
        }

        public List<UserLocationLog> GetLocations()
        {
            lock (locker)
                return db.Table<UserLocationLog>().ToList();
        }
    }

    // ======================
    // SyncOutbox (KEEP SAME FILE)
    // ======================

    [Table("SyncOutbox")]
    public class SyncOutbox
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public string Type { get; set; } = "";
        public string Payload { get; set; } = "";
        public bool IsSynced { get; set; }
        public int RetryCount { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}