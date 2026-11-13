using System.Text.Json.Nodes;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.Provider.Cards;

/// <summary>Daily zmanim: the next one highlighted (with a countdown), past ones dimmed. Small = just the next zman.</summary>
public sealed class ZmanimBuilder : IWidgetCardBuilder
{
    public string Kind => WidgetKinds.Zmanim;

    /// <summary>The "main" zmanim preset (a subset of Otzaria's registry ids; ids unknown to the registry are skipped).</summary>
    public static readonly IReadOnlyList<string> CoreZmanimIds =
        ["alos72Degrees", "sunrise", "sofZmanShmaGRA", "sofZmanTfilaGRA", "chatzos", "minchaGedolaGreater30", "sunset", "tzeitTikochinsky"];

    public CardResult Build(CardContext ctx)
    {
        var set = Compute(ctx, ctx.State.City);
        var city = set.City;
        var times = set.Times;
        var date = set.Date;
        bool tomorrow = set.Tomorrow;

        string hebrewDate = "";
        try { hebrewDate = ctx.Calendar.GetDayInfo(date, city).HebrewDate; }
        catch (Exception) { /* header falls back to the city only */ }
        string headerEnd = string.Join(" · ", new[] { city.Name, tomorrow ? "מחר" : "", hebrewDate }.Where(x => x.Length > 0));

        var root = CardData.Root(ctx, "זמני היום", headerEnd, "ok");
        root["headerEnd"] = headerEnd;
        CardData.SetLink(root, CardData.CalendarUrl, ctx);
        root["rows"] = new JsonArray();
        root["after"] = new JsonArray();
        root["next"] = null;

        if (times.Count == 0)
        {
            root["state"] = "empty";
            root["emptyText"] = "אין זמנים להצגה";
            return new CardResult(CardTemplates.Zmanim, CardTemplates.ToJson(root), CalendarContext.NextDayBoundary(ctx, city));
        }

        int nextIdx = set.NextIndex;
        string Fmt(ZmanTime t) => FormatTime(t, city, ctx.TwelveHour);

        if (nextIdx < times.Count)
        {
            var n = times[nextIdx];
            root["next"] = new JsonObject
            {
                ["label"] = tomorrow ? "הזמן הבא · מחר" : "הזמן הבא",
                ["name"] = n.Title,
                ["time"] = Fmt(n),
                ["countdown"] = CardFormat.Countdown(n.Time!.Value - ctx.Now),
            };
            // medium: the next three after the highlighted one
            var after = new JsonArray();
            for (int i = nextIdx + 1; i < Math.Min(times.Count, nextIdx + 4); i++)
                after.Add(new JsonObject { ["name"] = times[i].Title, ["time"] = Fmt(times[i]) });
            root["after"] = after;
        }

        // large: a window around the next zman (up to two past ones, dimmed), then zmanim of other days / not occurring today.
        int count = ctx.Size switch { CardSize.Small => 1, CardSize.Medium => 3, _ => 9 };
        int before = ctx.Size == CardSize.Large ? Math.Min(2, nextIdx) : 0;
        int start = Math.Max(0, nextIdx - before);
        if (start + count > times.Count) start = Math.Max(0, times.Count - count);

        var rows = new JsonArray();
        for (int i = start; i < Math.Min(times.Count, start + count); i++)
        {
            var t = times[i];
            bool isNext = i == nextIdx;
            rows.Add(Row(t.Title, Fmt(t), isNext ? CardFormat.Countdown(t.Time!.Value - ctx.Now) : "", isNext, i < nextIdx));
        }
        if (ctx.Size == CardSize.Large)
        {
            foreach (var o in set.OtherDays)
            {
                if (rows.Count >= count) break;
                rows.Add(Row(o.Title, FormatTime(o, city, ctx.TwelveHour), OtherDayLabel(o), false, false));
            }
            foreach (var u in set.Untimed)
            {
                if (rows.Count >= count) break;
                rows.Add(Row(u.Title, "--", "", false, true));
            }
        }
        root["rows"] = rows;

        // Refresh when the next zman changes (just after it passes), when the countdown text changes, or at the day boundary.
        DateTimeOffset? refresh = null;
        if (nextIdx < times.Count)
        {
            refresh = times[nextIdx].Time!.Value.AddSeconds(1);
            refresh = CalendarContext.Min(refresh, NextCountdownTick(ctx.Now, times[nextIdx].Time!.Value));
        }
        refresh = CalendarContext.Min(refresh, CalendarContext.NextDayBoundary(ctx, city));
        return new CardResult(CardTemplates.Zmanim, CardTemplates.ToJson(root), refresh);
    }

