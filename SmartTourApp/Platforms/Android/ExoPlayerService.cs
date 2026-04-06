using Android.Content;
using AndroidX.Media3.ExoPlayer;
using AndroidX.Media3.Common;
using AndroidX.Media3.ExoPlayer.Source;
using AndroidX.Media3.DataSource;

namespace SmartTourApp.Platforms.Android;

public class ExoPlayerService
{
    private IExoPlayer? player;
    private readonly Context context;

    public event Action<double>? OnProgress;
    public event Action<double>? OnDuration;

    private CancellationTokenSource? cts;

    public bool IsPlaying => player?.IsPlaying ?? false;

    public ExoPlayerService()
    {
        context = global::Android.App.Application.Context;
    }

    public void Init()
    {
        if (player != null) return;
        player = new ExoPlayerBuilder(context).Build();
    }

    public void Play(string filePath)
    {
        Init();

        player!.Stop();
        player.ClearMediaItems();

        var uri = global::Android.Net.Uri.FromFile(new Java.IO.File(filePath));

        var factory = new DefaultDataSource.Factory(context);
        var source = new ProgressiveMediaSource.Factory(factory)
            .CreateMediaSource(MediaItem.FromUri(uri));

        player.SetMediaSource(source);
        player.Prepare();
        player.Play();

        StartLoop();
    }

    private void StartLoop()
    {
        cts?.Cancel();
        cts = new CancellationTokenSource();
        var token = cts.Token;

        _ = Task.Run(async () =>
        {
            bool sentDuration = false;

            while (!token.IsCancellationRequested && player != null)
            {
                if (!sentDuration && player.Duration > 0)
                {
                    sentDuration = true;
                    OnDuration?.Invoke(player.Duration / 1000.0);
                }

                var pos = player.CurrentPosition / 1000.0;
                OnProgress?.Invoke(pos);

                await Task.Delay(100);
            }
        }, token);
    }

    public void Seek(double sec)
    {
        player?.SeekTo((long)(sec * 1000));
    }

    public void Pause() => player?.Pause();

    public void Stop()
    {
        cts?.Cancel();
        player?.Stop();
        player?.ClearMediaItems();
    }

    public void Release()
    {
        cts?.Cancel();
        player?.Release();
        player = null;
    }
}