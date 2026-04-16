using SmartTourApp.Data;

namespace SmartTourApp.Services;

public class LanguageService
{
    private readonly Database db;
    private string current;

    /// <summary>
    /// YC5: Default language is English for fresh installs.
    /// Preference key to track whether user has explicitly set language.
    /// </summary>
    private const string DefaultLang = "en";
    private const string FirstLaunchKey = "lang_first_launch_done";

    public LanguageService(Database db)
    {
        this.db = db;

        // YC5: On very first launch, always set English regardless of DB state.
        // After user changes language in Settings, we persist their choice.
        bool firstLaunchDone = Preferences.Default.Get(FirstLaunchKey, false);

        if (!firstLaunchDone)
        {
            // First install: force English, save to DB and mark done
            current = DefaultLang;
            db.SaveSetting("lang", DefaultLang);
            Preferences.Default.Set(FirstLaunchKey, true);
        }
        else
        {
            // Returning user: load their saved preference
            current = db.GetSetting("lang") ?? DefaultLang;
        }
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
