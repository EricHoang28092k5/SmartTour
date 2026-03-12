using SmartTourApp.Data;

namespace SmartTourApp.Services;

public class LanguageService
{
    private readonly Database db;

    public LanguageService(Database db)
    {
        this.db = db;
    }

    public string Current
    {
        get => db.GetSetting("lang", "vi");
        set => db.SaveSetting("lang", value);
    }
}