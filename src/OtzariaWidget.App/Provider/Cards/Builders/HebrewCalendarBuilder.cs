using System.Text.Json.Nodes;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.Provider.Cards;

/// <summary>Hebrew + Gregorian date, weekday and a prioritized list of highlights (holiday, candle lighting, parasha, omer, daf).</summary>
public sealed class HebrewCalendarBuilder : IWidgetCardBuilder
{
    public string Kind => WidgetKinds.HebrewCalendar;

    public CardResult Build(CardContext ctx)
    {
        var city = CalendarContext.ResolveCity(ctx, null);
        var date = CalendarContext.Today(ctx, city);
        var di = ctx.Calendar.GetDayInfo(date, city);

        var (day, year) = SplitHebrewDate(di.HebrewDate);
        var root = CardData.Root(ctx, "לוח עברי", "", "ok");
        root["headerEnd"] = city.Name;
        CardData.SetLink(root, CardData.CalendarUrl, ctx);
        root["date"] = new JsonObject
        {
            ["hebrew"] = di.HebrewDate,
            ["day"] = day,          // "ג׳ כסלו" - the big text
            ["year"] = year,        // "תשפ״ז"
            ["weekday"] = string.IsNullOrWhiteSpace(di.HebrewDayOfWeek) ? CardFormat.Weekday(di.Date) : di.HebrewDayOfWeek,
            ["gregorian"] = CardFormat.GregorianLong(di.Date),
        };

        var lines = new List<JsonObject>();
        foreach (var h in di.Holidays.Where(h => !string.IsNullOrWhiteSpace(h)))
            lines.Add(Line("", h, emphasize: true));

        string Fmt(DateTimeOffset t) => CardFormat.Time(t, city.TimeZoneId, ctx.TwelveHour);
        if (ctx.State.ShowCandle)
        {
            if (di.CandleLighting is { } cl) lines.Add(Line("הדלקת נרות", Fmt(cl)));
            if (di.Havdalah is { } hv) lines.Add(Line("הבדלה", Fmt(hv)));
        }
        if (!string.IsNullOrWhiteSpace(di.Parasha))
        {
            var p = di.Parasha!.Trim();
            lines.Add(p.StartsWith("פרשת", StringComparison.Ordinal) || p.StartsWith("פרשה", StringComparison.Ordinal)
                ? Line("", p)
                : Line("פרשה", p));
        }
        if (ctx.State.ShowOmer && !string.IsNullOrWhiteSpace(di.Omer)) lines.Add(Line("", di.Omer!));
        if (ctx.State.ShowDaf && !string.IsNullOrWhiteSpace(di.DafYomiBavli)) lines.Add(Line("דף יומי", di.DafYomiBavli!));
        if ((ctx.State.ShowYerushalmi ?? ctx.Settings.ShowYerushalmiDaf) && !string.IsNullOrWhiteSpace(di.DafYomiYerushalmi))
            lines.Add(Line("ירושלמי", di.DafYomiYerushalmi!));

        int cap = ctx.Size switch { CardSize.Small => 1, CardSize.Medium => 3, _ => 6 };
        var arr = new JsonArray();
        foreach (var l in lines.Take(cap)) arr.Add(l);
        root["lines"] = arr;

        return new CardResult(CardTemplates.HebrewCalendar, CardTemplates.ToJson(root), CalendarContext.NextDayBoundary(ctx, city));
    }

    /// <summary>"ג׳ כסלו תשפ״ז" -> ("ג׳ כסלו", "תשפ״ז"); when the last word does not look like a Hebrew year the whole text is the day.</summary>
    internal static (string Day, string Year) SplitHebrewDate(string? hebrew)
    {
        var h = (hebrew ?? "").Trim();
        int i = h.LastIndexOf(' ');
        if (i <= 0) return (h, "");
        var last = h[(i + 1)..];
        bool yearLike = last.Length >= 3 && last[0] is 'ת' or 'ה' && (last.Contains('״') || last.Contains('"'));
        return yearLike ? (h[..i].Trim(), last) : (h, "");
    }

    private static JsonObject Line(string label, string text, bool emphasize = false) => new()
    {
        ["label"] = label,
        ["text"] = text,
        ["weight"] = emphasize ? "bolder" : "default",
        ["color"] = emphasize ? "accent" : "default",
    };

    public CardResult BuildCustomization(CardContext ctx)
    {
        var st = ctx.State;
        var fields = new JsonArray
        {
            Fields.Toggle("showDaf", "הצג דף יומי (בבלי)", st.ShowDaf),
            Fields.Toggle("showYerushalmi", "הצג דף יומי ירושלמי", st.ShowYerushalmi ?? ctx.Settings.ShowYerushalmiDaf),
            Fields.Toggle("showOmer", "הצג ספירת העומר", st.ShowOmer),
            Fields.Toggle("showCandle", "הצג הדלקת נרות והבדלה", st.ShowCandle),
        };
        return Fields.Card(Kind, fields);
    }
}