    private static JsonObject Row(string name, string time, string sub, bool isNext, bool past) => new()
    {
        ["name"] = name,
        ["time"] = time,
        ["sub"] = sub,
        ["weight"] = isNext ? "bolder" : "default",
        ["color"] = isNext ? "accent" : "default",
        ["past"] = past,
        ["isNext"] = isNext,
    };

    /// <summary>
    /// Time of day of a zman: our formatter on <see cref="ZmanTime.Time"/> (respects 12h/24h and the city's zone, minutes truncated like
    /// Otzaria). Otzaria's own <see cref="ZmanTime.DisplayText"/> carries seconds markers and LTR isolates that read badly in a widget.
    /// "--" when the zman does not occur.
    /// </summary>
    internal static string FormatTime(ZmanTime t, CityInfo city, bool twelveHour) =>
        t.Time is { } v ? CardFormat.Time(v, city.TimeZoneId, twelveHour) : "--";

    /// <summary>"ליל שישי ג׳ לחודש" from Otzaria's kidush-levana label (the label minus its time).</summary>
    internal static string OtherDayLabel(ZmanTime t)
    {
        var text = CardFormat.StripBidi(t.DisplayText).Trim();
        int i = text.LastIndexOf(' ');
        if (i > 0 && text[(i + 1)..].Any(char.IsDigit)) text = text[..i].Trim();
        return text;
    }

