using System.Diagnostics;
using System.Globalization;
using System.Text;
using OtzariaWidget.Calendar.Cities;
using OtzariaWidget.Calendar.Tests.TestSupport;
using OtzariaWidget.Core.Contracts;
using Xunit.Abstractions;

namespace OtzariaWidget.Calendar.Tests;

/// <summary>QA: culture independence, thread safety, performance, fuzzing, time zone data and day-transition boundaries.</summary>
public class QaRobustnessTests(ITestOutputHelper output)
{
    private static readonly CalendarService Svc = new();
    private static readonly IReadOnlyList<string> AllIds = Svc.ZmanimRegistry.Select(z => z.Id).ToList();
    private static readonly CityInfo Jerusalem = Svc.FindCity("ירושלים")!;

    private static DateOnly D(string s) => DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ── culture independence ──────────────────────────────────────────────────────────────────────────

    private static readonly (string City, string Date)[] SnapshotInputs =
    [
        ("ירושלים", "2026-11-13"), ("ירושלים", "2026-11-14"), ("ניו יורק", "2027-04-21"), ("לונדון", "2026-10-25"), ("לוס אנג'לס", "2027-10-11"),
        ("אדמונטון", "2026-12-21"), ("סידני", "2027-03-13"), ("ירושלים", "2028-03-12"), ("ירושלים", "2027-05-09"), ("פריז", "2026-12-04"),
    ];

