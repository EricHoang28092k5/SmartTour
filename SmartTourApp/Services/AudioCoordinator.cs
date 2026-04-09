namespace SmartTourApp.Services;

/// <summary>
/// Định nghĩa các nguồn phát Audio - Đưa ra ngoài class để các file khác dễ gọi
/// </summary>
public enum AudioSource { None, Auto, HomeManual, DetailManual }

public class AudioCoordinator
{
    private AudioSource _activeSource = AudioSource.None;
    private Action? _stopActive = null;
    private readonly object _lock = new();

    public AudioSource ActiveSource
    {
        get { lock (_lock) return _activeSource; }
    }

    public bool IsAnyPlaying
    {
        get { lock (_lock) return _activeSource != AudioSource.None; }
    }

    public bool RequestPlay(AudioSource source, Action stopCallback)
    {
        Action? prevStop;

        lock (_lock)
        {
            prevStop = _stopActive;
            _activeSource = source;
            _stopActive = stopCallback;
        }

        if (prevStop != null)
        {
            try
            {
                prevStop.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AudioCoordinator] Stop error: {ex.Message}");
            }
        }

        return true;
    }

    public void NotifyStop(AudioSource source)
    {
        lock (_lock)
        {
            if (_activeSource == source)
            {
                _activeSource = AudioSource.None;
                _stopActive = null;
            }
        }
    }

    public bool IsActiveSource(AudioSource source)
    {
        lock (_lock) return _activeSource == source;
    }

    public void Reset()
    {
        lock (_lock)
        {
            _activeSource = AudioSource.None;
            _stopActive = null;
        }
    }
}