using System.Globalization;
using OtzariaWidget.Calendar.Hebrew;
using OtzariaWidget.Calendar.Tests.TestSupport;
using Xunit.Abstractions;

namespace OtzariaWidget.Calendar.Tests;

/// <summary>QA second-opinion parity: all 129 cities x 8 dates x every relevant zman to the SECOND, registry/city tables, 1924-2100 Hebrew calendar.</summary>
public class QaParityTests(ITestOutputHelper output)
{
    private static readonly CalendarService Svc = new();
    private static readonly IReadOnlyList<string> AllIds = Svc.ZmanimRegistry.Select(z => z.Id).ToList();

    private static DateOnly D(string s) => DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Zmanim that depend on the sun reaching 16.1 / 19.8 / 26 / 10.2-11.5 degrees below the horizon (or are derived from such a dawn).
    /// kosher_dart returns a clamped garbage time when the sun never gets that low (Edinburgh in June: "alos 16.1" at 04:14 while "tzais 16.1" is absent);
    /// Kosher.Zmanim correctly returns null and we show "--".</summary>
    private static bool IsDegreeBased(string id) =>
        id.EndsWith("Degrees", StringComparison.Ordinal) || id.Contains("16point1", StringComparison.Ordinal) ||
        id.StartsWith("misheyakir", StringComparison.Ordinal) || id is "plagAlosToSunset" or "plagAlos16ToGeonim";

    [Fact]
    public void All_cities_match_Otzaria_to_the_second_on_eight_dates()
    {
        var lines = QaGolden.Lines("Golden.qa_zmanim_all_cities.txt.gz");
        Assert.True(lines.Length > 80000, "golden too small: " + lines.Length);
        var failures = new List<string>();
        var garbageCells = 0;
        var nearDegenerate = 0;
        var compared = 0;
        foreach (var byCityDate in lines.Select(l => l.Split('|')).GroupBy(p => (p[0], p[1])))
        {
            var city = Svc.FindCity(byCityDate.Key.Item1) ?? throw new InvalidOperationException(byCityDate.Key.Item1);
            var date = D(byCityDate.Key.Item2);
            var ours = Svc.GetZmanim(date, city, AllIds).Where(z => !z.Id.Contains("KidushLevana", StringComparison.Ordinal)).ToDictionary(z => z.Id);
            var theirIds = byCityDate.Select(p => p[2]).ToHashSet();
            foreach (var p in byCityDate)
            {
                var id = p[2];
                if (!ours.TryGetValue(id, out var z) || z.Time == null)
                {
                    if (IsDegreeBased(id) && city.Latitude > 35 && date.Month is >= 3 and <= 9) { garbageCells++; continue; }
                    failures.Add($"{p[0]} {p[1]} {id}: missing (Otzaria {p[3]})");
                    continue;
                }
                compared++;
                var t = z.Time.Value;
                var mine = $"{t.Hour:00}:{t.Minute:00}:{t.Second:00}";
                if (mine == p[3]) continue;
                // alos/plag 26 degrees at Warsaw on 21 April: the sun barely reaches -26 degrees, acos is ill-conditioned and 1e-9 of noise is 20 s
                var diff = Math.Abs(GoldenData.SecondsOfDay(mine) - GoldenData.SecondsOfDay(p[3]));
                if (diff <= 60 && city.Latitude > 50 && IsDegreeBased(id)) { nearDegenerate++; continue; }
                failures.Add($"{p[0]} {p[1]} {id}: {mine} vs Otzaria {p[3]}");
            }
            foreach (var extra in ours.Values.Where(z => z.Time != null && !theirIds.Contains(z.Id)))
                failures.Add($"{city.Name} {byCityDate.Key.Item2} {extra.Id}: we return a time Otzaria does not ({extra.DisplayText})");
        }
        output.WriteLine($"{compared} cells identical to the second, {garbageCells} high-latitude cells where kosher_dart returns garbage and we return null, {nearDegenerate} ill-conditioned cells");
        Assert.True(failures.Count == 0, $"{failures.Count} mismatches:\n" + string.Join("\n", failures.Take(40)));
        Assert.InRange(garbageCells, 0, 800);
        Assert.InRange(nearDegenerate, 0, 5);
        Assert.True(compared > 79000, $"only {compared} cells compared");
    }

