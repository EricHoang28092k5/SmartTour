using SmartTour.Shared.Models;
using SmartTourApp.Data;

namespace SmartTourApp.Services;

public class OfflineService
{
    private readonly Database db;

    public OfflineService(Database db)
    {
        this.db = db;
    }

    public async Task DownloadTour(List<Poi> pois)
    {
        foreach (var poi in pois)
        {
            if (string.IsNullOrWhiteSpace(poi.AudioUrl))
                continue;

            var file =
                Path.Combine(
                    FileSystem.AppDataDirectory,
                    $"{poi.Id}.mp3");

            using var http = new HttpClient();

            var bytes =
                await http.GetByteArrayAsync(poi.AudioUrl);

            File.WriteAllBytes(file, bytes);

            poi.AudioUrl = file;

            db.AddPoi(poi);
        }
    }
}