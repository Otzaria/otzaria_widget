// Agent D - per-widget customization. Persisted by the Widgets host as WidgetUpdateRequestOptions.CustomState (JSON string).
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OtzariaWidget.App.Provider.Cards;

/// <summary>
/// One flat, schema-versioned bag of options. Every kind reads only the fields it cares about.
/// Parsing never throws: garbage / missing / out-of-range values fall back to defaults.
/// </summary>
public sealed class WidgetCustomState
{
    public const int CurrentSchema = 1;
    public const string AllGroups = "all";
    public const string NoGroup = "none";

    public static readonly IReadOnlyList<string> DashboardSections = ["date", "zman", "reading", "progress", "bookmarks"];

    public int Version { get; set; } = CurrentSchema;

    /// <summary>Items to show; 0 = automatic (by widget size). Clamped to the size maximum when rendering.</summary>
    public int Count { get; set; }

    // RecentBooks
    public bool ShowSearches { get; set; }
    public bool ShowLocation { get; set; } = true;
    public bool IncludePdfs { get; set; } = true;

    // Bookmarks: "all" | "none" (ungrouped) | group id
    public string Group { get; set; } = AllGroups;

    // ShamorZachor: "inProgress" | "completed" | "all"
    public string SzFilter { get; set; } = "inProgress";
    public bool ShowAllColumns { get; set; }

    // Zmanim: City "" = follow Otzaria/global; ZmanimMode "follow" | "custom"
    public string City { get; set; } = "";
    public string ZmanimMode { get; set; } = "follow";
    public List<string> ZmanimIds { get; set; } = [];

    // HebrewCalendar
    public bool ShowDaf { get; set; } = true;
    /// <summary>null = follow the global setting WidgetAppSettings.ShowYerushalmiDaf.</summary>
    public bool? ShowYerushalmi { get; set; }
    public bool ShowOmer { get; set; } = true;
    public bool ShowCandle { get; set; } = true;

    // Dashboard: enabled sections in display order
    public List<string> Sections { get; set; } = [.. DashboardSections];

    public static WidgetCustomState Default() => new();

    public WidgetCustomState Clone() => Parse(Serialize());

    // ------------------------------------------------------------------------------------------------ serialization
    public string Serialize()
    {
        var o = new JsonObject
        {
            ["v"] = Version,
            ["count"] = Count,
            ["showSearches"] = ShowSearches,
            ["showLocation"] = ShowLocation,
            ["includePdfs"] = IncludePdfs,
            ["group"] = Group,
            ["szFilter"] = SzFilter,
            ["szAllColumns"] = ShowAllColumns,
            ["city"] = City,
            ["zmanimMode"] = ZmanimMode,
            ["zmanim"] = new JsonArray(ZmanimIds.Select(z => (JsonNode?)JsonValue.Create(z)).ToArray()),
            ["showDaf"] = ShowDaf,
            ["showOmer"] = ShowOmer,
            ["showCandle"] = ShowCandle,
            ["sections"] = new JsonArray(Sections.Select(z => (JsonNode?)JsonValue.Create(z)).ToArray()),
        };
        if (ShowYerushalmi is bool y) o["showYerushalmi"] = y;
        return o.ToJsonString(CardTemplates.JsonOptions);
    }

    /// <summary>Tolerant parse. Never throws; null/empty/garbage => defaults.</summary>
    public static WidgetCustomState Parse(string? json)
    {
        var s = new WidgetCustomState();
        if (string.IsNullOrWhiteSpace(json)) return s;
        JsonObject? o;
        try { o = JsonNode.Parse(json) as JsonObject; }
        catch (Exception) { return s; }
        if (o is null) return s;

        s.Version = Int(o["v"], CurrentSchema);
        s.Count = Math.Clamp(Int(o["count"], 0), 0, 12);
        s.ShowSearches = Bool(o["showSearches"], s.ShowSearches);
        s.ShowLocation = Bool(o["showLocation"], s.ShowLocation);
        s.IncludePdfs = Bool(o["includePdfs"], s.IncludePdfs);
        s.Group = Str(o["group"]) is { Length: > 0 } g ? g : AllGroups;
        s.SzFilter = Str(o["szFilter"]) is "inProgress" or "completed" or "all" ? Str(o["szFilter"])! : "inProgress";
        s.ShowAllColumns = Bool(o["szAllColumns"], s.ShowAllColumns);
        s.City = Str(o["city"]) ?? "";
        s.ZmanimMode = Str(o["zmanimMode"]) == "custom" ? "custom" : "follow";
        s.ZmanimIds = StrList(o["zmanim"]);
        s.ShowDaf = Bool(o["showDaf"], s.ShowDaf);
        s.ShowYerushalmi = o["showYerushalmi"] is null ? null : Bool(o["showYerushalmi"], false);
        s.ShowOmer = Bool(o["showOmer"], s.ShowOmer);
        s.ShowCandle = Bool(o["showCandle"], s.ShowCandle);
        if (o["sections"] is JsonArray)
        {
            var secs = StrList(o["sections"]).Where(x => DashboardSections.Contains(x)).Distinct().ToList();
            s.Sections = secs;   // an explicitly empty list is a valid choice (shows the empty state)
        }
        return s;
    }

