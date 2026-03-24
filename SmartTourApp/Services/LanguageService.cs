using SmartTourApp.Data;

namespace SmartTourApp.Services;

public class LanguageService
{
    private readonly Database db;
    private string current;

    public LanguageService(Database db)
    {
        this.db = db;
        current = db.GetSetting("lang") ?? "vi";
    }

    public string Current
    {
        get => current;
        set
        {
            current = value;
            db.SaveSetting("lang", value);
        }
    }
}