    /// <summary>Everything the service can produce for a few tricky (city, date) pairs, serialized with explicit invariant formatting.</summary>
    private static string Inv(long? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "";

    private static string Snapshot()
    {
        var sb = new StringBuilder();
        foreach (var (cityName, date) in SnapshotInputs)
        {
            var city = Svc.FindCity(cityName) ?? Svc.Cities[0];
            var d = D(date);
            var i = Svc.GetDayInfo(d, city);
            sb.Append(i.Date.DayNumber.ToString(CultureInfo.InvariantCulture)).Append('|').Append(i.HebrewDate).Append('|').Append(i.HebrewDayOfWeek).Append('|').Append(i.GregorianDate).Append('|')
              .Append(i.Parasha).Append('|').Append(string.Join(';', i.Holidays)).Append('|').Append(i.DafYomiBavli).Append('|').Append(i.DafYomiYerushalmi).Append('|')
              .Append(i.Omer).Append('|').Append(Inv(i.CandleLighting?.UtcTicks)).Append('|').Append(Inv(i.Havdalah?.UtcTicks)).Append('|').Append(i.IsShabbat).Append(i.IsYomTov).Append('\n');
            foreach (var z in Svc.GetZmanim(d, city, AllIds))
                sb.Append(z.Id).Append('=').Append(Inv(z.Time?.UtcTicks)).Append('/').Append(Inv(z.Time?.Offset.Ticks)).Append('/').Append(z.DisplayText).Append('\n');
            sb.Append(Svc.GetEffectiveDate(new DateTimeOffset(2026, 11, 13, 17, 30, 0, TimeSpan.FromHours(2)), city, "sunset").DayNumber.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(Svc.GetCalendarHeaderDate(d, city)).Append('\n');
        }
        return sb.ToString();
    }

    private static CultureInfo HebrewIsraelWithHebrewCalendar()
    {
        // the dev machine's regional setting: he-IL, calendar = Hebrew calendar (so DateTime.ToString() prints Hebrew dates)
        var ci = new CultureInfo("he-IL");
        ci.DateTimeFormat.Calendar = new HebrewCalendar();
        return ci;
    }

    [Fact]
    public void Outputs_are_identical_under_he_IL_with_Hebrew_calendar_en_US_invariant_and_other_calendars()
    {
        var baseline = RunUnder(CultureInfo.InvariantCulture);
        Assert.Contains("ג׳ כסלו תשפ״ז", baseline);
        var cultures = new List<CultureInfo> { HebrewIsraelWithHebrewCalendar(), new("he-IL"), new("en-US"), new("ar-SA"), new("th-TH"), new("fa-IR"), new("ja-JP"), new("de-DE"), new("ru-RU"), new("tr-TR") };
        foreach (var ci in cultures)
        {
            var other = RunUnder(ci);
            if (baseline != other)
            {
                var a = baseline.Split((char)10); var b = other.Split((char)10);
                var i = Enumerable.Range(0, Math.Min(a.Length, b.Length)).First(k => a[k] != b[k]);
                Assert.Fail($"output differs under {ci.Name} ({ci.DateTimeFormat.Calendar.GetType().Name}) at line {i}: '{a[i]}' vs '{b[i]}'");
            }
        }

        static string RunUnder(CultureInfo ci)
        {
            var (old, oldUi) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
            try
            {
                CultureInfo.CurrentCulture = ci;
                CultureInfo.CurrentUICulture = ci;
                return Snapshot();
            }
            finally { CultureInfo.CurrentCulture = old; CultureInfo.CurrentUICulture = oldUi; }
        }
    }

    [Fact]
    public void He_IL_hebrew_calendar_culture_really_is_hebrew_calendar_in_this_test()
    {
        // guards the test above: under that culture ordinary formatting would have produced a Hebrew-calendar year
        var ci = HebrewIsraelWithHebrewCalendar();
        Assert.IsType<HebrewCalendar>(ci.DateTimeFormat.Calendar);
        Assert.Contains("תשפ", new DateTime(2026, 11, 13).ToString("yyyy", ci)); // Hebrew-letter year, i.e. NOT the Gregorian 2026
    }

    // ── thread safety ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Concurrent_calls_from_many_threads_give_the_same_answers_as_serial_calls()
    {
        var cities = Svc.Cities.Where((_, i) => i % 7 == 0).ToList();
        var dates = Enumerable.Range(0, 12).Select(i => new DateOnly(2026, 9, 1).AddDays(i * 29)).ToList();
        string Key(CityInfo c, DateOnly d)
        {
            var sb = new StringBuilder();
            var i = Svc.GetDayInfo(d, c);
            sb.Append(i.HebrewDate).Append(i.Parasha).Append(string.Join(';', i.Holidays)).Append(i.DafYomiBavli).Append(i.Omer).Append(i.CandleLighting?.UtcTicks).Append(i.Havdalah?.UtcTicks);
            foreach (var z in Svc.GetZmanim(d, c, AllIds)) sb.Append(z.Id).Append(z.Time?.UtcTicks).Append(z.DisplayText);
            sb.Append(Svc.GetEffectiveDate(new DateTimeOffset(d.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero), c, "tzais"));
            return sb.ToString();
        }
        var expected = new Dictionary<(string, DateOnly), string>();
        foreach (var c in cities) foreach (var d in dates) expected[(c.Name, d)] = Key(c, d);

        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, 32, new ParallelOptions { MaxDegreeOfParallelism = 16 }, worker =>
        {
            var rnd = new Random(worker);
            for (var n = 0; n < 150; n++)
            {
                var c = cities[rnd.Next(cities.Count)];
                var d = dates[rnd.Next(dates.Count)];
                try
                {
                    if (Key(c, d) != expected[(c.Name, d)]) errors.Add($"{c.Name} {d}: differs under concurrency");
                }
                catch (Exception e) { errors.Add($"{c.Name} {d}: {e.GetType().Name} {e.Message}"); }
            }
        });
        Assert.True(errors.IsEmpty, string.Join("\n", errors.Take(10)));
    }

    // ── performance ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Performance_targets_zmanim_98_ids_under_20ms_dayinfo_under_5ms()
    {
        var ny = Svc.FindCity("ניו יורק")!;
        for (var i = 0; i < 20; i++) { Svc.GetZmanim(new DateOnly(2026, 11, 13), Jerusalem, AllIds); Svc.GetDayInfo(new DateOnly(2026, 11, 13), ny); } // JIT warm-up

        double Median(Func<int, object> f)
        {
            var samples = new List<double>();
            for (var batch = 0; batch < 7; batch++)
            {
                var sw = Stopwatch.StartNew();
                for (var i = 0; i < 40; i++) f(i);
                samples.Add(sw.Elapsed.TotalMilliseconds / 40);
            }
            samples.Sort();
            return samples[samples.Count / 2];
        }

        var zmanim = Median(i => Svc.GetZmanim(new DateOnly(2026, 11, 13).AddDays(i % 9), i % 2 == 0 ? Jerusalem : ny, AllIds));
        var dayInfo = Median(i => Svc.GetDayInfo(new DateOnly(2026, 11, 13).AddDays(i % 9), i % 2 == 0 ? Jerusalem : ny));
        var defaults = Median(i => Svc.GetZmanim(new DateOnly(2026, 11, 13).AddDays(i % 9), Jerusalem, Svc.DefaultEnabledZmanim));
        var effective = Median(i => Svc.GetEffectiveDate(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero).AddMinutes(i * 17), Jerusalem, "tzais"));
        output.WriteLine($"GetZmanim(98 ids) {zmanim:F2} ms, GetZmanim(31 defaults) {defaults:F2} ms, GetDayInfo {dayInfo:F2} ms, GetEffectiveDate {effective:F3} ms (medians)");
        Assert.True(zmanim < 20, $"GetZmanim(98) {zmanim:F2} ms");
        Assert.True(dayInfo < 5, $"GetDayInfo {dayInfo:F2} ms");
        Assert.True(effective < 5, $"GetEffectiveDate {effective:F3} ms");
    }

