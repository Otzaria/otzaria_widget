using System.Text.Json.Nodes;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.Provider.Cards;

/// <summary>"Continue reading" hero (current tab, else newest history entry) + recent books (+ optional recent searches).</summary>
public sealed class RecentBooksBuilder : IWidgetCardBuilder
{
    public string Kind => WidgetKinds.RecentBooks;

    public CardResult Build(CardContext ctx)
    {
        var st = ctx.State;
        var root = CardData.Root(ctx, "ספרים אחרונים", "", "ok");
        root["headerEnd"] = "";
        CardData.SetLink(root, CardData.HistoryUrl, ctx);
        root["hero"] = null;
        root["rows"] = new JsonArray();
        root["searches"] = new JsonArray();
        root["pager"] = new JsonObject { ["show"] = false, ["hasPrev"] = false, ["hasNext"] = false, ["label"] = "", ["total"] = 0 };

        var missing = CardData.MissingReason(ctx, p => p.UserStateDb, SourceNames.UserState);
        if (missing is not null)
        {
            root["state"] = "missing";
            root["emptyText"] = missing;
            return Result(ctx, root);
        }

        var snap = ctx.Snapshot!;
        bool Allowed(ReadingEntry e) => st.IncludePdfs || !e.Book.IsPdf;

        var recents = snap.RecentBooks.Where(Allowed).ToList();

        // Hero: the tab that is open right now, else the newest entry.
        var tab = snap.OpenTabs.FirstOrDefault(t => t.IsCurrent && t.Reading is not null && Allowed(t.Reading));
        ReadingEntry? heroEntry = tab?.Reading ?? recents.FirstOrDefault();
        string heroLabel = tab is not null ? "המשך קריאה" : "קריאה אחרונה";

        if (heroEntry is null)
        {
            root["state"] = "empty";
            root["emptyText"] = "לא נמצאו ספרים אחרונים";
            return Result(ctx, root);
        }

        var hero = ReadingRows.Row(heroEntry, ctx, st.ShowLocation);
        hero["label"] = heroLabel;
        if (tab is not null) hero["meta"] = heroEntry.Book.IsPdf ? "PDF" : "";   // the open tab has no meaningful "when"
        if (tab?.DeepLink is { Length: > 0 } tl) CardData.SetLink(hero, tl, ctx);
        root["hero"] = hero;

        int effectivePage = 0;
        int total = CardData.Rows(ctx);
        int pageSize = Math.Max(0, total - 1);
        var heroKey = ReadingRows.Key(heroEntry);
        var rest = recents.Where(e => ReadingRows.Key(e) != heroKey).Take(24).ToList();

        if (ctx.Size != CardSize.Small && pageSize > 0)
        {
            var (page, pager) = CardData.Paginate(rest, pageSize, ctx.Page, ctx.Size == CardSize.Large);
            var rows = new JsonArray();
            foreach (var e in page) rows.Add(ReadingRows.Row(e, ctx, st.ShowLocation));
            root["rows"] = rows;
            root["pager"] = pager;
            effectivePage = (int)pager["index"]!;
        }

        if (st.ShowSearches && !ctx.Settings.HideSearches && ctx.Size != CardSize.Small)
        {
            int n = ctx.Size == CardSize.Large ? 2 : 1;
            var arr = new JsonArray();
            foreach (var s in snap.RecentSearches.Take(n)) arr.Add(ReadingRows.SearchRow(s, ctx));
            root["searches"] = arr;
        }
        return Result(ctx, root, effectivePage);
    }

    public CardResult BuildCustomization(CardContext ctx)
    {
        var st = ctx.State;
        var fields = new JsonArray
        {
            Fields.Number("count", "מספר פריטים (0 = אוטומטי)", st.Count, 0, 8),
            Fields.Toggle("showLocation", "הצג את המיקום בספר", st.ShowLocation),
            Fields.Toggle("includePdfs", "כלול ספרי PDF", st.IncludePdfs),
            Fields.Toggle("showSearches", "הצג חיפושים אחרונים", st.ShowSearches),
        };
        return Fields.Card(Kind, fields);
    }

    // Relative day labels ("היום"/"אתמול") change at local midnight.
    private static CardResult Result(CardContext ctx, JsonObject root, int page = 0) =>
        new(CardTemplates.RecentBooks, CardTemplates.ToJson(root), CalendarContext.NextLocalMidnight(ctx.Now, null), page);
}
