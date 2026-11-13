// Agent D - helpers shared by the builders: link actions, rows, progress bars, size caps, data-health evaluation.
using System.Text.Json.Nodes;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.Provider.Cards;

public static class Verbs
{
    public const string Open = "open";
    public const string OpenOtzaria = "openOtzaria";
    public const string Settings = "settings";
    public const string Refresh = "refresh";
    public const string Next = "next";
    public const string Prev = "prev";
    public const string SaveSettings = "saveSettings";
    public const string ExitCustomization = "exitCustomization";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
        { Open, OpenOtzaria, Settings, Refresh, Next, Prev, SaveSettings, ExitCustomization };
}

public static class CardData
{
    public const string LibraryUrl = "otzaria://open/library";
    public const string HistoryUrl = "otzaria://open/history";
    public const string BookmarksUrl = "otzaria://open/bookmarks";
    public const string CalendarUrl = "otzaria://open/calendar";
    public const string ShamorZachorUrl = "otzaria://open/shamor_zachor";
    /// <summary>Protocol registered by our own manifest; starts the settings window (used when Execute is unavailable).</summary>
    public const string SettingsUrl = "otzaria-widget://settings";

    // ---------------------------------------------------------------------------------------------- actions
    public static JsonObject OpenUrlAction(string url) => new() { ["type"] = "Action.OpenUrl", ["url"] = url };

    public static JsonObject ExecuteAction(string verb, JsonObject? data = null)
    {
        var a = new JsonObject { ["type"] = "Action.Execute", ["verb"] = verb };
        if (data is not null) a["data"] = data;
        return a;
    }

    /// <summary>
    /// Adds the link actions to a row/section object. Templates pick between them with
    /// <c>if($host.isUserContextAuthenticated == false || action == null, urlAction, action)</c>:
    /// <c>urlAction</c> always exists (works unauthenticated), <c>action</c> only in ProviderLaunch mode.
    /// A null url means "not linkable" and falls back to opening the library.
    /// </summary>
    public static void SetLink(JsonObject target, string? url, CardContext ctx)
    {
        var u = string.IsNullOrWhiteSpace(url) ? LibraryUrl : url!;
        target["urlAction"] = OpenUrlAction(u);
        if (ctx.LaunchViaProvider)
            target["action"] = ExecuteAction(Verbs.Open, new JsonObject { ["url"] = u });
    }

    // ---------------------------------------------------------------------------------------------- common root fields
    /// <summary>Fields every template reads (header, state, footer, link mode).</summary>
    public static JsonObject Root(CardContext ctx, string title, string subtitle, string state, string? emptyText = null)
    {
        var o = new JsonObject
        {
            ["title"] = title,
            ["subtitle"] = subtitle,
            ["state"] = state,                       // ok | empty | missing | error
            ["emptyText"] = emptyText ?? "",
            ["notice"] = ctx.Notice ?? "",
            ["note"] = "",                           // subtle footnote (e.g. "library unavailable")
            ["launch"] = ctx.LaunchViaProvider,
        };
        return o;
    }

    public const string MissingText = "לא נמצאו נתוני אוצריא — פתח את ההגדרות";

    // ---------------------------------------------------------------------------------------------- size caps
    /// <summary>Default rows per size for list widgets: S=1, M=3, L=7 (includes the hero row where there is one).</summary>
    public static int DefaultRows(CardSize size) => size switch { CardSize.Small => 1, CardSize.Medium => 3, _ => 7 };

    /// <summary>Upper bound: S=2, M=4, L=8.</summary>
    public static int MaxRows(CardSize size) => size switch { CardSize.Small => 2, CardSize.Medium => 4, _ => 8 };

    public static int Rows(CardContext ctx, int? defaultOverride = null)
    {
        int def = defaultOverride ?? DefaultRows(ctx.Size);
        int n = ctx.State.Count > 0 ? ctx.State.Count : def;
        return Math.Clamp(n, 1, MaxRows(ctx.Size));
    }

