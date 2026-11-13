using System.Text.Json.Nodes;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.Provider.Cards;

/// <summary>Personal board: user-selected sections (date, next zman, continue reading, progress, bookmarks).</summary>
public sealed class DashboardBuilder : IWidgetCardBuilder
{
    public string Kind => WidgetKinds.Dashboard;

    private static readonly Dictionary<string, string> SectionTitles = new()
    {
        ["date"] = "התאריך היום",
        ["zman"] = "הזמן הבא",
        ["reading"] = "המשך קריאה",
        ["progress"] = "שמור וזכור",
        ["bookmarks"] = "סימניות",
    };

    /// <summary>Short caption shown at the start of each line (fixed-width column in the template).</summary>
    private static readonly Dictionary<string, string> Leads = new()
    {
        ["date"] = "תאריך",
        ["zman"] = "הזמן הבא",
        ["reading"] = "קריאה",
        ["progress"] = "שמור וזכור",
        ["bookmarks"] = "סימניות",
    };

    /// <summary>Approximate pixel height of a section with the given number of lines (large card layout) - used to fill the card without overflowing.</summary>
    private static int Height(string key, int lines) => key switch
    {
        "progress" => 40 * lines,                 // title line + bar line per book
        _ => 40 + 24 * (lines - 1),               // first line has a sub line, the following ones too
    };

    public CardResult Build(CardContext ctx)
    {
        bool compact = ctx.Size != CardSize.Large;
        var root = CardData.Root(ctx, "לוח אישי", "", "ok");
        root["headerEnd"] = "";
        root["compact"] = compact;
        CardData.SetLink(root, CardData.LibraryUrl, ctx);
        root["sections"] = new JsonArray();

        var enabled = ctx.State.Sections.Where(SectionTitles.ContainsKey).Distinct().ToList();
        if (enabled.Count == 0)
        {
            root["state"] = "empty";
            root["emptyText"] = "לא נבחרו חלקים להצגה — התאם את הווידג'ט";
            return new CardResult(CardTemplates.Dashboard, CardTemplates.ToJson(root));
        }

        int maxSections = ctx.Size switch { CardSize.Small => 2, CardSize.Medium => 3, _ => 5 };
        // Large: one line per section, then extra lines (most useful first) while the estimated height still fits (~224 of 264 px: the title row and gaps are not counted).
        var extra = new Dictionary<string, int>();
        if (!compact)
        {
            var shown = enabled.Take(maxSections).ToList();
            int used = shown.Sum(k => Height(k, 1));
            foreach (var key in new[] { "reading", "progress", "bookmarks", "zman" })
            {
                if (!shown.Contains(key)) continue;
                int add = Height(key, 2) - Height(key, 1);
                if (used + add > 224) continue;
                extra[key] = 1; used += add;
            }
        }

        var sections = new JsonArray();
        DateTimeOffset? refresh = null;
        Exception? firstError = null;
        bool anyDataMissing = false;

        foreach (var key in enabled)
        {
            if (sections.Count >= maxSections) break;
            int lines = 1 + (extra.TryGetValue(key, out var e) ? e : 0);
            try
            {
                List<JsonObject>? ls = key switch
                {
                    "date" => DateLines(ctx, compact, ref refresh),
                    "zman" => ZmanLines(ctx, lines, ref refresh),
                    "reading" => ReadingLines(ctx, lines, ref anyDataMissing),
                    "progress" => ProgressLines(ctx, lines, ref anyDataMissing),
                    "bookmarks" => BookmarkLines(ctx, lines, ref anyDataMissing),
                    _ => null,
                };
                if (ls is { Count: > 0 })
                {
                    var arr = new JsonArray();
                    for (int i = 0; i < ls.Count; i++)
                    {
                        var l = ls[i];
                        if (compact)
                        {
                            // one line: [lead][text][meta]; the sub text (location, countdown) moves to the end when there is no meta
                            if (!l.ContainsKey("meta") || string.IsNullOrEmpty(l["meta"]?.GetValue<string>()))
                                l["meta"] = CardFormat.Clip(l["sub"]?.GetValue<string>(), 16);
                            l["sub"] = "";
                            l.Remove("bar");
                        }
                        // every field the template binds must exist (an absent property would be rendered literally)
                        foreach (var (k, v) in new[] { ("sub", ""), ("meta", ""), ("weight", "default"), ("color", "default") })
                            if (!l.ContainsKey(k)) l[k] = v;
                        arr.Add(l);
                    }
                    sections.Add(new JsonObject { ["key"] = key, ["title"] = SectionTitles[key], ["lead"] = Leads[key], ["lines"] = arr });
                }
            }
            catch (Exception ex) { firstError ??= ex; }
        }

        if (sections.Count == 0)
        {
            if (firstError is not null && !anyDataMissing) throw firstError;
            root["state"] = anyDataMissing ? "missing" : "empty";
            root["emptyText"] = anyDataMissing ? CardData.MissingText : "אין מה להציג כרגע";
            return new CardResult(CardTemplates.Dashboard, CardTemplates.ToJson(root), refresh);
        }
        root["sections"] = sections;
        return new CardResult(CardTemplates.Dashboard, CardTemplates.ToJson(root), refresh);
    }

