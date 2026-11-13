// Agent D - widget kind / size vocabulary. Pure code (no WinRT) so the card tests can compile it directly.
namespace OtzariaWidget.App.Provider.Cards;

/// <summary>Identifiers that must match packaging\Package.appxmanifest (a test enforces it).</summary>
public static class ProviderIds
{
    /// <summary>CLSID of the widget provider COM class (manifest: com:Class Id AND CreateInstance ClassId).</summary>
    public static readonly Guid Clsid = new("EB0AF4C1-58BE-4FA6-9F1B-88F8188C6326");
    public const string PackageName = "Otzaria.Widget";
    public const string SettingsProtocol = "otzaria-widget";
}

/// <summary>Widget definition ids (identical to the <c>Definition Id</c> values in packaging\Package.appxmanifest).</summary>
public static class WidgetKinds
{
    public const string RecentBooks = "RecentBooks";
    public const string Bookmarks = "Bookmarks";
    public const string ShamorZachor = "ShamorZachor";
    public const string Zmanim = "Zmanim";
    public const string HebrewCalendar = "HebrewCalendar";
    public const string Dashboard = "Dashboard";

    public static readonly IReadOnlyList<string> All =
        [RecentBooks, Bookmarks, ShamorZachor, Zmanim, HebrewCalendar, Dashboard];

    public static bool IsKnown(string? id) => id is not null && All.Contains(id, StringComparer.Ordinal);

    /// <summary>Hebrew display name (same strings as the manifest).</summary>
    public static string DisplayName(string kind) => kind switch
    {
        RecentBooks => "אוצריא · ספרים אחרונים",
        Bookmarks => "אוצריא · סימניות",
        ShamorZachor => "שמור וזכור",
        Zmanim => "זמני היום",
        HebrewCalendar => "לוח עברי",
        Dashboard => "אוצריא · לוח אישי",
        _ => "אוצריא",
    };
}

public enum CardSize { Small, Medium, Large }

public static class CardSizes
{
    /// <summary>Value of <c>$host.widgetSize</c>.</summary>
    public static string ToHostName(this CardSize s) => s switch
    {
        CardSize.Small => "small",
        CardSize.Medium => "medium",
        _ => "large",
    };

    public static CardSize Parse(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "small" => CardSize.Small,
        "medium" => CardSize.Medium,
        _ => CardSize.Large,
    };
}
