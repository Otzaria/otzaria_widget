using OtzariaWidget.Calendar.Tests.TestSupport;
using OtzariaWidget.Calendar.Zmanim;

namespace OtzariaWidget.Calendar.Tests;

/// <summary>Parity with Otzaria's real zmanim output (Dart ground truth): 7 cities x 21 dates x every relevant registry entry.</summary>
public class ZmanimGoldenTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly CalendarService Svc = new();
    private static readonly IReadOnlyList<string> AllIds = Svc.ZmanimRegistry.Select(z => z.Id).ToList();

    // Exact to the second: ZmanContext/OtzariaZmanimCalendar reproduce kosher_dart's whole-second instants, double-millisecond temporal-hour
    // arithmetic and 6356.9 km earth radius (the earlier +-2 s tolerance hid a 1.4% rate of 1 s differences and ~0.1% displayed-minute flips).
    private const int ToleranceSeconds = 0;

    [Fact]
    public void Every_city_date_matches_Otzaria_item_set_and_displayed_times()
    {
        var failures = new List<string>();
        var checkedCells = 0;
        var displayedMinuteMismatch = 0;
        var polarDays = 0;
        var levanaDstShifts = 0;
        var polarCells = 0;
        foreach (var group in GoldenData.ZmanimLines.GroupBy(l => (l.City, l.Date)))
        {
            var city = Svc.FindCity(group.Key.City) ?? throw new InvalidOperationException(group.Key.City);
            var ours = Svc.GetZmanim(group.Key.Date, city, AllIds).ToDictionary(z => z.Id);
            var theirs = group.ToDictionary(l => l.Id);
            // Known, documented difference: where the sun never gets 16.1/19.8/26 degrees below the horizon (London in summer) KosherJava 3
            // returns null for the degree-based zmanim while kosher_dart returns a clamped number. We show "--" (null) there.
            var noDawn16 = ours.TryGetValue("alos72Degrees", out var dawn16) && dawn16.Time == null;
            var polarSeason = city.Latitude > 50 && group.Key.Date.Month is >= 5 and <= 8;
            if (noDawn16)
            {
                Assert.True(polarSeason, $"unexpected polar-like day {city.Name} {D(group.Key.Date)}");
                polarDays++;
            }

            foreach (var id in ours.Keys.Where(k => !theirs.ContainsKey(k) && ours[k].Time != null))
                failures.Add($"{group.Key.City} {D(group.Key.Date)} {id}: we return a time Otzaria does not show ({ours[id].DisplayText})");

            foreach (var (id, line) in theirs)
            {
                if (!ours.TryGetValue(id, out var z) || z.Time == null)
                {
                    if (polarSeason && (noDawn16 || z?.Time == null)) { polarCells++; continue; }
                    failures.Add($"{group.Key.City} {D(group.Key.Date)} {id}: missing (Otzaria {line.Clock ?? line.Label})");
                    continue;
                }
                checkedCells++;
                if (line.Label != null)
                {
                    if (noDawn16 || z.DisplayText == line.Label) continue;
                    // Otzaria does `DateTime(localMachineZone).add(Duration)`: when molad + 3/7/15 days crosses a DST change of the MACHINE's zone
                    // (this ground truth was produced on an Israel machine) its wall clock is shifted by one hour. We add plain elapsed time (correct,
                    // machine independent), so such cells are not comparable.
                    if (CrossesIsraelDst(group.Key.Date, city, id)) { levanaDstShifts++; continue; }
                    failures.Add($"{group.Key.City} {D(group.Key.Date)} {id}: kidush levana '{z.DisplayText}' != Otzaria '{line.Label}'");
                    continue;
                }
                var dart = GoldenData.SecondsOfDay(line.Clock!);
                var t = z.Time.Value;
                var mine = t.Hour * 3600 + t.Minute * 60 + t.Second;
                // chatzos layla can fall after midnight: compare modulo a day
                var diff = ((mine - dart + 43200) % 86400 + 86400) % 86400 - 43200;
                if (Math.Abs(diff) > ToleranceSeconds)
                {
                    failures.Add($"{group.Key.City} {D(group.Key.Date)} {id}: {t:HH:mm:ss} vs Otzaria {line.Clock} (diff {diff}s)");
                    continue;
                }
                // displayed text: minute + marker must match unless Dart is within tolerance of a minute/30-second boundary
                var dartText = $"{dart / 3600:00}:{dart / 60 % 60:00}{(dart % 60 < 30 ? '.' : ':')}";
                var myText = z.DisplayText!.Trim(ZmanTimeFormatter.LtrIsolateStart, ZmanTimeFormatter.LtrIsolateEnd);
                if (myText != dartText)
                {
                    var secInMinute = dart % 60;
                    var nearBoundary = secInMinute <= ToleranceSeconds || secInMinute >= 60 - ToleranceSeconds
                        || Math.Abs(secInMinute - 30) <= ToleranceSeconds;
                    displayedMinuteMismatch++;
                    if (!nearBoundary) failures.Add($"{group.Key.City} {D(group.Key.Date)} {id}: displayed '{myText}' vs Otzaria '{dartText}' ({line.Clock})");
                }
            }
        }
        Assert.True(checkedCells > 11000, $"only {checkedCells} cells compared");
        Assert.True(failures.Count == 0, $"{failures.Count} failures (of {checkedCells} cells; {displayedMinuteMismatch} boundary-case display diffs):\n" + string.Join("\n", failures.Take(40)));
        output.WriteLine($"parity: {checkedCells} cells compared, {displayedMinuteMismatch} boundary-case display diffs ({displayedMinuteMismatch * 100.0 / checkedCells:F2}%), {levanaDstShifts} kidush levana DST-quirk cells skipped, {polarDays} polar-like days ({polarCells} null cells)");
        Assert.True(levanaDstShifts <= 40, $"{levanaDstShifts} kidush levana labels differ by a DST hour");
        Assert.True(polarDays <= 12, $"{polarDays} polar-like days ({polarCells} cells)");
        // the boundary-case residue stays tiny (the research measured 0.48%)
        Assert.True(displayedMinuteMismatch < checkedCells * 0.02, $"{displayedMinuteMismatch} display diffs out of {checkedCells}");
    }

    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static readonly TimeZoneInfo Jerusalem = TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");

    private static TimeSpan LevanaOffset(string id) => id switch
    {
        "tchilasKidushLevana3" => TimeSpan.FromDays(3),
        "tchilasKidushLevana7" => TimeSpan.FromDays(7),
        "sofKidushLevanaMoldos" => new TimeSpan(14, 18, 22, 1, 666),
        _ => TimeSpan.FromDays(15),
    };

    /// <summary>True when the naive wall-clock span molad .. molad+offset contains an Israel DST change (the Dart machine's zone).</summary>
    private static bool CrossesIsraelDst(DateOnly date, OtzariaWidget.Core.Contracts.CityInfo city, string id)
    {
        var jc = new Kosher.Zmanim.HebrewCalendar.JewishCalendar(date) { InIsrael = city.IsInIsrael };
        var start = KidushLevana.RawNaive(jc, TimeSpan.Zero);
        var end = KidushLevana.RawNaive(jc, LevanaOffset(id));
        return Jerusalem.GetUtcOffset(start) != Jerusalem.GetUtcOffset(end);
    }

    [Fact]
    public void Today_2026_11_13_jerusalem_and_modiin_illit_default_zmanim_match_the_research_table()
    {
        var date = new DateOnly(2026, 11, 13);
        var jer = Svc.GetZmanim(date, Svc.FindCity("ירושלים")!, Svc.DefaultEnabledZmanim).ToDictionary(z => z.Id);
        var mod = Svc.GetZmanim(date, Svc.FindCity("מודיעין עילית")!, Svc.DefaultEnabledZmanim).ToDictionary(z => z.Id);

        // Friday: candle lighting only (no motzei / chametz / fast ends)
        Assert.Contains("candleLighting", jer.Keys);
        Assert.DoesNotContain("motzeiShabbos", jer.Keys);
        Assert.DoesNotContain("achilasChametzGRA", jer.Keys);
        Assert.DoesNotContain("fastEndItimLenient", jer.Keys);

        Assert.Equal("16:05", ZmanTimeFormatter.FormatPlain(jer["candleLighting"].Time!.Value));   // Jerusalem 40 minutes
        Assert.Equal("16:14", ZmanTimeFormatter.FormatPlain(mod["candleLighting"].Time!.Value));   // Modi'in Illit 30 minutes
        Assert.Equal("16:45", ZmanTimeFormatter.FormatPlain(jer["sunset"].Time!.Value));
        Assert.Equal("16:44", ZmanTimeFormatter.FormatPlain(mod["sunset"].Time!.Value));
        Assert.Equal("06:05", ZmanTimeFormatter.FormatPlain(jer["sunrise"].Time!.Value));
        Assert.Equal("08:44", ZmanTimeFormatter.FormatPlain(jer["sofZmanShmaGRA"].Time!.Value));
        Assert.Equal("11:23", ZmanTimeFormatter.FormatPlain(jer["chatzos"].Time!.Value));
        Assert.Equal("23:23", ZmanTimeFormatter.FormatPlain(jer["chatzosLayla"].Time!.Value));
        Assert.Equal("17:57", ZmanTimeFormatter.FormatPlain(jer["rt72Shavos"].Time!.Value));
        Assert.Equal("16:59", ZmanTimeFormatter.FormatPlain(jer["tzeitTikochinsky"].Time!.Value));
        Assert.Equal("17:03", ZmanTimeFormatter.FormatPlain(jer["tzeitItimLeVina"].Time!.Value));
        Assert.Equal(TimeSpan.FromHours(2), jer["sunset"].Time!.Value.Offset);
    }

    [Fact]
    public void Results_come_in_registry_order_and_only_for_requested_ids()
    {
        var ids = new[] { "sunset", "alos72Degrees", "chatzos", "nonexistent", "candleLighting" };
        var r = Svc.GetZmanim(new DateOnly(2026, 11, 13), Svc.FindCity("ירושלים")!, ids);
        Assert.Equal(["alos72Degrees", "chatzos", "sunset", "candleLighting"], r.Select(z => z.Id).ToArray());
        Assert.All(r, z => Assert.NotNull(z.Time));
    }

    [Fact]
    public void Irrelevant_day_dependent_zmanim_are_omitted()
    {
        var jer = Svc.FindCity("ירושלים")!;
        var tuesday = Svc.GetZmanim(new DateOnly(2026, 11, 10), jer, AllIds).Select(z => z.Id).ToHashSet();
        Assert.DoesNotContain("candleLighting", tuesday);
        Assert.DoesNotContain("motzeiShabbos", tuesday);
        Assert.DoesNotContain("motzeiYomTov", tuesday);
        Assert.DoesNotContain("achilasChametzGRA", tuesday);
        Assert.DoesNotContain("fastEndItimLenient", tuesday);
        Assert.Contains("sunset", tuesday);
    }

    [Fact]
    public void Day_dependent_zmanim_appear_exactly_on_their_days()
    {
        var jer = Svc.FindCity("ירושלים")!;
        HashSet<string> Ids(DateOnly d) => Svc.GetZmanim(d, jer, AllIds).Select(z => z.Id).ToHashSet();

        var motzeiShabbat = Ids(new DateOnly(2026, 11, 14));
        Assert.Contains("motzeiShabbos", motzeiShabbat);
        Assert.Contains("motzeiShabbosChazonIsh", motzeiShabbat);
        Assert.DoesNotContain("candleLighting", motzeiShabbat);

        var erevPesach = Ids(new DateOnly(2026, 4, 1));
        Assert.Contains("achilasChametzGRA", erevPesach);
        Assert.Contains("biurChametzMGAShavos", erevPesach);
        Assert.Contains("candleLighting", erevPesach);

        var tammuz17 = Ids(new DateOnly(2026, 7, 2));
        Assert.Contains("fastEndItimLenient", tammuz17);
        Assert.Contains("fastEndTikLenient", tammuz17);

        var yomKippur = Ids(new DateOnly(2026, 9, 21));
        Assert.DoesNotContain("fastEndItimLenient", yomKippur); // Otzaria: taanis but NOT Yom Kippur
    }

    [Fact]
    public void Display_values_use_the_city_offset_and_the_otzaria_marker_format()
    {
        var jer = Svc.FindCity("ירושלים")!;
        var z = Svc.GetZmanim(new DateOnly(2026, 11, 13), jer, ["candleLighting"]).Single();
        Assert.Equal("⁦16:05:⁩", z.DisplayText); // 16:05:32 -> seconds 30-59 give the ':' marker (0-29 would give '.')
    }
}
