using SQLite;
using SmartTour.Shared.Models;

namespace SmartTourApp.Data;

public class Database : IDisposable
{
    private readonly SQLiteConnection db;
    private readonly object locker = new();

    public Database()
    {
        string path = Path.Combine(
            FileSystem.AppDataDirectory,
            "tour.db");

        db = new SQLiteConnection(path);

        InitTables();
        CreateIndexes();
    }

    private void InitTables()
    {
        db.CreateTable<Poi>();
        db.CreateTable<PlayLog>();
        db.CreateTable<UserLocationLog>();
        db.CreateTable<AppSetting>();
    }

    private void CreateIndexes()
    {
        db.Execute("CREATE INDEX IF NOT EXISTS idx_poi_lat ON Poi(Lat)");
        db.Execute("CREATE INDEX IF NOT EXISTS idx_poi_lng ON Poi(Lng)");
        db.Execute("CREATE INDEX IF NOT EXISTS idx_playlog_poi ON PlayLog(PoiId)");
        db.Execute("CREATE INDEX IF NOT EXISTS idx_location_lat ON UserLocationLog(Latitude)");
    }

    // ======================
    // POI
    // ======================

    public List<Poi> GetPois()
    {
        lock (locker)
        {
            return db.Table<Poi>().ToList();
        }
    }

    public Poi? GetPoi(int id)
    {
        lock (locker)
        {
            return db.Find<Poi>(id);
        }
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
        }
    }

    public void AddPois(IEnumerable<Poi> pois)
    {
        lock (locker)
        {
            var existingIds = db.Table<Poi>().Select(p => p.Id).ToHashSet();

            db.RunInTransaction(() =>
            {
                foreach (var poi in pois)
                {
                    if (existingIds.Contains(poi.Id))
                        db.Update(poi);
                    else
                        db.Insert(poi);
                }
            });
        }
    }

    // ======================
    // PLAY LOG
    // ======================

    public void AddLog(PlayLog log)
    {
        lock (locker)
        {
            db.Insert(log);
        }
    }

    public List<PlayLog> GetLogs()
    {
        lock (locker)
        {
            return db.Table<PlayLog>().ToList();
        }
    }

    // ======================
    // LOCATION LOG
    // ======================

    private DateTime lastLogTime = DateTime.MinValue;

    public void AddLocation(UserLocationLog log)
    {
        if ((DateTime.Now - lastLogTime).TotalSeconds < 10)
            return;

        lastLogTime = DateTime.Now;

        lock (locker)
        {
            db.Insert(log);
        }
    }

    public List<UserLocationLog> GetLocations()
    {
        lock (locker)
        {
            return db.Table<UserLocationLog>().ToList();
        }
    }

    // ======================
    // SETTINGS
    // ======================

    public void SaveSetting(string key, string value)
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

    public string GetSetting(string key)
    {
        return db.Table<AppSetting>()
                 .FirstOrDefault(x => x.SettingKey == key)
                 ?.SettingValue;
    }

    // ======================
    // MAINTENANCE
    // ======================

    public void ClearLogs()
    {
        lock (locker)
        {
            db.DeleteAll<PlayLog>();
        }
    }

    public void ClearLocations()
    {
        lock (locker)
        {
            db.DeleteAll<UserLocationLog>();
        }
    }

    public void Dispose()
    {
        db?.Close();
    }
    public void ClearPois()
    {
        lock (locker)
        {
            db.DeleteAll<Poi>();
        }
    }
}