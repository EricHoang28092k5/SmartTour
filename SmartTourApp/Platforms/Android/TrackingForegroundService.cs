using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using System.Runtime.Versioning;

namespace SmartTourApp.Platforms.Android;

[Service]
public class TrackingForegroundService : Service
{
    const string ChannelId = "gps";

    public override IBinder? OnBind(Intent? intent)
        => null;

    public override StartCommandResult OnStartCommand(
        Intent? intent,
        StartCommandFlags flags,
        int startId)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            CreateNotificationChannel();
        }

        var notification = CreateNotification();

        StartForeground(1, notification);

        // 🔥 Giữ service sống
        return StartCommandResult.Sticky;
    }

    Notification CreateNotification()
    {
        var builder = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("SmartTour")!
            .SetContentText("Đang theo dõi vị trí")!
            .SetSmallIcon(global::SmartTourApp.Resource.Mipmap.logo_app)!
            .SetOngoing(true)!;

        var notification = builder.Build();

        return notification ?? throw new InvalidOperationException("Could not create notification");
    }

    [SupportedOSPlatform("android26.0")]
    void CreateNotificationChannel()
    {
        var manager = GetSystemService(NotificationService) as NotificationManager;

        if (manager == null)
            return;

        var channel = new NotificationChannel(
            ChannelId,
            "GPS Tracking",
            NotificationImportance.Default);

        manager.CreateNotificationChannel(channel);
    }
}