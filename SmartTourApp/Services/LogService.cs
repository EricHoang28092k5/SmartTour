using SmartTourApp.Models;

namespace SmartTourApp.Services;

public class LogService
{
    //test
    private readonly List<PlayLog> logs = new();

    public void Add(PlayLog log)
    {
        logs.Add(log);
    }

    public List<PlayLog> GetLogs()
    {
        return logs;
    }
}