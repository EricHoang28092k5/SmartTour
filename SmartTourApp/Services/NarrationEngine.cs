using SmartTour.Shared.Models; // Quan trọng nhất là dòng này
using SmartTourApp.Data;

namespace SmartTourApp.Services;

public class NarrationEngine
{
    private readonly AudioService audio;

    private readonly Database db;

    private readonly Dictionary<int, DateTime> history = new();

    private bool isPlaying;

    public NarrationEngine(
        AudioService audio,
        Database db)
    {
        this.audio = audio;
        this.db = db;
    }

    public async Task Play(Poi? poi, Location location)
    {
        if (poi == null)
            return;

        if (isPlaying)
            return;

        if (string.IsNullOrWhiteSpace(poi.AudioUrl))
            return;

        if (history.ContainsKey(poi.Id))
        {
            if ((DateTime.Now - history[poi.Id]).Minutes < 10)
                return;
        }

        isPlaying = true;

        await audio.Play(poi.AudioUrl);

        history[poi.Id] = DateTime.Now;

        db.AddLog(new PlayLog
        {
            PoiId = poi.Id,
            Time = DateTime.Now,
            Lat = location.Latitude,
            Lng = location.Longitude
        });

        isPlaying = false;
    }
}