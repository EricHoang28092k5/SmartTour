using SmartTour.Shared.Models;
using SmartTourApp.Data;
using System.Linq;

namespace SmartTourApp.Services;

public class NarrationEngine
{
    private readonly AudioService audio;
    private readonly TtsService tts;
    private readonly Database db;
    private readonly LanguageService langService; // Thêm để biết đang chọn ngôn ngữ nào

    private readonly Dictionary<int, DateTime> history = new();
    private readonly Queue<Poi> queue = new();
    private bool isPlaying;

    public NarrationEngine(
        AudioService audio,
        TtsService tts,
        Database db,
        LanguageService langService)
    {
        this.audio = audio;
        this.tts = tts;
        this.db = db;
        this.langService = langService;
    }

    public async Task Play(Poi? poi, Location location)
    {
        if (poi == null) return;

        // Chặn phát lại trong vòng 10 phút để tránh làm phiền người dùng
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
            string? finalUrl = null;

            // 1. Kiểm tra trường audioUrl trực tiếp
            if (!string.IsNullOrWhiteSpace(poi.AudioUrl))
            {
                finalUrl = poi.AudioUrl;
            }
            // 2. Nếu trống, tìm trong danh sách audioFiles (Dựa trên Data JSON của bạn)
            else if (poi.AudioFiles != null && poi.AudioFiles.Any())
            {
                // Logic: Tìm file khớp với ngôn ngữ đang chọn (vi = 1, en = 2 chẳng hạn)
                // Nếu không thấy thì lấy file đầu tiên làm mặc định
                var currentLang = langService.Current == "vi" ? 1 : 2;
                var audioFile = poi.AudioFiles.FirstOrDefault(f => f.LanguageId == currentLang)
                                ?? poi.AudioFiles.First();

                finalUrl = audioFile.FileUrl;
            }

            // --- THỰC THI PHÁT ---
            if (!string.IsNullOrWhiteSpace(finalUrl))
            {
                // Lưu ý: Nếu URL bắt đầu bằng "/" thì là path local, 
                // nếu bắt đầu bằng "http" là Cloudinary
                await audio.Play(finalUrl);

                // Đợi một khoảng thời gian ước lượng hoặc dựa trên duration nếu có
                await Task.Delay(8000);
            }
            else if (!string.IsNullOrWhiteSpace(poi.TtsScript))
            {
                await tts.Speak(poi.TtsScript, langService.Current);
            }

            // Lưu lịch sử
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