    // ── fuzzing: never throws ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Fuzz_every_city_random_dates_1900_2100_never_throws()
    {
        var rnd = new Random(20261113);
        var failures = new List<string>();
        var edge = new[] { new DateOnly(1900, 1, 1), new DateOnly(2100, 12, 31), new DateOnly(1923, 9, 10), new DateOnly(1923, 9, 11), new DateOnly(2000, 2, 29), new DateOnly(2026, 3, 27), new DateOnly(2026, 10, 25) };
        var transitions = new[] { "sunset", "tzais", "rabbeinuTam", "midnight", "", "garbage", null! };
        foreach (var city in Svc.Cities)
        {
            var dates = edge.Concat(Enumerable.Range(0, 12).Select(_ => new DateOnly(1900, 1, 1).AddDays(rnd.Next(0, 73000)))).ToList();
            foreach (var d in dates)
            {
                try
                {
                    var z = Svc.GetZmanim(d, city, AllIds);
                    Assert.All(z, x => { Assert.NotNull(x.Id); Assert.NotNull(x.Title); if (x.Time != null) Assert.NotNull(x.DisplayText); });
                    var info = Svc.GetDayInfo(d, city);
                    Assert.False(string.IsNullOrEmpty(info.HebrewDate));
                    var now = new DateTimeOffset(d.ToDateTime(new TimeOnly(rnd.Next(24), rnd.Next(60)), DateTimeKind.Utc));
                    foreach (var t in transitions) Svc.GetEffectiveDate(now, city, t);
                }
                catch (Exception e) { failures.Add($"{city.Name} {d:yyyy-MM-dd}: {e.GetType().Name}: {e.Message}"); }
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(10)));
    }

    [Fact]
    public void Null_empty_duplicate_and_unknown_ids_are_harmless()
    {
        var d = new DateOnly(2026, 11, 13);
        Assert.Empty(Svc.GetZmanim(d, Jerusalem, null!));
        Assert.Empty(Svc.GetZmanim(d, Jerusalem, []));
        Assert.Empty(Svc.GetZmanim(d, Jerusalem, ["", "nope", "SUNSET", " sunset"]));
        var r = Svc.GetZmanim(d, Jerusalem, [null!, "sunset", "sunset", "zzz", "alos72Degrees"]);
        Assert.Equal(["alos72Degrees", "sunset"], r.Select(z => z.Id).ToArray());
        Assert.Null(Svc.FindCity(null!));
        Assert.Null(Svc.FindCity(""));
        Assert.Null(Svc.FindCity("אטלנטיס"));
    }