    // ---------------------------------------------------------------------------------------------- paging
    public static (IReadOnlyList<T> Page, JsonObject Pager) Paginate<T>(IReadOnlyList<T> all, int pageSize, int page, bool showPager = true)
    {
        pageSize = Math.Max(1, pageSize);
        int pages = Math.Max(1, (all.Count + pageSize - 1) / pageSize);
        int p = Math.Clamp(page, 0, pages - 1);
        var slice = all.Skip(p * pageSize).Take(pageSize).ToList();
        var pager = new JsonObject
        {
            ["show"] = showPager && pages > 1,
            ["hasPrev"] = p > 0,
            ["hasNext"] = p < pages - 1,
            ["label"] = $"{CardFormat.Number(p + 1)} / {CardFormat.Number(pages)}",
            ["total"] = all.Count,
            ["index"] = p,
        };
        return (slice, pager);
    }

    // ---------------------------------------------------------------------------------------------- progress bar
    /// <summary>
    /// Legacy weighted-column bar [{w, style}] (kept for tests/compat). The templates use <see cref="BarText"/>: styled containers get
    /// a host-defined padding on the board, which would make the bar thick; glyph text bars look the same everywhere.
    /// </summary>
    public static JsonArray Bar(double ratio, bool complete = false)
    {
        int fill = (int)Math.Round(Math.Clamp(ratio, 0, 1) * 100, MidpointRounding.AwayFromZero);
        var arr = new JsonArray();
        if (fill > 0) arr.Add(new JsonObject { ["w"] = fill, ["style"] = complete ? "good" : "accent" });
        if (fill < 100) arr.Add(new JsonObject { ["w"] = 100 - fill, ["style"] = "emphasis" });
        return arr;
    }

    /// <summary>Segment counts (glyph advance at 12px is ~12 px): small 10 = 120 px, medium 8 = 96 px (inline with title and percent), large 18 = 216 px, per-column bars 14.</summary>
    public const int BarSegmentsSmall = 10, BarSegmentsMedium = 8, BarSegmentsLarge = 18, BarSegmentsColumn = 14;

    /// <summary>Glyph bar: {fill, rest, color}. The template renders two TextBlocks: <c>fill</c> in <c>color</c>, <c>rest</c> subtle. Empty strings are omitted by <c>$when</c>.</summary>
    public static JsonObject BarText(double ratio, int segments, bool complete = false)
    {
        var (fill, rest) = CardFormat.BarGlyphs(ratio, segments);
        return new JsonObject { ["fill"] = fill, ["rest"] = rest, ["color"] = complete ? "good" : "accent" };
    }

    // ---------------------------------------------------------------------------------------------- data health
    /// <summary>
    /// Decides whether the data the widget depends on is missing. <paramref name="sourceNames"/> are exact
    /// <see cref="SourceStatus.Name"/> values (<see cref="SourceNames"/>), matched with ordinal equality.
    /// Returns null when OK, else a Hebrew message.
    /// </summary>
    public static string? MissingReason(CardContext ctx, Func<OtzariaPaths, string?> requiredPath, params string[] sourceNames)
    {
        var snap = ctx.Snapshot;
        if (snap is null) return MissingText;
        if (string.IsNullOrWhiteSpace(requiredPath(snap.Paths))) return MissingText;
        return StatusProblem(snap, sourceNames);
    }

    /// <summary>First non-OK status (exact name match) among the given sources, as a Hebrew message; null when all are OK or absent.</summary>
    public static string? StatusProblem(OtzariaSnapshot snap, params string[] sourceNames)
    {
        foreach (var st in snap.Status)
        {
            if (st.Health == DataHealth.Ok) continue;
            if (sourceNames.Any(n => string.Equals(st.Name, n, StringComparison.Ordinal)))
                return string.IsNullOrWhiteSpace(st.Message) ? MissingText : st.Message!;
        }
        return null;
    }

    public static JsonArray StringArray(IEnumerable<string> items)
    {
        var a = new JsonArray();
        foreach (var s in items) a.Add(s);
        return a;
    }
}
