using System.Globalization;
using OtzariaWidget.Calendar.Tests.TestSupport;

namespace OtzariaWidget.Calendar.Tests;

/// <summary>
/// Day-by-day parity with Otzaria for 2023-09-01 .. 2031-08-31 (2,922 days), Israel (Jerusalem) and diaspora (New York): Hebrew date, holiday labels
/// (the CalendarDayCell algorithm), upcoming parsha, daf yomi, omer text, Yom Tov flag and which day-dependent zmanim are shown.
/// Ground truth = Otzaria's real Dart code (kosher_dart 2.0.20).
/// </summary>
public class HebrewGoldenTests
{
    private static readonly CalendarService Svc = new();

    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Documented kosher_dart 2.0.20 bug: it labels BOTH Saturday 17 Tammuz / 9 Av and the postponed Sunday as the fast
    /// (docs/research/calendar-and-zmanim.md 7.2). We (correctly, like KosherJava 3) label only the postponed Sunday.</summary>
    private static bool IsKosherDartPostponedFastBug(GoldenData.HebrewLine l, IReadOnlyList<string> ours)
    {
        if (l.DayOfWeek != 7) return false;
        var ymd = l.JewishYmd.Split('-').Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray(); // year-month(Nissan=1)-day
        var fastOnShabbat = (ymd[1] == 4 && ymd[2] == 17) || (ymd[1] == 5 && ymd[2] == 9);
        return fastOnShabbat && l.Events.Any(e => e is "שבעה עשר בתמוז" or "תשעה באב") && !ours.Any(e => e is "שבעה עשר בתמוז" or "תשעה באב");
    }

    [Theory]
    [InlineData(true, "ירושלים")]
    [InlineData(false, "ניו יורק")]
    public void Every_day_matches_Otzaria(bool israel, string cityName)
    {
        var city = Svc.FindCity(cityName)!;
        Assert.Equal(israel, city.IsInIsrael);
        var failures = new List<string>();
        var lines = GoldenData.Hebrew(israel);
        var shabbatFastBugs = 0;
        foreach (var l in lines)
        {
            var info = Svc.GetDayInfo(l.Date, city);

            // chip spelling: Otzaria's library chip says חשוון/סיוון, its calendar header says חשון/סיון -> we use the header spelling
            var expectedDate = l.Chip.Replace("חשוון", "חשון").Replace("סיוון", "סיון");
            if (info.HebrewDate != expectedDate) failures.Add($"{D(l.Date)} date '{info.HebrewDate}' != '{expectedDate}'");

            // Otzaria appends the parsha to the Shabbat's label list; the widget keeps it in DayInfo.Parasha
            var expectedHolidays = l.DayOfWeek == 7 ? l.Events.Where(e => e != l.UpcomingParasha).ToList() : l.Events.ToList();
            var dartFastBug = IsKosherDartPostponedFastBug(l, info.Holidays);
            if (dartFastBug) { shabbatFastBugs++; expectedHolidays.RemoveAll(e => e is "שבעה עשר בתמוז" or "תשעה באב"); }
            if (!info.Holidays.SequenceEqual(expectedHolidays)) failures.Add($"{D(l.Date)} holidays [{string.Join(";", info.Holidays)}] != [{string.Join(";", expectedHolidays)}]");

            var expectedParasha = l.UpcomingParasha.Length == 0 ? null : l.UpcomingParasha;
            if (info.Parasha != expectedParasha) failures.Add($"{D(l.Date)} parasha '{info.Parasha}' != '{expectedParasha}'");

            var expectedDaf = l.Daf.Length == 0 ? null : l.Daf;
            if (info.DafYomiBavli != expectedDaf) failures.Add($"{D(l.Date)} daf '{info.DafYomiBavli}' != '{expectedDaf}'");

            var expectedOmer = l.Omer.Length == 0 ? null : l.Omer;
            if (info.Omer != expectedOmer) failures.Add($"{D(l.Date)} omer '{info.Omer}' != '{expectedOmer}'");

            if (info.IsYomTov != l.AssurBemelacha) failures.Add($"{D(l.Date)} isYomTov {info.IsYomTov} != {l.AssurBemelacha}");
            if (info.IsShabbat != (l.DayOfWeek == 7)) failures.Add($"{D(l.Date)} isShabbat");
            if (info.GregorianDate != l.Date.ToString("dd'/'MM'/'yyyy", CultureInfo.InvariantCulture)) failures.Add($"{D(l.Date)} gregorian {info.GregorianDate}");

            // which of Otzaria's day-dependent zmanim are relevant
            var ids = new[] { "candleLighting", "motzeiShabbos", "motzeiYomTov", "achilasChametzGRA", "fastEndTikLenient" };
            var shown = Svc.GetZmanim(l.Date, city, ids).Select(z => z.Id).ToHashSet();
            foreach (var id in ids)
            {
                if (dartFastBug && id == "fastEndTikLenient") { Assert.DoesNotContain(id, shown); continue; } // Otzaria wrongly offers fast-end times on that Shabbat
                var expected = l.RelevantIds.Contains(id);
                if (shown.Contains(id) != expected) failures.Add($"{D(l.Date)} relevance of {id}: ours {shown.Contains(id)} != Otzaria {expected}");
            }

            // DayInfo candle lighting / havdalah follow the same predicates
            if ((info.CandleLighting != null) != l.RelevantIds.Contains("candleLighting")) failures.Add($"{D(l.Date)} DayInfo.CandleLighting presence");
            if ((info.Havdalah != null) != (l.RelevantIds.Contains("motzeiShabbos") || l.RelevantIds.Contains("motzeiYomTov"))) failures.Add($"{D(l.Date)} DayInfo.Havdalah presence");
        }
        Assert.Equal(2922, lines.Count);
        Assert.True(failures.Count == 0, $"{failures.Count} mismatches:\n" + string.Join("\n", failures.Take(40)));
        Assert.True(shabbatFastBugs <= 2, $"{shabbatFastBugs} Shabbat fast days"); // 17 Tammuz 5789 (2029-06-30) is the only one in this range
    }
}