    /// <summary>
    /// Countdown granularity: every minute during the last hour, every 5 minutes before that (the text rounds to 5 minutes),
    /// aligned to wall-clock minutes. Never later than the zman itself.
    /// </summary>
    internal static DateTimeOffset? NextCountdownTick(DateTimeOffset now, DateTimeOffset target)
    {
        var remaining = target - now;
        if (remaining <= TimeSpan.Zero) return null;
        var floor = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Offset);
        var tick = remaining < TimeSpan.FromMinutes(61)
            ? floor.AddMinutes(1)
            : floor.AddMinutes(5 - now.Minute % 5);
        return tick < target ? tick : target;
    }

    /// <summary>The zmanim to show: today's, or tomorrow's when today's are all over. Sorted by time; null times kept apart.</summary>
    internal sealed record ZmanSet(CityInfo City, DateOnly Date, bool Tomorrow, List<ZmanTime> Times, int NextIndex)
    {
        /// <summary>Zmanim whose instant falls on another civil date (kidush levana): shown after the day's list, never "next".</summary>
        public List<ZmanTime> OtherDays { get; init; } = [];
        /// <summary>Selected zmanim that do not occur on this date (Time == null).</summary>
        public List<ZmanTime> Untimed { get; init; } = [];
    }

    internal static ZmanSet Compute(CardContext ctx, string? widgetCity)
    {
        var city = CalendarContext.ResolveCity(ctx, widgetCity);
        var ids = CalendarContext.ZmanimIds(ctx);
        var date = CalendarContext.Today(ctx, city);
        var (times, other, untimed) = Load(ctx, date, city, ids);
        if (times.Count > 0 && times.All(t => t.Time!.Value <= ctx.Now))
        {
            var next = Load(ctx, date.AddDays(1), city, ids);
            if (next.Times.Count > 0) { (times, other, untimed) = next; date = date.AddDays(1); }
        }
        var civilToday = DateOnly.FromDateTime(CardFormat.ToZone(ctx.Now, city.TimeZoneId));
        bool tomorrow = date > civilToday;
        int nextIdx = times.FindIndex(t => t.Time!.Value >= ctx.Now);
        if (nextIdx < 0) nextIdx = times.Count;
        return new ZmanSet(city, date, tomorrow, times, nextIdx) { OtherDays = other, Untimed = untimed };
    }

    private static (List<ZmanTime> Times, List<ZmanTime> Other, List<ZmanTime> Untimed) Load(
        CardContext ctx, DateOnly date, CityInfo city, IReadOnlyList<string> ids)
    {
        var all = ctx.Calendar.GetZmanim(date, city, ids);
        var times = new List<ZmanTime>();
        var other = new List<ZmanTime>();
        var untimed = new List<ZmanTime>();
        foreach (var z in all)
        {
            if (z.Time is not { } t) { untimed.Add(z); continue; }
            if (DateOnly.FromDateTime(CardFormat.ToZone(t, city.TimeZoneId)) != date) other.Add(z); else times.Add(z);
        }
        return (times.OrderBy(t => t.Time!.Value).ToList(), other.OrderBy(t => t.Time!.Value).ToList(), untimed);
    }

    // ------------------------------------------------------------------------------------------------ customization
    /// <summary>
    /// A 98-item checklist is unusable on the board, so the card offers presets only. With the current custom-state fields the preset is
    /// expressed as two inputs: <c>zmanimMode</c> (follow Otzaria | custom) and <c>zmanim</c> (a single choice whose value is the comma
    /// separated id list of the preset; <see cref="WidgetCustomState.ApplyInputs"/> already accepts a CSV string). The full checklist lives in
    /// the settings app (global override, honoured by "follow").
    /// </summary>
    public CardResult BuildCustomization(CardContext ctx)
    {
        var st = ctx.State;
        var cal = ctx.Calendar;
        var cityChoices = new List<(string, string)> { ("לפי הגדרות אוצריא / הגדרות כלליות", "") };
        cityChoices.AddRange(cal.Cities.Select(c => (c.Name, c.Name)));

        var known = new HashSet<string>(cal.ZmanimRegistry.Select(z => z.Id), StringComparer.Ordinal);
        var core = CoreZmanimIds.Where(known.Contains).ToList();
        var defaults = cal.DefaultEnabledZmanim.Where(known.Contains).ToList();
        var presets = new List<(string Title, string Value)>();
        if (core.Count > 0) presets.Add(($"עיקריים · {CardFormat.Number(core.Count)} זמנים", string.Join(",", core)));
        if (defaults.Count > 0) presets.Add(($"ברירת המחדל של אוצריא · {CardFormat.Number(defaults.Count)} זמנים", string.Join(",", defaults)));

        string selected = presets.Count > 0 ? presets[0].Value : "";
        if (st.ZmanimMode == "custom" && st.ZmanimIds.Count > 0)
        {
            var csv = string.Join(",", st.ZmanimIds);
            if (presets.All(p => p.Value != csv)) presets.Add(($"הרשימה שלי · {CardFormat.Number(st.ZmanimIds.Count)} זמנים", csv));
            selected = csv;
        }

        var fields = new JsonArray
        {
            Fields.Choice("city", "עיר", st.City, cityChoices),
            Fields.Choice("zmanimMode", "קבוצת זמנים", st.ZmanimMode,
            [
                ("כמו באוצריא", "follow"),
                ("מותאם אישית", "custom"),
            ]),
        };
        if (presets.Count > 0)
            fields.Add(Fields.Choice("zmanim", "במצב מותאם אישית — הזמנים להצגה", selected, presets));
        var card = Fields.Card(Kind, fields,
            "רשימה מלאה של כל הזמנים לבחירה — בהגדרות האפליקציה (חל על כל הווידג'טים)", settingsButton: true);
        return card;
    }
}
