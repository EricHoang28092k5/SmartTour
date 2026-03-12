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

    public List<PlayLog> GetLogs()
    {
        return db.Table<PlayLog>().ToList();
    }
}