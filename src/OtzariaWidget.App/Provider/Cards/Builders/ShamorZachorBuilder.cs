using System.Text.Json.Nodes;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.Provider.Cards;

/// <summary>Shamor Vezachor: books with progress bars (headline ring, or every visible column on Large).</summary>
public sealed class ShamorZachorBuilder : IWidgetCardBuilder
{
    public string Kind => WidgetKinds.ShamorZachor;

    public CardResult Build(CardContext ctx)
    {
        var st = ctx.State;
        var root = CardData.Root(ctx, "שמור וזכור", FilterLabel(st.SzFilter), "ok");
        root["headerEnd"] = "";
        CardData.SetLink(root, CardData.ShamorZachorUrl, ctx);
        root["rows"] = new JsonArray();
        root["pager"] = new JsonObject { ["show"] = false, ["hasPrev"] = false, ["hasNext"] = false, ["label"] = "", ["total"] = 0 };

        // The hive (progress is stored in it) and the progress computation are required; a missing seforim.db only costs us
        // the book titles / totals ("ספר N"), so the list is still shown with a subtle note.
        var missing = CardData.MissingReason(ctx, p => p.PreferencesHive, SourceNames.Preferences, SourceNames.Progress);
        if (missing is not null)
        {
            root["state"] = "missing";
            root["emptyText"] = missing;
            return new CardResult(CardTemplates.ShamorZachor, CardTemplates.ToJson(root));
        }

        var all = ctx.Snapshot!.Progress;
        var list = all.Where(b => Matches(b, st.SzFilter)).ToList();
        if (list.Count == 0)
        {
            root["state"] = "empty";
            root["emptyText"] = st.SzFilter switch
            {
                "completed" => "אין ספרים שהושלמו",
                "all" => "לא נמצאו ספרים בשמור וזכור",
                _ => "אין ספרים בתהליך",
            };
            return new CardResult(CardTemplates.ShamorZachor, CardTemplates.ToJson(root));
        }
        root["headerEnd"] = CardFormat.Number(list.Count) + st.SzFilter switch { "completed" => " הושלמו", "all" => " ספרים", _ => " בתהליך" };
        if (CardData.StatusProblem(ctx.Snapshot!, SourceNames.Library) is not null)
            root["note"] = "ספריית אוצריא לא זמינה — שמות הספרים עשויים להיות חלקיים";

        bool allCols = st.ShowAllColumns && ctx.Size == CardSize.Large;
        int def = ctx.Size switch { CardSize.Small => 1, CardSize.Medium => 3, _ => allCols ? 2 : 4 };
        int rowsN = CardData.Rows(ctx, def);
        if (allCols) rowsN = Math.Min(rowsN, 4);

        var (page, pager) = CardData.Paginate(list.Take(40).ToList(), rowsN, ctx.Page, ctx.Size == CardSize.Large);
        var rows = new JsonArray();
        foreach (var b in page) rows.Add(Row(b, ctx, allCols));
        root["rows"] = rows;
        root["pager"] = pager;
        return new CardResult(CardTemplates.ShamorZachor, CardTemplates.ToJson(root), null, (int)pager["index"]!);
    }

    internal static bool Matches(BookProgress b, string filter) => filter switch
    {
        "completed" => b.IsCompleted,
        "all" => true,
        _ => (b.IsTracked || b.IsInProgress) && !b.IsCompleted,
    };

    private static string FilterLabel(string f) => f switch { "completed" => "הושלמו", "all" => "כל הספרים", _ => "בתהליך" };

    private static JsonObject Row(BookProgress b, CardContext ctx, bool allCols)
    {
        int segs = ctx.Size switch { CardSize.Small => CardData.BarSegmentsSmall, CardSize.Medium => CardData.BarSegmentsMedium, _ => CardData.BarSegmentsLarge };
        var bars = new JsonArray();
        if (allCols && b.VisibleColumns.Count > 0)
        {
            foreach (var c in b.VisibleColumns)
            {
                var bar = CardData.BarText(c.Ratio, CardData.BarSegmentsColumn, c.Ratio >= 1);
                bar["label"] = c.Label;
                bar["pct"] = CardFormat.Percent(c.Ratio);
                bars.Add(bar);
            }
        }

        string sub = b.IsCompleted
            ? (b.CompletedAt is { } d ? "הושלם ב-" + CardFormat.GregorianShort(DateOnly.FromDateTime(d.ToLocalTime().DateTime)) : "הושלם")
            : b.StatusText;
        var row = new JsonObject
        {
            ["text"] = CardFormat.Clip(b.Title, 60),
            ["meta"] = b.IsCompleted ? "הושלם ✓" : CardFormat.Percent(b.Headline),
            ["sub"] = ctx.Size == CardSize.Large ? sub : "",
            ["cycle"] = b.IsCompleted ? "" : (b.CurrentCycleText ?? ""),
            ["complete"] = b.IsCompleted,
            ["bar"] = CardData.BarText(b.Headline, segs, b.IsCompleted),
            ["bars"] = bars,
        };
        CardData.SetLink(row, b.DeepLink ?? CardData.ShamorZachorUrl, ctx);
        return row;
    }

    public CardResult BuildCustomization(CardContext ctx)
    {
        var st = ctx.State;
        var fields = new JsonArray
        {
            Fields.Choice("szFilter", "הצג", st.SzFilter,
            [
                ("ספרים בתהליך", "inProgress"),
                ("ספרים שהושלמו", "completed"),
                ("הכל", "all"),
            ]),
            Fields.Number("count", "מספר פריטים (0 = אוטומטי)", st.Count, 0, 8),
            Fields.Toggle("szAllColumns", "הצג את כל העמודות (חזרות) בווידג'ט הגדול", st.ShowAllColumns),
        };
        return Fields.Card(Kind, fields);
    }
}