    // ------------------------------------------------------------------------------------------------ customization card input
    /// <summary>
    /// Applies the Input.* values returned by the customization card (<c>args.Data</c>). Values arrive as strings
    /// ("true"/"false", "3", "a,b,c") but real JSON types are accepted as well. Unknown keys are ignored.
    /// </summary>
    public void ApplyInputs(string kind, string? inputsJson)
    {
        if (string.IsNullOrWhiteSpace(inputsJson)) return;
        JsonObject? o;
        try { o = JsonNode.Parse(inputsJson) as JsonObject; }
        catch (Exception) { return; }
        if (o is null) return;

        switch (kind)
        {
            case WidgetKinds.RecentBooks:
                if (o.ContainsKey("count")) Count = Math.Clamp(Int(o["count"], Count), 0, 12);
                if (o.ContainsKey("showSearches")) ShowSearches = Bool(o["showSearches"], ShowSearches);
                if (o.ContainsKey("showLocation")) ShowLocation = Bool(o["showLocation"], ShowLocation);
                if (o.ContainsKey("includePdfs")) IncludePdfs = Bool(o["includePdfs"], IncludePdfs);
                break;
            case WidgetKinds.Bookmarks:
                if (o.ContainsKey("count")) Count = Math.Clamp(Int(o["count"], Count), 0, 12);
                if (Str(o["group"]) is { Length: > 0 } g) Group = g;
                break;
            case WidgetKinds.ShamorZachor:
                if (o.ContainsKey("count")) Count = Math.Clamp(Int(o["count"], Count), 0, 12);
                if (Str(o["szFilter"]) is "inProgress" or "completed" or "all") SzFilter = Str(o["szFilter"])!;
                if (o.ContainsKey("szAllColumns")) ShowAllColumns = Bool(o["szAllColumns"], ShowAllColumns);
                break;
            case WidgetKinds.Zmanim:
                if (o.ContainsKey("city")) City = Str(o["city"]) ?? "";
                if (Str(o["zmanimMode"]) is "follow" or "custom") ZmanimMode = Str(o["zmanimMode"])!;
                if (o.ContainsKey("zmanim")) ZmanimIds = StrList(o["zmanim"]);
                break;
            case WidgetKinds.HebrewCalendar:
                if (o.ContainsKey("showDaf")) ShowDaf = Bool(o["showDaf"], ShowDaf);
                if (o.ContainsKey("showYerushalmi")) ShowYerushalmi = Bool(o["showYerushalmi"], false);
                if (o.ContainsKey("showOmer")) ShowOmer = Bool(o["showOmer"], ShowOmer);
                if (o.ContainsKey("showCandle")) ShowCandle = Bool(o["showCandle"], ShowCandle);
                break;
            case WidgetKinds.Dashboard:
                // pos_<section> = 0 (hidden) | 1..5 (order)
                var picked = new List<(string Sec, int Pos, int Idx)>();
                for (int i = 0; i < DashboardSections.Count; i++)
                {
                    var sec = DashboardSections[i];
                    if (!o.ContainsKey("pos_" + sec)) continue;
                    int pos = Math.Clamp(Int(o["pos_" + sec], 0), 0, 9);
                    if (pos > 0) picked.Add((sec, pos, i));
                }
                if (o.Any(kv => kv.Key.StartsWith("pos_", StringComparison.Ordinal)))
                    Sections = picked.OrderBy(p => p.Pos).ThenBy(p => p.Idx).Select(p => p.Sec).ToList();
                break;
        }
    }

    // ------------------------------------------------------------------------------------------------ json helpers
    private static string? Str(JsonNode? n)
    {
        if (n is null) return null;
        try
        {
            if (n is JsonValue v)
            {
                if (v.TryGetValue<string>(out var s)) return s;
                return v.ToJsonString().Trim('"');
            }
        }
        catch (Exception) { }
        return null;
    }

    private static int Int(JsonNode? n, int def)
    {
        if (n is not JsonValue v) return def;
        try
        {
            if (v.TryGetValue<int>(out var i)) return i;
            if (v.TryGetValue<double>(out var d)) return (int)Math.Round(d);
            if (v.TryGetValue<string>(out var s) &&
                double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var ds)) return (int)Math.Round(ds);
        }
        catch (Exception) { }
        return def;
    }

    private static bool Bool(JsonNode? n, bool def)
    {
        if (n is not JsonValue v) return def;
        try
        {
            if (v.TryGetValue<bool>(out var b)) return b;
            if (v.TryGetValue<string>(out var s))
            {
                if (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1") return true;
                if (s.Equals("false", StringComparison.OrdinalIgnoreCase) || s == "0") return false;
            }
            if (v.TryGetValue<int>(out var i)) return i != 0;
        }
        catch (Exception) { }
        return def;
    }

    /// <summary>Accepts a JSON array of strings or a comma separated string (multi-select ChoiceSet value).</summary>
    private static List<string> StrList(JsonNode? n)
    {
        var res = new List<string>();
        if (n is JsonArray a)
        {
            foreach (var e in a) if (Str(e) is { Length: > 0 } s) res.Add(s);
        }
        else if (Str(n) is { Length: > 0 } csv)
        {
            res.AddRange(csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        return res;
    }
}