    // ---------------------------------------------------------------------------------------------- sections
    private static List<JsonObject> DateLines(CardContext ctx, bool compact, ref DateTimeOffset? refresh)
    {
        var city = CalendarContext.ResolveCity(ctx, null);
        var date = CalendarContext.Today(ctx, city);
        var di = ctx.Calendar.GetDayInfo(date, city);
        var weekday = string.IsNullOrWhiteSpace(di.HebrewDayOfWeek) ? CardFormat.Weekday(di.Date) : di.HebrewDayOfWeek;
        var extra = di.Holidays.FirstOrDefault(h => !string.IsNullOrWhiteSpace(h))
                    ?? (string.IsNullOrWhiteSpace(di.Parasha) ? null : di.Parasha);
        var sub = weekday + " · " + CardFormat.GregorianLong(di.Date) + (extra is null ? "" : " · " + extra);
        var line = compact ? Line(di.HebrewDate, "", weekday, ctx, CardData.CalendarUrl) : Line(di.HebrewDate, sub, "", ctx, CardData.CalendarUrl);
        line["weight"] = "bolder";
        refresh = CalendarContext.Min(refresh, CalendarContext.NextDayBoundary(ctx, city));
        return [line];
    }

    private static List<JsonObject> ZmanLines(CardContext ctx, int count, ref DateTimeOffset? refresh)
    {
        var set = ZmanimBuilder.Compute(ctx, null);
        var res = new List<JsonObject>();
        for (int i = set.NextIndex; i < Math.Min(set.Times.Count, set.NextIndex + count); i++)
        {
            var t = set.Times[i];
            var line = Line(t.Title, "", ZmanimBuilder.FormatTime(t, set.City, ctx.TwelveHour), ctx, CardData.CalendarUrl);
            if (i == set.NextIndex)
            {
                line["color"] = "accent"; line["weight"] = "bolder";
                line["sub"] = (set.Tomorrow ? "מחר · " : "") + CardFormat.Countdown(t.Time!.Value - ctx.Now);
            }
            res.Add(line);
        }
        if (set.NextIndex < set.Times.Count)
        {
            var nt = set.Times[set.NextIndex].Time!.Value;
            refresh = CalendarContext.Min(refresh, nt.AddSeconds(1));
            refresh = CalendarContext.Min(refresh, ZmanimBuilder.NextCountdownTick(ctx.Now, nt));
        }
        refresh = CalendarContext.Min(refresh, CalendarContext.NextDayBoundary(ctx, set.City));
        return res;
    }

    private static List<JsonObject>? ReadingLines(CardContext ctx, int count, ref bool missing)
    {
        if (CardData.MissingReason(ctx, p => p.UserStateDb, SourceNames.UserState) is not null) { missing = true; return null; }
        var snap = ctx.Snapshot!;
        bool Allowed(ReadingEntry e) => ctx.State.IncludePdfs || !e.Book.IsPdf;
        var res = new List<JsonObject>();
        var tab = snap.OpenTabs.FirstOrDefault(t => t.IsCurrent && t.Reading is not null && Allowed(t.Reading));
        var seen = new HashSet<string>();
        if (tab?.Reading is { } r)
        {
            var row = ReadingRows.Row(r, ctx, true, "");
            if (tab.DeepLink is { Length: > 0 } tl) CardData.SetLink(row, tl, ctx);
            row["sub"] = CardFormat.StripTitle(r.Ref, r.Book.Title);
            res.Add(row);
            seen.Add(ReadingRows.Key(r));
        }
        foreach (var e in snap.RecentBooks.Where(Allowed))
        {
            if (res.Count >= count) break;
            if (!seen.Add(ReadingRows.Key(e))) continue;
            res.Add(ReadingRows.Row(e, ctx, true, ""));
        }
        return res;
    }

    private static List<JsonObject>? ProgressLines(CardContext ctx, int count, ref bool missing)
    {
        if (CardData.MissingReason(ctx, p => p.PreferencesHive, SourceNames.Preferences, SourceNames.Progress) is not null)
        { missing = true; return null; }
        var res = new List<JsonObject>();
        foreach (var b in ctx.Snapshot!.Progress.Where(b => ShamorZachorBuilder.Matches(b, "inProgress")).Take(count))
        {
            var line = Line(CardFormat.Clip(b.Title, 50), "", CardFormat.Percent(b.Headline), ctx, b.DeepLink ?? CardData.ShamorZachorUrl);
            line["bar"] = CardData.BarText(b.Headline, CardData.BarSegmentsColumn);
            res.Add(line);
        }
        return res;
    }

    private static List<JsonObject>? BookmarkLines(CardContext ctx, int count, ref bool missing)
    {
        if (CardData.MissingReason(ctx, p => p.UserStateDb, SourceNames.UserState) is not null) { missing = true; return null; }
        var res = new List<JsonObject>();
        foreach (var b in ctx.Snapshot!.Bookmarks.Take(count))
        {
            bool hasLabel = !string.IsNullOrWhiteSpace(b.Label);
            var line = Line(CardFormat.Clip(hasLabel ? b.Label : b.Book.Title, 50),
                            CardFormat.Clip(hasLabel ? b.Book.Title : CardFormat.StripTitle(b.Ref, b.Book.Title), 50), "", ctx, b.DeepLink);
            res.Add(line);
        }
        return res;
    }

    private static JsonObject Line(string text, string sub, string meta, CardContext ctx, string? url)
    {
        var o = new JsonObject { ["text"] = text, ["sub"] = sub, ["meta"] = meta, ["weight"] = "default", ["color"] = "default" };
        CardData.SetLink(o, url, ctx);
        return o;
    }

    // ---------------------------------------------------------------------------------------------- customization
    public CardResult BuildCustomization(CardContext ctx)
    {
        var fields = new JsonArray();
        foreach (var key in WidgetCustomState.DashboardSections)
        {
            int idx = ctx.State.Sections.IndexOf(key);
            fields.Add(Fields.Number("pos_" + key, SectionTitles[key] + " — מיקום (0 = מוסתר)", idx < 0 ? 0 : idx + 1, 0, 5));
        }
        return Fields.Card(Kind, fields, "בחרו מיקום 1-5 לכל חלק; 0 מסתיר אותו. הווידג'ט הבינוני מציג עד 3 חלקים.");
    }
}
