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

        return StartCommandResult.Sticky;
    }

    Notification CreateNotification()
    {
        var builder = new NotificationCompat.Builder(this!, ChannelId)
            .SetContentTitle("SmartTour")
            .SetContentText("Đang theo dõi vị trí")
            .SetSmallIcon(Resource.Mipmap.appicon);

        return builder.Build()!;
    }

    [SupportedOSPlatform("android26.0")]
    void CreateNotificationChannel()
    {
        var service = GetSystemService(NotificationService);

        if (service is not NotificationManager manager)
            return;

        var channel = new NotificationChannel(
            ChannelId,
            "GPS Tracking",
            NotificationImportance.Default);

        manager.CreateNotificationChannel(channel);
    }
}