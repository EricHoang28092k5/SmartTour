using Microsoft.Maui.Devices.Sensors;
using SmartTour.Shared.Models;

namespace SmartTourApp.Services;

public class TrackingService
{
    private readonly LocationService location;
    private readonly GeofencingEngine geo;
    private readonly NarrationEngine narration;
    private readonly PoiRepository repo;
    private readonly LocationLogger logger;

    private bool isRunning = false;
    private List<Poi> pois = new();

    public event Action<Location>? OnLocationChanged;

    public TrackingService(
        LocationService location,
        GeofencingEngine geo,
        NarrationEngine narration,
        PoiRepository repo,
        LocationLogger logger)
    {
        this.location = location;
        this.geo = geo;
        this.narration = narration;
        this.repo = repo;
        this.logger = logger;
    }

    public async Task Start()
    {
        if (isRunning) return;

        isRunning = true;

        pois = await repo.GetPois();

        _ = Task.Run(async () =>
        {
            var timer = new PeriodicTimer(TimeSpan.FromSeconds(7));

            try
            {
                while (isRunning && await timer.WaitForNextTickAsync())
                {
                    var loc = await location.GetLocation();

                    if (loc == null) continue;

                    logger.Log(loc);
                    OnLocationChanged?.Invoke(loc);

                    // SỬA TẠI ĐÂY:
                    // 1. Tìm ông gần nhất (hàm mới bạn vừa viết)
                    var nearestPoi = geo.GetNearestPoi(loc, pois);

<<<<<<< HEAD
                    // 2. Chỉ phát nhạc nếu là vùng MỚI (để không bị lặp mỗi 3 giây)
                    if (nearestPoi != null && geo.IsNewZone(nearestPoi.Id))
                    {
                        await narration.Play(nearestPoi, loc);
                    }
                    else if (nearestPoi == null)
                    {
                        // Nếu không ở gần ông nào thì reset trạng thái các vùng
                        foreach (var p in pois) geo.LeaveZone(p.Id);
                    }
                }
=======
                    if (poi != null)
                        await narration.Play(poi, loc);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Tracking error: " + ex.Message);
>>>>>>> e91d1ab27c788503c01afd96e95d2391a9bdc9b0
            }
        });
    }

    public void Stop()
    {
        isRunning = false;
    }
}