    [Fact]
    public void Registry_matches_Otzaria_field_by_field()
    {
        var gold = QaGolden.Lines("Golden.qa_registry.txt.gz").Select(l => l.Split('|')).ToList();
        Assert.Equal(98, gold.Count);
        var detail = Svc.ZmanimRegistryDetails;
        Assert.Equal(98, detail.Count);
        for (var i = 0; i < gold.Count; i++)
        {
            var g = gold[i];
            var e = detail[i];
            Assert.Equal(g[0], e.Id);
            Assert.Equal(g[1], e.Title);
            Assert.Equal(g[2], e.Subtitle);
            Assert.Equal(g[3], e.Category);
            Assert.Equal(g[4] == "1", e.DefaultEnabled);
            Assert.Equal(g[5] == "1", e.ShowHebrewDate);
            Assert.Equal(g[7].Length == 0 ? null : g[7], e.PairId);
            Assert.Equal(g[8], e.PairLabel);
            Assert.Equal(g[9], e.Description); // first line of Otzaria's explanation (Dart escape \' already resolved)
            Assert.DoesNotContain('\\', e.Description);
            Assert.Equal(g[1] + (g[2].Length == 0 ? "" : " — " + g[2]), Svc.ZmanimRegistry[i].Title);
        }
        // the kidush levana entries are exactly the "show Hebrew date" ones; push-to-tzais = the two "beginning" entries (hard-coded in ZmanCalculator)
        Assert.Equal(["tchilasKidushLevana3", "tchilasKidushLevana7", "sofKidushLevanaMoldos", "sofKidushLevana15"],
            gold.Where(g => g[5] == "1").Select(g => g[0]).ToArray());
        Assert.Equal(["tchilasKidushLevana3", "tchilasKidushLevana7"], gold.Where(g => g[5] == "1" && g[6] == "1").Select(g => g[0]).ToArray());
    }

    [Fact]
    public void City_table_matches_Otzaria_for_all_129_cities()
    {
        var gold = QaGolden.Lines("Golden.qa_cities.txt.gz").Select(l => l.Split('|')).ToList();
        Assert.Equal(129, gold.Count);
        Assert.Equal(129, Svc.Cities.Count);
        foreach (var g in gold)
        {
            var c = Svc.FindCity(g[0]);
            Assert.NotNull(c);
            Assert.Equal(g[1], c!.Country);
            Assert.Equal(double.Parse(g[2], CultureInfo.InvariantCulture), c.Latitude);
            Assert.Equal(double.Parse(g[3], CultureInfo.InvariantCulture), c.Longitude);
            Assert.Equal(double.Parse(g[4], CultureInfo.InvariantCulture), c.Elevation);
            Assert.Equal(g[5], c.TimeZoneId);
            Assert.Equal(g[6] == "1", c.IsInIsrael);
            Assert.Equal(int.Parse(g[7], CultureInfo.InvariantCulture), c.CandleLightingMinutes);
        }
    }

    [Fact]
    public void Hebrew_calendar_1924_to_2100_sampled_matches_Otzaria()
    {
        string[] dows = ["", "ראשון", "שני", "שלישי", "רביעי", "חמישי", "שישי", "שבת"];
        foreach (var israel in new[] { true, false })
        {
            var city = Svc.FindCity(israel ? "ירושלים" : "ניו יורק")!;
            var failures = new List<string>();
            var rows = QaGolden.Lines(israel ? "Golden.qa_hebrew_israel.txt.gz" : "Golden.qa_hebrew_diaspora.txt.gz");
            Assert.True(rows.Length > 5000);
            foreach (var r in rows)
            {
                var p = r.Split('|'); // date|header date|dow|events|upcoming parasha|daf|omer|yom tov
                var date = D(p[0]);
                var info = Svc.GetDayInfo(date, city);
                var jc = new Kosher.Zmanim.HebrewCalendar.JewishCalendar(date) { InIsrael = israel };

                var header = Svc.GetCalendarHeaderDate(date, city);
                if (header != p[1]) failures.Add($"{p[0]} header '{header}' != '{p[1]}'");
                if (dows[jc.DayOfWeek] != p[2]) failures.Add($"{p[0]} dow");
                if (info.HebrewDayOfWeek != (jc.DayOfWeek == 7 ? "שבת קודש" : "יום " + p[2])) failures.Add($"{p[0]} dow name {info.HebrewDayOfWeek}");

                var events = p[3].Length == 0 ? new List<string>() : p[3].Split(';').ToList();
                if (jc.DayOfWeek == 7 && p[4].Length > 0) events.Remove(p[4]); // Otzaria appends the parsha to Shabbat's list; we keep it in DayInfo.Parasha
                var fastOnShabbat = jc.DayOfWeek == 7 && events.Any(e => e is "שבעה עשר בתמוז" or "תשעה באב") && !info.Holidays.Any(e => e is "שבעה עשר בתמוז" or "תשעה באב");
                if (fastOnShabbat) events.RemoveAll(e => e is "שבעה עשר בתמוז" or "תשעה באב"); // documented kosher_dart bug
                if (!info.Holidays.SequenceEqual(events)) failures.Add($"{p[0]} holidays [{string.Join(';', info.Holidays)}] != [{string.Join(';', events)}]");

                if ((info.Parasha ?? "") != p[4]) failures.Add($"{p[0]} parasha '{info.Parasha}' != '{p[4]}'");
                if ((info.DafYomiBavli ?? "") != p[5]) failures.Add($"{p[0]} daf '{info.DafYomiBavli}' != '{p[5]}'");
                if ((info.Omer ?? "") != p[6]) failures.Add($"{p[0]} omer '{info.Omer}' != '{p[6]}'");
                if (info.IsYomTov != (p[7] == "1")) failures.Add($"{p[0]} yom tov");
            }
            Assert.True(failures.Count == 0, $"{(israel ? "Israel" : "diaspora")}: {failures.Count} mismatches:\n" + string.Join("\n", failures.Take(30)));
        }
    }

