using SQLite;
using SmartTour.Shared.Models;

namespace SmartTourApp.Data;

public class Database
{
    private SQLiteConnection db;

    public Database()
    {
        string path =
            Path.Combine(
                FileSystem.AppDataDirectory,
                "tour.db");

        db = new SQLiteConnection(path);

        db.CreateTable<Poi>();
        db.CreateTable<PlayLog>();
        db.CreateTable<UserLocationLog>();
        db.CreateTable<AppSetting>();
    }

    public List<Poi> GetPois()
    {
        return db.Table<Poi>().ToList();
    }

    public void AddPoi(Poi poi)
    {
        db.Insert(poi);
    }

    public void AddLog(PlayLog log)
    {
        db.Insert(log);
    }

    public void AddLocation(UserLocationLog log)
    {
        db.Insert(log);
    }

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

    public string GetSetting(string key, string defaultValue)
    {
        var s = db.Table<AppSetting>()
            .FirstOrDefault(x => x.SettingKey == key);

        return s?.SettingValue ?? defaultValue;
    }
}