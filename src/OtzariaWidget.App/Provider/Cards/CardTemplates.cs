// Agent D - template file names, loading and JSON serialization settings.
using System.IO;
using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OtzariaWidget.App.Provider.Cards;

public static class CardTemplates
{
    public const string RecentBooks = "recent-books.json";
    public const string Bookmarks = "bookmarks.json";
    public const string ShamorZachor = "shamor-zachor.json";
    public const string Zmanim = "zmanim.json";
    public const string HebrewCalendar = "hebrew-calendar.json";
    public const string Dashboard = "dashboard.json";
    public const string Error = "error.json";
    public const string Customize = "customize.json";

    public static readonly IReadOnlyList<string> All =
        [RecentBooks, Bookmarks, ShamorZachor, Zmanim, HebrewCalendar, Dashboard, Error, Customize];

    /// <summary>Overridable for tests; default is <c>&lt;exe dir&gt;\Templates</c>.</summary>
    public static string Directory { get; set; } = Path.Combine(AppContext.BaseDirectory, "Templates");

    private static readonly ConcurrentDictionary<string, string> Cache = new();

    public static string Load(string file) =>
        Cache.GetOrAdd(Path.Combine(Directory, file), p => File.ReadAllText(p, System.Text.Encoding.UTF8));

    /// <summary>Hebrew stays readable in the payload (no \uXXXX bloat).</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    public static string ToJson(JsonNode node) => node.ToJsonString(JsonOptions);
}
