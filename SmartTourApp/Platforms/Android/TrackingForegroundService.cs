using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;

namespace SmartTourApp.Platforms.Android;

/// <summary>
/// TrackingForegroundService — Android Foreground Service giữ GPS tracking sống
/// kể cả khi app bị đưa vào background hoặc màn hình tắt.
///
/// ╔══════════════════════════════════════════════════════════════════╗
/// ║ FIX: Service giờ thực sự START TrackingService thông qua        ║
/// ║ MAUI DI container — không còn rời rạc nữa.                     ║
/// ║                                                                  ║
/// ║ FLOW:                                                            ║
/// ║  App.OnSleep → StartForegroundServiceCompat() →                 ║
/// ║  OnStartCommand → lấy TrackingService từ DI → Start()           ║
/// ║  App.OnResume → StopService() → TrackingService.Stop()          ║
/// ╚══════════════════════════════════════════════════════════════════╝
/// </summary>
[Service(ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeLocation)]
public class TrackingForegroundService : Service
{
    public const string ChannelId = "gps_tracking";
    public const string ActionStart = "ACTION_START_TRACKING";
    public const string ActionStop = "ACTION_STOP_TRACKING";

    private static TrackingService? _trackingServiceRef;
    private CancellationTokenSource? _cts;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(
        Intent? intent,
        StartCommandFlags flags,
        int startId)
    {
        var action = intent?.Action ?? ActionStart;

        if (action == ActionStop)
        {
            StopTracking();
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        // Tạo notification channel (Android 8+)
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            CreateNotificationChannel();

        // Start foreground với notification GPS
        var notification = BuildNotification();
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            StartForeground(NotificationId, notification,
                global::Android.Content.PM.ForegroundService.TypeLocation);
        else
            StartForeground(NotificationId, notification);

        // ── Lấy TrackingService từ MAUI DI và start ──
        StartTrackingViaMAUI();

        return StartCommandResult.Sticky;
    }

    /// <summary>
    /// Lấy TrackingService từ MAUI DI container và gọi Start().
    /// Đây là cầu nối thực sự giữa Android Service và MAUI service layer.
    /// </summary>
    private void StartTrackingViaMAUI()
    {
        try
        {
            // Lấy DI container từ MAUI Application
            var mauiApp = global::Microsoft.Maui.Controls.Application.Current;
            var services = mauiApp?.Handler?.MauiContext?.Services;

            if (services == null)
            {
                // DI chưa sẵn sàng → retry sau 2s
                _cts?.Cancel();
                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                _ = Task.Run(async () =>
                {
                    await Task.Delay(2000, token);
                    if (!token.IsCancellationRequested)
                        StartTrackingViaMAUI();
                }, token);
                return;
            }

            var trackingService = services.GetService<TrackingService>();
            if (trackingService == null)
            {
                System.Diagnostics.Debug.WriteLine(
                    "[ForegroundSvc] TrackingService not found in DI container");
                return;
            }

            _trackingServiceRef = trackingService;

            // Start tracking (idempotent — TrackingService.Start() tự check cts != null)
            _ = Task.Run(async () =>
            {
                try
                {
                    await trackingService.Start();
                    System.Diagnostics.Debug.WriteLine(
                        "[ForegroundSvc] ✅ TrackingService started from ForegroundService");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[ForegroundSvc] TrackingService.Start() error: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[ForegroundSvc] StartTrackingViaMAUI error: {ex.Message}");
        }
    }

    private void StopTracking()
    {
        try
        {
            _cts?.Cancel();
            _trackingServiceRef?.Stop();
            _trackingServiceRef = null;
            System.Diagnostics.Debug.WriteLine("[ForegroundSvc] TrackingService stopped");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[ForegroundSvc] StopTracking error: {ex.Message}");
        }
    }

    public override void OnDestroy()
    {
        StopTracking();
        base.OnDestroy();
    }

    // ══════════════════════════════════════════════════════════════
    // NOTIFICATION
    // ══════════════════════════════════════════════════════════════

    private const int NotificationId = 1001;

    private Notification BuildNotification()
    {
        // Intent để mở app khi nhấn notification
        var openIntent = global::Android.App.Application.Context
            .PackageManager?
            .GetLaunchIntentForPackage(
                global::Android.App.Application.Context.PackageName ?? "");

        var pendingIntent = openIntent != null
            ? PendingIntent.GetActivity(
                this, 0, openIntent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)
            : null;

        var builder = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("SmartTour đang chạy")!
            .SetContentText("GPS đang theo dõi để phát thuyết minh tự động")!
            .SetSmallIcon(global::SmartTourApp.Resource.Mipmap.logo_app)!
            .SetOngoing(true)!
            .SetPriority(NotificationCompat.PriorityLow)! // Ít xâm phạm hơn
            .SetCategory(NotificationCompat.CategoryService)!
            .SetVisibility(NotificationCompat.VisibilityPublic)!;

        if (pendingIntent != null)
            builder.SetContentIntent(pendingIntent);

        return builder.Build()
            ?? throw new InvalidOperationException("Could not create notification");
    }

    [SupportedOSPlatform("android26.0")]
    private void CreateNotificationChannel()
    {
        var manager = GetSystemService(NotificationService) as NotificationManager;
        if (manager == null) return;

        // Kiểm tra channel đã tồn tại chưa
        if (manager.GetNotificationChannel(ChannelId) != null) return;

        var channel = new NotificationChannel(
            ChannelId,
            "GPS Tracking",
            NotificationImportance.Low) // LOW = ít làm phiền, không âm thanh
        {
            Description = "Giữ GPS chạy để phát thuyết minh khi đến gần điểm tham quan"
        };

        channel.SetShowBadge(false);
        manager.CreateNotificationChannel(channel);
    }

    // ══════════════════════════════════════════════════════════════
    // STATIC HELPERS — gọi từ App.xaml.cs
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Khởi động ForegroundService từ App layer.
    /// Tương thích Android 8+ (startForegroundService).
    /// </summary>
    public static void StartService(Context context)
    {
        try
        {
            var intent = new Intent(context, typeof(TrackingForegroundService));
            intent.SetAction(ActionStart);

            if (OperatingSystem.IsAndroidVersionAtLeast(26))
                context.StartForegroundService(intent);
            else
                context.StartService(intent);

            System.Diagnostics.Debug.WriteLine("[ForegroundSvc] Service start requested");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[ForegroundSvc] StartService error: {ex.Message}");
        }
    }

    /// <summary>
    /// Dừng ForegroundService khi app thực sự đóng.
    /// </summary>
    public static void StopService(Context context)
    {
        try
        {
            var intent = new Intent(context, typeof(TrackingForegroundService));
            intent.SetAction(ActionStop);
            context.StartService(intent);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[ForegroundSvc] StopService error: {ex.Message}");
        }
    }
}