    [Fact]
    public void Extreme_dates_degrade_instead_of_throwing()
    {
        Svc.GetZmanim(DateOnly.MinValue, Jerusalem, AllIds);
        Svc.GetZmanim(DateOnly.MaxValue, Jerusalem, AllIds);
        var max = Svc.GetDayInfo(DateOnly.MaxValue, Jerusalem);
        Assert.Equal(DateOnly.MaxValue, max.Date);
        Svc.GetDayInfo(DateOnly.MinValue, Jerusalem);
        Svc.GetEffectiveDate(DateTimeOffset.MinValue, Jerusalem, "sunset");
        Svc.GetEffectiveDate(DateTimeOffset.MaxValue, Jerusalem, "midnight");
        Svc.GetEffectiveDate(DateTimeOffset.MaxValue, Jerusalem, "sunset");
    }

    // ── time zones ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Every_city_zone_resolves_to_a_real_zone_not_the_longitude_fallback()
    {
        foreach (var city in Svc.Cities)
        {
            var z = TimeZones.Resolve(city);
            Assert.False(z.Id.StartsWith("fixed:", StringComparison.Ordinal), $"{city.Name} ({city.TimeZoneId}) fell back to a fixed offset");
        }
        foreach (var iana in Svc.Cities.Select(c => c.TimeZoneId).Distinct())
            Assert.True(TimeZones.WindowsIds.ContainsKey(iana), $"no Windows fallback id for {iana}");
    }

