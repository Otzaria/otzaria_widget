// Agent D - row builders shared by RecentBooks / Bookmarks / Dashboard.
using System.Text.Json.Nodes;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.Provider.Cards;

internal static class ReadingRows
{
    /// <summary>Identity of a book for de-duplication (hero vs. list).</summary>
    public static string Key(ReadingEntry e) => $"{e.Book.Source}|{e.Book.Id?.ToString() ?? e.Book.Title}|{e.Book.Type}";

    public static JsonObject Row(ReadingEntry e, CardContext ctx, bool showLocation, string? meta = null)
    {
        // Otzaria's location text starts with the book title; the title is already the headline, so show only the position.
        var sub = showLocation ? CardFormat.StripTitle(e.Ref, e.Book.Title) : "";
        var row = new JsonObject
        {
            ["text"] = CardFormat.Clip(e.Label is { Length: > 0 } ? e.Label : e.Book.Title, 80),
            ["sub"] = CardFormat.Clip(sub, 70),
            ["meta"] = meta ?? DefaultMeta(e, ctx),
        };
        CardData.SetLink(row, e.DeepLink, ctx);
        return row;
    }

    public static string DefaultMeta(ReadingEntry e, CardContext ctx)
    {
        var parts = new List<string>(2);
        if (e.Book.IsPdf) parts.Add("PDF");
        var day = CardFormat.RelativeDay(e.CreatedAt, ctx.Now);
        if (day.Length > 0) parts.Add(day);
        return string.Join(" · ", parts);
    }

    public static JsonObject SearchRow(RecentSearch s, CardContext ctx)
    {
        var row = new JsonObject
        {
            ["text"] = CardFormat.Clip(s.DisplayText is { Length: > 0 } ? s.DisplayText : s.Query, 60),
            ["sub"] = s.Mode switch { "exact" => "חיפוש מדויק", "fuzzy" => "חיפוש מקורב", "smart" => "חיפוש חכם", _ => "" },
        };
        CardData.SetLink(row, s.DeepLink ?? ("otzaria://open/search?q=" + Uri.EscapeDataString(s.Query)), ctx);
        return row;
    }
}