    /// <summary>First day of each masechta of the 14th Bavli cycle (started 2020-01-05) as Otzaria shows it: two-word names, the Shekalim /
    /// Meilah / Kinnim / Tamid / Midos special cases and no geresh/gershayim in the daf number (formatAmud).</summary>
    [Theory]
    [InlineData("2020-01-05", "ברכות ב")]
    [InlineData("2020-03-08", "שבת ב")]
    [InlineData("2021-03-23", "שקלים ב")]
    [InlineData("2021-10-11", "ראש השנה ב")]
    [InlineData("2022-01-14", "מועד קטן ב")]
    [InlineData("2023-11-04", "בבא קמא ב")]
    [InlineData("2024-03-01", "בבא מציעא ב")]
    [InlineData("2024-06-27", "בבא בתרא ב")]
    [InlineData("2025-06-20", "עבודה זרה ב")]
    [InlineData("2025-09-03", "הוריות ב")]
    [InlineData("2026-11-19", "ערכין ב")]
    [InlineData("2027-02-20", "מעילה ב")]
    [InlineData("2027-03-13", "קינים כג")]
    [InlineData("2027-03-16", "תמיד כו")]
    [InlineData("2027-03-24", "מידות לד")]
    [InlineData("2027-03-28", "נדה ב")]
    [InlineData("2026-11-13", "בכורות נו")]
    public void Bavli_daf_yomi_special_masechtot(string date, string expected)
    {
        var info = Svc.GetDayInfo(D(date), Svc.FindCity("ירושלים")!);
        Assert.Equal(expected, info.DafYomiBavli);
        Assert.DoesNotContain(HebrewNumbers.Geresh, info.DafYomiBavli!);
        Assert.DoesNotContain(HebrewNumbers.Gershayim, info.DafYomiBavli!);
    }

    [Fact]
    public void The_cycle_has_40_distinct_masechta_names_in_a_row()
    {
        var jer = Svc.FindCity("ירושלים")!;
        var seen = new List<string>();
        for (var d = D("2020-01-05"); d < D("2027-06-01"); d = d.AddDays(1))
        {
            var daf = Svc.GetDayInfo(d, jer).DafYomiBavli!;
            var masechta = daf[..daf.LastIndexOf(' ')];
            if (seen.Count == 0 || seen[^1] != masechta) seen.Add(masechta);
        }
        Assert.Equal(40, seen.Count);
        Assert.Equal(40, seen.Distinct().Count());
    }

    /// <summary>Yerushalmi is not shown by Otzaria (no ground truth: kosher_dart 2.0.20 uses an outdated masechta table). Real-world anchors instead:
    /// the second Yerushalmi cycle began Monday 14 Nov 2022 (Berachos 1) and there is no daf on Yom Kippur / Tisha B'Av.</summary>
    [Fact]
    public void Yerushalmi_daf_yomi_anchors()
    {
        var jer = Svc.FindCity("ירושלים")!;
        Assert.Equal("ברכות א", Svc.GetDayInfo(D("2022-11-14"), jer).DafYomiYerushalmi);
        Assert.Equal("ברכות ב", Svc.GetDayInfo(D("2022-11-15"), jer).DafYomiYerushalmi);
        Assert.Null(Svc.GetDayInfo(D("2026-09-21"), jer).DafYomiYerushalmi); // Yom Kippur 5787
        Assert.Null(Svc.GetDayInfo(D("2026-07-23"), jer).DafYomiYerushalmi); // Tisha B'Av 5786
        Assert.NotNull(Svc.GetDayInfo(D("2026-11-13"), jer).DafYomiYerushalmi);
    }
}
