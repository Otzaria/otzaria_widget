// Agent D - formatting helpers. The machine culture is he-IL with the Hebrew calendar, so NOTHING here uses the
// current culture: numbers use InvariantCulture and Gregorian dates are formatted through an explicit GregorianCalendar.
using System.Globalization;

namespace OtzariaWidget.App.Provider.Cards;

public static class CardFormat
{
    private static readonly GregorianCalendar Greg = new();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static readonly string[] GregorianMonths =
        ["ינואר", "פברואר", "מרץ", "אפריל", "מאי", "יוני", "יולי", "אוגוסט", "ספטמבר", "אוקטובר", "נובמבר", "דצמבר"];

    public static readonly string[] ShortMonths =
        ["ינו׳", "פבר׳", "מרץ", "אפר׳", "מאי", "יוני", "יולי", "אוג׳", "ספט׳", "אוק׳", "נוב׳", "דצמ׳"];

    /// <summary>Indexed by (int)DayOfWeek: Sunday = 0.</summary>
    public static readonly string[] HebrewWeekdays =
        ["יום ראשון", "יום שני", "יום שלישי", "יום רביעי", "יום חמישי", "יום שישי", "שבת"];

    public static readonly string[] ShortWeekdays = ["א׳", "ב׳", "ג׳", "ד׳", "ה׳", "ו׳", "שבת"];

    // ---------------------------------------------------------------------------------------------- time of day
    /// <summary>"HH:mm" (24h) or "h:mm AM/PM" (12h). Invariant digits, no culture leakage.</summary>
    public static string Time(DateTime local, bool twelveHour)
    {
        if (!twelveHour) return local.Hour.ToString("00", Inv) + ":" + local.Minute.ToString("00", Inv);
        int h = local.Hour % 12; if (h == 0) h = 12;
        return h.ToString(Inv) + ":" + local.Minute.ToString("00", Inv) + (local.Hour < 12 ? " AM" : " PM");
    }

