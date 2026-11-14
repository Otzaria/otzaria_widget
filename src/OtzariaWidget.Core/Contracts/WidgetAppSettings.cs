// ============================================================================
//  SHARED CONTRACTS — owned by the project lead.
//  The widget's OWN global settings (never written into Otzaria's files).
//  Persisted as JSON by ISettingsStore at %LOCALAPPDATA%\OtzariaWidget\settings.json
//  (inside the MSIX this is transparently redirected to the package LocalCache;
//  the provider and the settings UI share the same package so they see the same file).
// ============================================================================
namespace OtzariaWidget.Core.Contracts;

public enum LinkOpenMode
{
    /// <summary>Card button uses Action.OpenUrl with the otzaria:// link (host launches it).</summary>
    OpenUrl,
    /// <summary>Card button uses Action.Execute; the provider launches otzaria.exe "uri" itself.</summary>
    ProviderLaunch,
}

public enum ThemeMode { FollowBoard, Light, Dark }

public sealed class WidgetAppSettings
{
    public int SchemaVersion { get; set; } = 1;

    // ---- Data locations (null = auto-detect) ----
    public string? DataRootOverride { get; set; }
    public string? DatabasesDirOverride { get; set; }
    public string? LibraryDbOverride { get; set; }
    public string? OtzariaExeOverride { get; set; }

    // ---- Behaviour ----
    public LinkOpenMode LinkMode { get; set; } = LinkOpenMode.OpenUrl;
    public ThemeMode Theme { get; set; } = ThemeMode.FollowBoard;
    /// <summary>Accent color for cards, "#RRGGBB"; null = use Otzaria's seed color / default.</summary>
    public string? AccentColor { get; set; }

    // ---- Calendar / zmanim (null = follow Otzaria's own settings) ----
    public string? CityOverride { get; set; }
    public List<string>? ZmanimOverride { get; set; }
    public bool? TwelveHourClockOverride { get; set; }
    public string? DayTransitionOverride { get; set; }
    public bool ShowYerushalmiDaf { get; set; }

    // ---- Privacy ----
    /// <summary>Hide searches from all widgets.</summary>
    public bool HideSearches { get; set; }

    // ---- Settings window ----
    /// <summary>Language of the settings window: "system" (follow the Windows display language), "he" or "en". Cards are not affected.</summary>
    public string UiLanguage { get; set; } = "system";
}

public interface ISettingsStore
{
    WidgetAppSettings Load();
    void Save(WidgetAppSettings settings);
    string SettingsFilePath { get; }
    event EventHandler? Changed;
}
