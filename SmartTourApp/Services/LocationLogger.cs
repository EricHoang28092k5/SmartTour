using SmartTour.Shared.Models;
using SmartTourApp.Data;

namespace SmartTourApp.Services;

public class LocationLogger
{
    private readonly Database db;

    public LocationLogger(Database db)
    {
        this.db = db;
    }

    public void Log(Location location)
    {
        db.AddLocation(new UserLocationLog
        {
            UserId = 0,
            Latitude = (decimal)location.Latitude,
            Longitude = (decimal)location.Longitude
        });
    }
}