    /// <summary>Wall-clock time in the given IANA/Windows time zone; falls back to the instant's own offset.</summary>
    public static DateTime ToZone(DateTimeOffset t, string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            try { return TimeZoneInfo.ConvertTime(t, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)).DateTime; }
            catch (Exception) { /* unknown zone id on this machine: fall through */ }
        }
        return t.DateTime;
    }

    public static string Time(DateTimeOffset t, string? timeZoneId, bool twelveHour) => Time(ToZone(t, timeZoneId), twelveHour);

    // ---------------------------------------------------------------------------------------------- dates
    /// <summary>"13 בנובמבר 2026"</summary>
    public static string GregorianLong(DateOnly d)
    {
        var dt = d.ToDateTime(TimeOnly.MinValue);
        return Greg.GetDayOfMonth(dt).ToString(Inv) + " ב" + GregorianMonths[Greg.GetMonth(dt) - 1] + " " + Greg.GetYear(dt).ToString(Inv);
    }

    /// <summary>"13 נוב׳"</summary>
    public static string GregorianShort(DateOnly d)
    {
        var dt = d.ToDateTime(TimeOnly.MinValue);
        return Greg.GetDayOfMonth(dt).ToString(Inv) + " " + ShortMonths[Greg.GetMonth(dt) - 1];
    }

    public static string Weekday(DateOnly d) => HebrewWeekdays[(int)Greg.GetDayOfWeek(d.ToDateTime(TimeOnly.MinValue))];

    /// <summary>"היום", "אתמול", "לפני 3 ימים", else "13 נוב׳" - used for history timestamps.</summary>
    public static string RelativeDay(DateTimeOffset? when, DateTimeOffset now)
    {
        if (when is null) return "";
        var w = when.Value.ToLocalTime();
        var n = now.ToLocalTime();
        int days = (int)(DateOnly.FromDateTime(n.DateTime).DayNumber - DateOnly.FromDateTime(w.DateTime).DayNumber);
        if (days < 0) return "";
        if (days == 0) return "היום";
        if (days == 1) return "אתמול";
        if (days < 7) return "לפני " + days.ToString(Inv) + " ימים";
        return GregorianShort(DateOnly.FromDateTime(w.DateTime));
    }

    // ---------------------------------------------------------------------------------------------- numbers / text
    /// <summary>Half away from zero, like Dart's num.round() (the .NET default is banker's rounding).</summary>
    public static string Percent(double ratio) =>
        Math.Round(Math.Clamp(ratio, 0, 1) * 100, MidpointRounding.AwayFromZero).ToString("0", Inv) + "%";

    public static string Number(int n) => n.ToString(Inv);

    /// <summary>Truncates long titles on a word boundary when possible (the renderer also uses maxLines).</summary>
    public static string Clip(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Trim();
        if (s.Length <= max) return s;
        int cut = s.LastIndexOf(' ', max - 1);
        if (cut < max / 2) cut = max - 1;
        return s[..cut].TrimEnd(' ', ',', '-', '.') + "…";
    }

    // ---------------------------------------------------------------------------------------------- countdown / refs / bars
    /// <summary>"בעוד 2:15 שעות" / "בעוד 42 דקות" / "בעוד דקה" / "עוד רגע". Hours form is rounded to 5 minutes (the card refreshes on that grid).</summary>
    public static string Countdown(TimeSpan d)
    {
        double min = Math.Ceiling(d.TotalMinutes);
        if (min <= 0) return "עוד רגע";
        if (min >= 60)
        {
            int m5 = (int)(Math.Round(min / 5, MidpointRounding.AwayFromZero) * 5);
            return "בעוד " + (m5 / 60).ToString(Inv) + ":" + (m5 % 60).ToString("00", Inv) + " שעות";
        }
        if (min == 1) return "בעוד דקה";
        return "בעוד " + ((int)min).ToString(Inv) + " דקות";
    }

    /// <summary>
    /// Otzaria's location text starts with the book title ("ברכות, ב." / "מנחות דף עו: — עמוד 150"); drop that prefix so the
    /// subtitle only shows where in the book you are. Falls back to the full text when nothing would be left.
    /// </summary>
    public static string StripTitle(string? reference, string? title)
    {
        var r = (reference ?? "").Trim();
        var t = (title ?? "").Trim();
        if (r.Length == 0 || t.Length == 0 || !r.StartsWith(t, StringComparison.Ordinal)) return r;
        var rest = r[t.Length..].TrimStart(' ', ',', ':', '-', '–', '—').TrimEnd();
        return rest.Length == 0 ? r : rest;
    }

    /// <summary>Removes bidi isolates/marks (Otzaria wraps its time labels in LTR isolates).</summary>
    public static string StripBidi(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
            if (!(c is >= '⁦' and <= '⁩' or '‎' or '‏' or '‪' or '‫' or '‬' or '‭' or '‮' or '؜')) sb.Append(c);
        return sb.ToString();
    }

    public const string BarGlyph = "▬";        // BLACK RECTANGLE - filled segment
    public const string BarTrackGlyph = "▭";   // WHITE RECTANGLE - empty segment (outline reads as a light track in both themes)

    /// <summary>Progress bar as two glyph strings (filled / rest). A started bar always shows at least one filled segment, an unfinished one at least one empty.</summary>
    public static (string Fill, string Remaining) BarGlyphs(double ratio, int segments)
    {
        segments = Math.Max(2, segments);
        double r = Math.Clamp(double.IsNaN(ratio) ? 0 : ratio, 0, 1);
        int fill = r <= 0 ? 0 : r >= 1 ? segments : Math.Clamp((int)Math.Round(r * segments, MidpointRounding.AwayFromZero), 1, segments - 1);
        return (new string(BarGlyph[0], fill), new string(BarTrackGlyph[0], segments - fill));
    }
}