    /// <summary>Offsets of every zone in the city table at 13 sample dates (noon local) from the Dart <c>timezone</c> package (IANA tzdata 2026e) that Otzaria uses.</summary>
    [Fact]
    public void Zone_offsets_match_the_tzdata_Otzaria_uses()
    {
        var failures = new List<string>();
        foreach (var line in QaGolden.Lines("Golden.qa_timezones.txt.gz"))
        {
            var p = line.Split('|');
            var ymd = p[1].Split('-').Select(int.Parse).ToArray();
            var zone = TimeZones.Resolve(p[0]);
            if (p[0] == "Africa/Casablanca" && ymd[0] == 2026 && ymd[1] <= 2) continue; // Windows' Morocco table differs from tzdata only until 2026-02-08 (already past; later Ramadan windows agree)
            var offset = (int)zone.GetUtcOffset(new DateTime(ymd[0], ymd[1], ymd[2], 12, 0, 0)).TotalSeconds;
            if (offset != int.Parse(p[2], CultureInfo.InvariantCulture)) failures.Add($"{p[0]} {p[1]}: {offset} vs tzdata {p[2]}");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(15)));
    }

    /// <summary>tzdata 2026: Alberta moves to permanent UTC-6 and British Columbia to permanent UTC-7 on 2026-11-01. Windows can lag behind;
    /// the widget must still match Otzaria (otherwise every zman of Edmonton / Calgary / Vancouver is an hour off).</summary>
    [Theory]
    [InlineData("America/Edmonton", "2026-03-07", -7)]
    [InlineData("America/Edmonton", "2026-07-01", -6)]
    [InlineData("America/Edmonton", "2026-10-31", -6)]
    [InlineData("America/Edmonton", "2026-11-13", -6)]
    [InlineData("America/Edmonton", "2027-01-15", -6)]
    [InlineData("America/Edmonton", "2027-07-15", -6)]
    [InlineData("America/Vancouver", "2026-03-07", -8)]
    [InlineData("America/Vancouver", "2026-07-01", -7)]
    [InlineData("America/Vancouver", "2026-11-13", -7)]
    [InlineData("America/Vancouver", "2027-01-15", -7)]
    [InlineData("America/Vancouver", "2027-07-15", -7)]
    [InlineData("America/Denver", "2026-11-13", -7)]
    public void Alberta_and_british_columbia_follow_tzdata_2026(string iana, string date, int expectedHours)
    {
        var d = D(date);
        Assert.Equal(TimeSpan.FromHours(expectedHours), TimeZones.Resolve(iana).GetUtcOffset(d.ToDateTime(new TimeOnly(12, 0))));
    }

    [Fact]
    public void Edmonton_zmanim_carry_the_permanent_offset()
    {
        var edmonton = Svc.FindCity("אדמונטון")!;
        var sunset = Svc.GetZmanim(D("2026-11-13"), edmonton, ["sunset"]).Single().Time!.Value;
        Assert.Equal(TimeSpan.FromHours(-6), sunset.Offset);
        Assert.Equal((17, 45), (sunset.Hour, sunset.Minute)); // Otzaria: 17:45:40 (permanent UTC-6), not 16:45
        var vancouver = Svc.FindCity("ונקובר")!;
        Assert.Equal(TimeSpan.FromHours(-7), Svc.GetZmanim(D("2026-11-13"), vancouver, ["sunset"]).Single().Time!.Value.Offset);
    }

    [Fact]
    public void Windows_fallback_ids_agree_with_the_iana_zones_in_2026_2030()
    {
        var failures = new List<string>();
        foreach (var (iana, windows) in TimeZones.WindowsIds)
        {
            TimeZoneInfo? w;
            try { w = TimeZoneInfo.FindSystemTimeZoneById(windows); }
            catch (Exception e) { failures.Add($"{iana} -> {windows}: {e.GetType().Name}"); continue; }
            var iz = TimeZoneInfo.FindSystemTimeZoneById(iana);
            foreach (var d in new[] { new DateTime(2026, 1, 15, 12, 0, 0), new DateTime(2026, 7, 15, 12, 0, 0), new DateTime(2027, 7, 15, 12, 0, 0), new DateTime(2029, 1, 15, 12, 0, 0) })
            {
                if (iana is "America/Edmonton" or "America/Vancouver") continue; // known tzdata drift, handled by TimeZones.Corrected
                if (iana == "Africa/Casablanca" && d.Month == 1 && d.Year == 2026) continue; // Ramadan 2026 differs in Windows' table
                if (w.GetUtcOffset(d) != iz.GetUtcOffset(d)) failures.Add($"{iana} -> {windows} at {d:yyyy-MM-dd}: {w.GetUtcOffset(d)} vs {iz.GetUtcOffset(d)}");
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    // ── day transition boundaries (all four transitions, every city, DST days, high latitude) ─────────

    [Theory]
    [InlineData("2026-03-27")] // Israel DST starts (Friday)
    [InlineData("2026-03-29")] // UK / EU DST starts
    [InlineData("2026-10-25")] // Israel / UK / EU DST ends
    [InlineData("2026-11-01")] // US DST ends
    [InlineData("2026-11-13")]
    [InlineData("2027-06-21")] // very short nights up north
    [InlineData("2027-12-21")]
    public void The_transition_instant_flips_the_effective_date_exactly_for_all_cities(string date)
    {
        var civil = D(date);
        var failures = new List<string>();
        var nullTzais = 0;
        foreach (var city in Svc.Cities)
        {
            var zone = TimeZones.Resolve(city);
            foreach (var mode in new[] { "sunset", "tzais", "rabbeinuTam" })
            {
                var instant = Svc.GetDayTransitionInstant(civil, city, mode);
                if (instant == null)
                {
                    nullTzais++;
                    // no tzais / sunset today: Otzaria keeps the civil date all day
                    foreach (var hour in new[] { 1, 12, 23 })
                    {
                        var t = new DateTimeOffset(civil.ToDateTime(new TimeOnly(hour, 0)), zone.GetUtcOffset(civil.ToDateTime(new TimeOnly(hour, 0))));
                        if (Svc.GetEffectiveDate(t, city, mode) != civil) failures.Add($"{city.Name} {date} {mode}: null transition but date flipped at {hour}:00");
                    }
                    continue;
                }
                var tt = instant.Value;
                if (DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(tt, zone).DateTime) != civil) continue; // sunset after local midnight (polar-ish): belongs to the next civil day
                var before = Svc.GetEffectiveDate(tt.AddSeconds(-1), city, mode);
                var at = Svc.GetEffectiveDate(tt, city, mode);
                var after = Svc.GetEffectiveDate(tt.AddSeconds(1), city, mode);
                if (before != civil || at != civil.AddDays(1) || after != civil.AddDays(1))
                    failures.Add($"{city.Name} {date} {mode}: {before:yyyy-MM-dd}/{at:yyyy-MM-dd}/{after:yyyy-MM-dd} around {tt:HH:mm:ss}");
            }
            // midnight: flips exactly at the city's civil midnight
            var nextMidnight = TimeZoneInfo.ConvertTimeToUtc(civil.AddDays(1).ToDateTime(TimeOnly.MinValue), zone);
            var m = new DateTimeOffset(nextMidnight, TimeSpan.Zero);
            if (Svc.GetEffectiveDate(m.AddSeconds(-1), city, "midnight") != civil || Svc.GetEffectiveDate(m, city, "midnight") != civil.AddDays(1))
                failures.Add($"{city.Name} {date} midnight");
        }
        output.WriteLine($"{nullTzais} (city, mode) pairs without a transition instant on {date}");
        Assert.True(failures.Count == 0, $"{failures.Count} failures:\n" + string.Join("\n", failures.Take(15)));
    }

    [Fact]
    public void Ambiguous_local_hour_at_dst_end_never_changes_the_civil_date()
    {
        var london = Svc.FindCity("לונדון")!;
        // 2026-10-25 01:30 happens twice in London (BST then GMT): both instants are the same civil day, before sunset
        foreach (var offset in new[] { TimeSpan.FromHours(1), TimeSpan.Zero })
        {
            var now = new DateTimeOffset(2026, 10, 25, 1, 30, 0, offset);
            Assert.Equal(D("2026-10-25"), Svc.GetEffectiveDate(now, london, "sunset"));
            Assert.Equal(D("2026-10-25"), Svc.GetEffectiveDate(now, london, "midnight"));
        }
        // the skipped hour in Israel (2026-03-27 02:00-03:00 does not exist) is also harmless
        Assert.Equal(D("2026-03-27"), Svc.GetEffectiveDate(new DateTimeOffset(2026, 3, 27, 3, 0, 0, TimeSpan.FromHours(3)), Jerusalem, "tzais"));
        Assert.Equal(D("2026-03-27"), Svc.GetEffectiveDate(new DateTimeOffset(2026, 3, 27, 0, 0, 0, TimeSpan.FromHours(2)), Jerusalem, "tzais"));
    }

    [Fact]
    public void High_latitude_without_tzais_keeps_the_civil_date_and_the_other_transitions_still_work()
    {
        var reykjavik = Svc.FindCity("רייקיאוויק")!;
        var date = D("2027-06-21");
        Assert.Null(Svc.GetDayTransitionInstant(date, reykjavik, "tzais"));  // the sun never gets 8.5 degrees below the horizon
        foreach (var hour in new[] { 0, 6, 12, 18, 23 })
            Assert.Equal(date, Svc.GetEffectiveDate(new DateTimeOffset(date.ToDateTime(new TimeOnly(hour, 30)), TimeSpan.Zero), reykjavik, "tzais"));
        // ...and the zmanim list marks tzais as absent, not as an exception
        var tzais = Svc.GetZmanim(date, reykjavik, ["tzeitGeonim8point5", "sunset"]);
        Assert.Contains(tzais, z => z.Id == "tzeitGeonim8point5" && z.Time == null && z.DisplayText == null);
        Assert.Contains(tzais, z => z.Id == "sunset");
    }
}
