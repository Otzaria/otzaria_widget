// Agent D - registry + the "never throw" entry points the provider uses.
using System.Text.Json.Nodes;

namespace OtzariaWidget.App.Provider.Cards;

public static class CardBuilders
{
    private static readonly IReadOnlyDictionary<string, IWidgetCardBuilder> Map = new Dictionary<string, IWidgetCardBuilder>(StringComparer.Ordinal)
    {
        [WidgetKinds.RecentBooks] = new RecentBooksBuilder(),
        [WidgetKinds.Bookmarks] = new BookmarksBuilder(),
        [WidgetKinds.ShamorZachor] = new ShamorZachorBuilder(),
        [WidgetKinds.Zmanim] = new ZmanimBuilder(),
        [WidgetKinds.HebrewCalendar] = new HebrewCalendarBuilder(),
        [WidgetKinds.Dashboard] = new DashboardBuilder(),
    };

    public static IWidgetCardBuilder? Get(string kind) => Map.TryGetValue(kind, out var b) ? b : null;

    /// <summary>Builds the card; any exception (including NotImplementedException from stubbed services) becomes the error card.</summary>
    public static CardResult BuildSafe(CardContext ctx, Action<Exception>? onError = null)
    {
        try
        {
            var b = Get(ctx.Kind) ?? throw new ArgumentException("unknown widget kind: " + ctx.Kind);
            return b.Build(ctx);
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
            return Error(ctx, ex);
        }
    }

    public static CardResult BuildCustomizationSafe(CardContext ctx, Action<Exception>? onError = null)
    {
        try
        {
            var b = Get(ctx.Kind) ?? throw new ArgumentException("unknown widget kind: " + ctx.Kind);
            return b.BuildCustomization(ctx);
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
            return Error(ctx, ex);
        }
    }

    /// <summary>Generic error card: Hebrew message, "try again" + "open settings". No exception text (it may contain paths).</summary>
    public static CardResult Error(CardContext ctx, Exception? ex = null)
    {
        var root = new JsonObject
        {
            ["title"] = WidgetKinds.DisplayName(ctx.Kind),
            ["subtitle"] = "",
            ["state"] = "error",
            ["emptyText"] = ex is NotImplementedException ? "הנתונים עדיין אינם זמינים — פתח את ההגדרות" : "אירעה שגיאה בטעינת הנתונים",
            ["notice"] = ctx.Notice ?? "",
            ["launch"] = ctx.LaunchViaProvider,
        };
        return new CardResult(CardTemplates.Error, CardTemplates.ToJson(root));
    }
}
