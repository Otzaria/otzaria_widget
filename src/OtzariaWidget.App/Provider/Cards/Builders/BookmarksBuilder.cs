using System.Text.Json.Nodes;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.Provider.Cards;

/// <summary>Bookmarks (label or location + book), optionally limited to one group / ungrouped.</summary>
public sealed class BookmarksBuilder : IWidgetCardBuilder
{
    public string Kind => WidgetKinds.Bookmarks;

    public CardResult Build(CardContext ctx)
    {
        var st = ctx.State;
        var root = CardData.Root(ctx, "סימניות", "", "ok");
        root["headerEnd"] = "";
        CardData.SetLink(root, CardData.BookmarksUrl, ctx);
        root["rows"] = new JsonArray();
        root["pager"] = new JsonObject { ["show"] = false, ["hasPrev"] = false, ["hasNext"] = false, ["label"] = "", ["total"] = 0 };

        var missing = CardData.MissingReason(ctx, p => p.UserStateDb, SourceNames.UserState);
        if (missing is not null)
        {
            root["state"] = "missing";
            root["emptyText"] = missing;
            return new CardResult(CardTemplates.Bookmarks, CardTemplates.ToJson(root));
        }

        var snap = ctx.Snapshot!;
        IEnumerable<ReadingEntry> src = snap.Bookmarks;
        string groupName = "";
        if (st.Group == WidgetCustomState.NoGroup)
        {
            var grouped = new HashSet<string>(snap.BookmarkGroups.SelectMany(g => g.Items).Select(ItemKey));
            src = src.Where(b => !grouped.Contains(ItemKey(b)));
            groupName = "ללא קבוצה";
        }
        else if (st.Group != WidgetCustomState.AllGroups)
        {
            var g = snap.BookmarkGroups.FirstOrDefault(x => x.Id == st.Group);
            if (g is not null) { src = g.Items; groupName = g.Name; }
        }
        var list = src.Take(40).ToList();
        root["subtitle"] = groupName;

        if (list.Count == 0)
        {
            root["state"] = "empty";
            root["emptyText"] = groupName.Length > 0 ? "אין סימניות בקבוצה זו" : "לא נמצאו סימניות";
            return new CardResult(CardTemplates.Bookmarks, CardTemplates.ToJson(root));
        }

        var (page, pager) = CardData.Paginate(list, CardData.Rows(ctx, ctx.Size == CardSize.Large ? 5 : null), ctx.Page, ctx.Size == CardSize.Large);
        var rows = new JsonArray();
        foreach (var b in page)
        {
            // Label is the user's own text; show "ref · book" below it. Without a label the book is the headline.
            bool hasLabel = !string.IsNullOrWhiteSpace(b.Label);
            var loc = CardFormat.StripTitle(b.Ref, b.Book.Title);
            var sub = hasLabel
                ? string.Join(" · ", new[] { b.Book.Title, loc }.Where(x => !string.IsNullOrWhiteSpace(x)))
                : loc;
            var row = new JsonObject
            {
                ["text"] = CardFormat.Clip(hasLabel ? b.Label : b.Book.Title, 80),
                ["sub"] = st.ShowLocation || hasLabel ? CardFormat.Clip(sub, 80) : "",
                ["meta"] = b.Book.IsPdf ? "PDF" : "",
                ["short"] = CardFormat.Clip(loc, 22),    // one-line rows (medium): location at the row end
            };
            CardData.SetLink(row, b.DeepLink, ctx);
            rows.Add(row);
        }
        root["rows"] = rows;
        root["pager"] = pager;
        return new CardResult(CardTemplates.Bookmarks, CardTemplates.ToJson(root), null, (int)pager["index"]!);
    }

    private static string ItemKey(ReadingEntry e) => $"{ReadingRows.Key(e)}|{e.Index}";

    public CardResult BuildCustomization(CardContext ctx)
    {
        var st = ctx.State;
        var choices = new List<(string, string)>
        {
            ("כל הסימניות", WidgetCustomState.AllGroups),
            ("ללא קבוצה", WidgetCustomState.NoGroup),
        };
        foreach (var g in ctx.Snapshot?.BookmarkGroups ?? [])
            if (!string.IsNullOrWhiteSpace(g.Name)) choices.Add((g.Name, g.Id));
        var fields = new JsonArray
        {
            Fields.Choice("group", "קבוצה", st.Group, choices),
            Fields.Number("count", "מספר פריטים (0 = אוטומטי)", st.Count, 0, 8),
        };
        return Fields.Card(Kind, fields);
    }
}
