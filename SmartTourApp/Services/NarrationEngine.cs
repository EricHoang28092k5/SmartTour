using SmartTour.Shared.Models;
using SmartTourApp.Data;

namespace SmartTourApp.Services;

public class NarrationEngine
{
    private readonly AudioService audio;
    private readonly TtsService tts;
    private readonly Database db;

    private readonly Dictionary<int, DateTime> history = new();

    private readonly Queue<Poi> queue = new();

    private bool isPlaying;

    public NarrationEngine(
        AudioService audio,
        TtsService tts,
        Database db)
    {
        this.audio = audio;
        this.tts = tts;
        this.db = db;
    }

    public async Task Play(Poi? poi, Location location)
    {
        if (poi == null)
            return;

        if (history.ContainsKey(poi.Id))
        {
            if ((DateTime.Now - history[poi.Id]).TotalMinutes < 10)
                return;
        }

        queue.Enqueue(poi);

        if (!isPlaying)
            await ProcessQueue(location);
    }

    private async Task ProcessQueue(Location location)
    {
        isPlaying = true;

        while (queue.Count > 0)
        {
            var poi = queue.Dequeue();

            string? audioUrl = null;

            // 🎯 Ưu tiên audioFiles từ server
            if (poi.AudioFiles != null && poi.AudioFiles.Any())
            {
                audioUrl = poi.AudioFiles.First().FileUrl;
            }
            else if (!string.IsNullOrWhiteSpace(poi.AudioUrl))
            {
                audioUrl = poi.AudioUrl;
            }

            if (!string.IsNullOrWhiteSpace(audioUrl))
            {
                await audio.Play(audioUrl); // phải fix AudioService nữa
            }
            else if (!string.IsNullOrWhiteSpace(poi.TtsScript))
            {
                await tts.Speak(poi.TtsScript);
            }

            history[poi.Id] = DateTime.Now;

            db.AddLog(new PlayLog
            {
                PoiId = poi.Id,
                Time = DateTime.Now,
                Lat = location.Latitude,
                Lng = location.Longitude
            });
        }

        isPlaying = false;
    }
}