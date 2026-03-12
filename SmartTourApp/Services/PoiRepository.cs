using SmartTour.Shared.Models;
using SmartTourApp.Data;

namespace SmartTourApp.Services;

public class PoiRepository
{
    private readonly Database db;

    public PoiRepository()
    {
        db = new Database();

        Seed();
    }

    public List<Poi> GetPois()
    {
        return db.GetPois();
    }

    private void Seed()
    {
        if (db.GetPois().Count > 0)
            return;

        db.AddPoi(new Poi
        {
            Name = "Bến Nhà Rồng",
            Lat = 10.7690,
            Lng = 106.7050,
            Radius = 80,
            AudioUrl = "ben_nha_rong.mp3"
        });

        db.AddPoi(new Poi
        {
            Name = "Bảo Tàng Hồ Chí Minh",
            Lat = 10.7702,
            Lng = 106.7061,
            Radius = 80,
            AudioUrl = "bao_tang.mp3"
        });
    }
}