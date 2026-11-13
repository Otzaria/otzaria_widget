using System.Globalization;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.Calendar.Tests;

public class EffectiveDateAndDstTests
{
    private static readonly CalendarService Svc = new();
    private static readonly CityInfo Jerusalem = Svc.FindCity("ירושלים")!;
    private static readonly CityInfo NewYork = Svc.FindCity("ניו יורק")!;
    private static readonly CityInfo London = Svc.FindCity("לונדון")!;

    private static DateOnly D(string s) => DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTimeOffset Zman(string date, CityInfo city, string id)
        => Svc.GetZmanim(D(date), city, [id]).Single().Time!.Value;

    // ── effective date (day transition) ───────────────────────────────────────────

    [Fact]
    public void Sunset_transition_flips_the_date_at_the_elevation_adjusted_sunset()
    {
        var sunset = Zman("2026-11-13", Jerusalem, "sunset"); // 16:45:32
        Assert.Equal(D("2026-11-13"), Svc.GetEffectiveDate(sunset.AddSeconds(-1), Jerusalem, "sunset"));
        Assert.Equal(D("2026-11-14"), Svc.GetEffectiveDate(sunset, Jerusalem, "sunset"));
        Assert.Equal(D("2026-11-14"), Svc.GetEffectiveDate(sunset.AddMinutes(1), Jerusalem, "sunset"));
        // morning, noon, and just before civil midnight
        Assert.Equal(D("2026-11-13"), Svc.GetEffectiveDate(new DateTimeOffset(2026, 11, 13, 0, 5, 0, TimeSpan.FromHours(2)), Jerusalem, "sunset"));
        Assert.Equal(D("2026-11-13"), Svc.GetEffectiveDate(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.FromHours(2)), Jerusalem, "sunset"));
        // after sunset the Jewish date is already the next civil date, and it stays so after civil midnight (new civil day, before its sunset)
        Assert.Equal(D("2026-11-14"), Svc.GetEffectiveDate(new DateTimeOffset(2026, 11, 13, 23, 59, 0, TimeSpan.FromHours(2)), Jerusalem, "sunset"));
        Assert.Equal(D("2026-11-14"), Svc.GetEffectiveDate(new DateTimeOffset(2026, 11, 14, 0, 1, 0, TimeSpan.FromHours(2)), Jerusalem, "sunset"));
    }

    [Fact]
    public void Tzais_and_rabbeinu_tam_transitions()
    {
        var tzais = Zman("2026-11-13", Jerusalem, "tzeitGeonim8point5");
        Assert.Equal(D("2026-11-13"), Svc.GetEffectiveDate(tzais.AddSeconds(-2), Jerusalem, "tzais"));
        Assert.Equal(D("2026-11-14"), Svc.GetEffectiveDate(tzais.AddSeconds(1), Jerusalem, "tzais"));

        var rt = Zman("2026-11-13", Jerusalem, "sunset").AddMinutes(72);
        Assert.Equal(D("2026-11-13"), Svc.GetEffectiveDate(rt.AddSeconds(-1), Jerusalem, "rabbeinuTam"));
        Assert.Equal(D("2026-11-14"), Svc.GetEffectiveDate(rt, Jerusalem, "rabbeinuTam"));
        // between sunset and RT the date has NOT advanced for rabbeinuTam but has for sunset
        var between = Zman("2026-11-13", Jerusalem, "sunset").AddMinutes(30);
        Assert.Equal(D("2026-11-13"), Svc.GetEffectiveDate(between, Jerusalem, "rabbeinuTam"));
        Assert.Equal(D("2026-11-14"), Svc.GetEffectiveDate(between, Jerusalem, "sunset"));
    }

    [Fact]
    public void Midnight_transition_is_the_civil_date()
    {
        Assert.Equal(D("2026-11-13"), Svc.GetEffectiveDate(new DateTimeOffset(2026, 11, 13, 23, 59, 59, TimeSpan.FromHours(2)), Jerusalem, "midnight"));
        Assert.Equal(D("2026-11-14"), Svc.GetEffectiveDate(new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.FromHours(2)), Jerusalem, "midnight"));
        Assert.Equal(D("2026-11-13"), Svc.GetEffectiveDate(new DateTimeOffset(2026, 11, 13, 18, 30, 0, TimeSpan.FromHours(2)), Jerusalem, "midnight"));
    }

    [Fact]
    public void Unknown_transition_behaves_like_sunset_as_in_otzaria()
    {
        var after = Zman("2026-11-13", Jerusalem, "sunset").AddMinutes(5);
        Assert.Equal(D("2026-11-14"), Svc.GetEffectiveDate(after, Jerusalem, "whatever"));
        Assert.Equal(D("2026-11-14"), Svc.GetEffectiveDate(after, Jerusalem, ""));
    }

    [Fact]
    public void Effective_date_uses_the_city_zone_not_the_instants_offset()
    {
        // 2026-11-13 21:30 UTC is 23:30 in Jerusalem (already after sunset) but 16:30 in New York (before sunset)
        var instant = new DateTimeOffset(2026, 11, 13, 21, 30, 0, TimeSpan.Zero);
        Assert.Equal(D("2026-11-14"), Svc.GetEffectiveDate(instant, Jerusalem, "sunset"));
        Assert.Equal(D("2026-11-13"), Svc.GetEffectiveDate(instant, NewYork, "sunset"));
        // the same instant with a different offset must give the same answer
        Assert.Equal(D("2026-11-14"), Svc.GetEffectiveDate(instant.ToOffset(TimeSpan.FromHours(-8)), Jerusalem, "sunset"));
        // civil date differs between zones: 2026-11-14 01:00 UTC is Nov 14 03:00 in Jerusalem and Nov 13 20:00 in NY (after sunset -> Nov 14)
        var late = new DateTimeOffset(2026, 11, 14, 1, 0, 0, TimeSpan.Zero);
        Assert.Equal(D("2026-11-14"), Svc.GetEffectiveDate(late, Jerusalem, "sunset"));
        Assert.Equal(D("2026-11-14"), Svc.GetEffectiveDate(late, NewYork, "sunset"));
    }

    [Fact]
    public void Effective_date_on_dst_change_days()
    {
        // Israel DST starts Friday 2026-03-27 02:00 (UTC+2 -> +3): the civil date logic must not be off by the lost hour
        Assert.Equal(D("2026-03-27"), Svc.GetEffectiveDate(new DateTimeOffset(2026, 3, 27, 12, 0, 0, TimeSpan.FromHours(3)), Jerusalem, "sunset"));
        var sunset = Zman("2026-03-27", Jerusalem, "sunset");
        Assert.Equal(TimeSpan.FromHours(3), sunset.Offset);
        Assert.Equal(D("2026-03-28"), Svc.GetEffectiveDate(sunset.AddMinutes(1), Jerusalem, "sunset"));
        Assert.Equal(D("2026-03-27"), Svc.GetEffectiveDate(sunset.AddMinutes(-1), Jerusalem, "sunset"));
        // 2026-03-27 00:30 +02:00 (before the jump) is still the same civil day
        Assert.Equal(D("2026-03-27"), Svc.GetEffectiveDate(new DateTimeOffset(2026, 3, 27, 0, 30, 0, TimeSpan.FromHours(2)), Jerusalem, "midnight"));
        // US: 2026-11-01 DST ends at 02:00 EDT -> 01:00 EST (25 hour day)
        var nySunset = Zman("2026-11-01", NewYork, "sunset");
        Assert.Equal(TimeSpan.FromHours(-5), nySunset.Offset);
        Assert.Equal(D("2026-11-01"), Svc.GetEffectiveDate(nySunset.AddMinutes(-1), NewYork, "sunset"));
        Assert.Equal(D("2026-11-02"), Svc.GetEffectiveDate(nySunset.AddMinutes(1), NewYork, "sunset"));
    }

    [Fact]
    public void Ohr_prefix_shows_between_the_transition_and_90_minutes_before_sunrise()
    {
        var sunsetNow = Zman("2026-11-13", Jerusalem, "sunset").AddMinutes(10);
        Assert.True(Svc.ShouldShowOhrPrefix(sunsetNow, Jerusalem, "sunset"));        // evening: the night that leads into 4 Kislev
        Assert.False(Svc.ShouldShowOhrPrefix(sunsetNow, Jerusalem, "midnight"));     // midnight transition: today is still 3 Kislev, past alos
        var predawn = new DateTimeOffset(2026, 11, 14, 4, 0, 0, TimeSpan.FromHours(2));
        Assert.True(Svc.ShouldShowOhrPrefix(predawn, Jerusalem, "midnight"));        // before sunrise - 90 min (~04:31)
        Assert.False(Svc.ShouldShowOhrPrefix(new DateTimeOffset(2026, 11, 14, 8, 0, 0, TimeSpan.FromHours(2)), Jerusalem, "midnight"));
    }

    // ── DST boundaries ────────────────────────────────────────────────────────────

    [Fact]
    public void Israel_dst_start_and_end_zmanim_carry_the_right_offsets()
    {
        // starts Friday 2026-03-27 (02:00 local), ends Sunday 2026-10-25 (02:00 local)
        Assert.Equal(TimeSpan.FromHours(2), Zman("2026-03-26", Jerusalem, "sunrise").Offset);
        Assert.Equal(TimeSpan.FromHours(3), Zman("2026-03-27", Jerusalem, "sunrise").Offset);
        Assert.Equal(TimeSpan.FromHours(3), Zman("2026-10-24", Jerusalem, "sunset").Offset);
        Assert.Equal(TimeSpan.FromHours(2), Zman("2026-10-25", Jerusalem, "sunset").Offset);

        // local clock sanity: sunrise barely changes in wall-clock terms across the 1h jump... it moves by the jump (+1h) minus 1-2 minutes of seasonal drift
        var before = Zman("2026-03-26", Jerusalem, "sunrise");
        var after = Zman("2026-03-27", Jerusalem, "sunrise");
        var wallDelta = (after.TimeOfDay - before.TimeOfDay).TotalMinutes;
        Assert.InRange(wallDelta, 56, 60);
        // and the displayed candle lighting on the DST Friday is computed from the +03:00 sunset
        var candle = Svc.GetZmanim(D("2026-03-27"), Jerusalem, ["candleLighting"]).Single().Time!.Value;
        Assert.Equal(TimeSpan.FromHours(3), candle.Offset);
        Assert.Equal(Zman("2026-03-27", Jerusalem, "sunset").AddMinutes(-40), candle);
    }

    [Fact]
    public void Chatzos_layla_across_the_dst_jump_is_the_true_midpoint_of_the_night()
    {
        // night of Thursday 2026-03-26 -> Friday 2026-03-27 is only 11h long in wall-clock terms but the midpoint must still be the midpoint of the instants
        var sunset = Zman("2026-03-26", Jerusalem, "sunset");
        var sunriseTomorrow = Zman("2026-03-27", Jerusalem, "sunriseAstronomical");
        var mid = Zman("2026-03-26", Jerusalem, "chatzosLayla");
        Assert.InRange(Math.Abs((mid - sunset).TotalMinutes - (sunriseTomorrow - mid).TotalMinutes), 0, 0.1);
        // the midpoint instant is ~23:5x in the old offset: before the jump at 02:00 -> +02:00
        Assert.Equal(TimeSpan.FromHours(2), mid.Offset);
        // night 2026-10-24 -> 25 (clocks go back): midpoint is before 02:00 local -> still +03:00
        var mid2 = Zman("2026-10-24", Jerusalem, "chatzosLayla");
        Assert.Equal(TimeSpan.FromHours(3), mid2.Offset);
    }

    [Fact]
    public void Us_dst_start_and_end()
    {
        Assert.Equal(TimeSpan.FromHours(-5), Zman("2026-03-07", NewYork, "sunrise").Offset);
        Assert.Equal(TimeSpan.FromHours(-4), Zman("2026-03-08", NewYork, "sunrise").Offset);
        Assert.Equal(TimeSpan.FromHours(-4), Zman("2026-10-31", NewYork, "sunset").Offset);
        Assert.Equal(TimeSpan.FromHours(-5), Zman("2026-11-01", NewYork, "sunset").Offset);
        // wall clock of sunset jumps by about +1h at the start (17:50 -> 18:5x)
        var a = Zman("2026-03-07", NewYork, "sunset");
        var b = Zman("2026-03-08", NewYork, "sunset");
        Assert.InRange((b.TimeOfDay - a.TimeOfDay).TotalMinutes, 58, 63);
        // candle lighting for New York (18 min) on a DST Friday
        var friday = Svc.GetZmanim(D("2026-11-13"), NewYork, ["candleLighting"]).Single().Time!.Value;
        Assert.Equal(TimeSpan.FromHours(-5), friday.Offset);
    }

    [Fact]
    public void Uk_dst_start_and_end()
    {
        Assert.Equal(TimeSpan.Zero, Zman("2026-03-28", London, "sunrise").Offset);
        Assert.Equal(TimeSpan.FromHours(1), Zman("2026-03-29", London, "sunrise").Offset);
        Assert.Equal(TimeSpan.FromHours(1), Zman("2026-10-24", London, "sunset").Offset);
        Assert.Equal(TimeSpan.Zero, Zman("2026-10-25", London, "sunset").Offset);
    }

    [Fact]
    public void Day_length_is_plausible_every_day_of_a_year_in_all_israel_and_us_zones()
    {
        // a whole year in Jerusalem and New York: sunrise < chatzos < sunset, day length 9-15.5h, no exception, the offset flips exactly twice
        foreach (var city in new[] { Jerusalem, NewYork })
        {
            var offsets = new HashSet<TimeSpan>();
            var flips = 0;
            TimeSpan? last = null;
            for (var d = D("2026-01-01"); d.Year == 2026; d = d.AddDays(1))
            {
                var z = Svc.GetZmanim(d, city, ["sunrise", "chatzos", "sunset"]).ToDictionary(x => x.Id);
                var sr = z["sunrise"].Time!.Value;
                var ch = z["chatzos"].Time!.Value;
                var ss = z["sunset"].Time!.Value;
                Assert.True(sr < ch && ch < ss, $"{city.Name} {d:yyyy-MM-dd}");
                var hours = (ss - sr).TotalHours;
                Assert.InRange(hours, 9, 15.5);
                offsets.Add(sr.Offset);
                if (last != null && last != ss.Offset) flips++;
                last = ss.Offset;
            }
            Assert.Equal(2, offsets.Count);
            Assert.Equal(2, flips);
        }
    }
}
