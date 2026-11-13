using Kosher.Zmanim.HebrewCalendar;

namespace OtzariaWidget.Calendar.Hebrew;

/// <summary>
/// Otzaria's holiday / special-day labels: a port of <c>CalendarDayCell._calcJewishEvents</c>
/// (calendar_day_cell.dart:316-380) on top of Kosher.Zmanim's formatter, with the string remaps needed to
/// reproduce kosher_dart 2.0.20 (docs/research/calendar-and-zmanim.md §7.2/§7.3):
/// 'שושן פורים' → 'פורים שושן', 'שושן פורים קטן' → 'פורים שושן קטן', 'אסרו חג' dropped, parsha 'נצבים' → 'ניצבים'.
/// </summary>
public static class HolidayLabels
{
    private static readonly string[] Letters = ["", "א", "ב", "ג", "ד", "ה", "ו", "ז", "ח"];

    private static string Letter(int n) => n > 0 && n < Letters.Length ? Letters[n] : "";

    private static HebrewDateFormatter NewFormatter() => new() { IsHebrewFormat = true };

    /// <summary>Holiday labels of the day, Otzaria order, de-duplicated. The weekly parsha is NOT included
    /// (Otzaria appends it on Shabbat; the widget shows it separately via <see cref="Parasha"/>).</summary>
    public static IReadOnlyList<string> Calc(JewishCalendar jc)
    {
        var hdf = NewFormatter();
        var l = new List<string>();

        var yomTov = hdf.FormatYomTov(jc);
        if (!string.IsNullOrEmpty(yomTov))
        {
            foreach (var part in yomTov.Split(','))
            {
                var label = RemapYomTov(part.Trim());
                if (label.Length > 0) l.Add(label);
            }
        }

        if (jc.IsRoshChodesh() && !l.Contains("ראש חודש")) l.Add("ר\"ח");

        var index = jc.GetYomTovIndex();

        if (index == JewishCalendar.CholHamoedSuccos || index == JewishCalendar.CholHamoedPesach)
        {
            l.RemoveAll(e => e.Contains("חול המועד"));
            var dayOfCholHamoed = jc.JewishDayOfMonth - (jc.InIsrael ? 15 : 16);
            var name = index == JewishCalendar.CholHamoedPesach ? "פסח" : "סוכות";
            l.Add($"{Letter(dayOfCholHamoed)} דחוה\"מ {name}");
        }

        if (index == JewishCalendar.Pesach && jc.JewishDayOfMonth == 21)
        {
            l.RemoveAll(e => e == "פסח");
            l.Add("שביעי של פסח");
        }

        if (index == JewishCalendar.Chanukah)
        {
            l.RemoveAll(e => e.Contains("חנוכה"));
            var d = jc.GetDayOfChanukah();
            if (d != -1) l.Add($"נר {Letter(d)} דחנוכה");
        }

        if (index == JewishCalendar.HoshanaRabba)
            l.Add((jc.InIsrael ? "ו" : "ה") + "' דחוה\"מ");

        if (jc.JewishMonth == 7)
        {
            if (jc.JewishDayOfMonth == 22)
            {
                if (!l.Contains("שמיני עצרת")) l.Add("שמיני עצרת");
                if (jc.InIsrael && !l.Contains("שמחת תורה")) l.Add("שמחת תורה");
            }
            if (jc.JewishDayOfMonth == 23 && !jc.InIsrael && !l.Contains("שמחת תורה")) l.Add("שמחת תורה");
        }

        return l.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Kosher.Zmanim → kosher_dart spelling of a single formatYomTov label; "" = drop it.</summary>
    internal static string RemapYomTov(string label) => label switch
    {
        "אסרו חג" => "",
        "שושן פורים" => "פורים שושן",
        "שושן פורים קטן" => "פורים שושן קטן",
        _ => label,
    };

    /// <summary>Parsha of the given Shabbat (the date's own day must be Shabbat), "" when that Shabbat has none
    /// (it is a Yom Tov / Chol HaMoed). Double parshiyot are joined with a space exactly as Otzaria shows them ("ויקהל פקודי").</summary>
    public static string ParashaOfShabbat(JewishCalendar shabbat)
    {
        var s = NewFormatter().FormatParshah(shabbat);
        // kosher_dart spells it ניצבים; KosherJava 3 says נצבים (also in the combined "נצבים וילך").
        return string.IsNullOrEmpty(s) ? "" : s.Replace("נצבים", "ניצבים", StringComparison.Ordinal);
    }

    /// <summary>The upcoming parsha as Otzaria's "upcoming parsha" helpers compute it (tikkun_korim_bloc.dart:25-35):
    /// the Shabbat on or after the date; null when that Shabbat has no regular parsha.</summary>
    public static string? Parasha(JewishCalendar jc)
    {
        var daysUntilShabbat = jc.DayOfWeek == 7 ? 0 : 7 - jc.DayOfWeek;
        var shabbat = daysUntilShabbat == 0
            ? jc
            : new JewishCalendar(jc.LocalDate.AddDays(daysUntilShabbat)) { InIsrael = jc.InIsrael };
        var p = ParashaOfShabbat(shabbat);
        return p.Length == 0 ? null : p;
    }
}
