using System.ComponentModel;
using System.Runtime.CompilerServices;

// 🔥 Đưa vào trong #if để Windows không nhìn thấy dòng này
#if ANDROID
using SmartTourApp.Platforms.Android;
#endif

namespace SmartTourApp.ViewModels;

public class AudioViewModel : INotifyPropertyChanged
{
    // 🔥 Chỉ khai báo biến này khi đang ở nền tảng Android
#if ANDROID
    private readonly ExoPlayerService audioService;
#endif

    private double currentPosition;
    private double duration;

    public double CurrentPosition
    {
        get => currentPosition;
        set { currentPosition = value; OnPropertyChanged(); }
    }

    public double Duration
    {
        get => duration;
        set { duration = value; OnPropertyChanged(); }
    }

    // 🔥 Constructor cũng phải xử lý điều kiện
#if ANDROID
    public AudioViewModel(ExoPlayerService service)
    {
        audioService = service;

        audioService.OnProgress += p =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                CurrentPosition = p;
            });
        };

        audioService.OnDuration += d =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Duration = d;
            });
        };
    }
#else
    // Constructor mặc định cho Windows để không bị lỗi compile
    public AudioViewModel() { }
#endif

    public void Play(string filePath)
    {
#if ANDROID
        audioService.Play(filePath);
#endif
    }

    public void Pause()
    {
#if ANDROID
        audioService.Pause();
#endif
    }

    public void Stop()
    {
#if ANDROID
        audioService.Stop();
#endif
    }

    public void Seek(double sec)
    {
#if ANDROID
        audioService.Seek(sec);
#endif
        CurrentPosition = sec;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}