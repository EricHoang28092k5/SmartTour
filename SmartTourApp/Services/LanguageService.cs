using SmartTourApp.Data;

namespace SmartTourApp.Services;

public class LanguageService
{
    private readonly Database db;
    private string current;

    // Mặc định "en" cho app mới
    private const string DefaultLang = "en";

    public LanguageService(Database db)
    {
        this.db = db;
        // Lấy từ DB — nếu chưa có thì dùng "en" (YC2: default en)
        current = db.GetSetting("lang") ?? DefaultLang;
    }

    public string Current
    {
        get => current;
        set
        {
            if (current == value) return;
            current = value;
            db.SaveSetting("lang", value);
            OnLanguageChanged?.Invoke(value);
        }
    }

    /// <summary>
    /// Fired when language changes — UI subscribes to refresh text.
    /// </summary>
    public event Action<string>? OnLanguageChanged;
}
