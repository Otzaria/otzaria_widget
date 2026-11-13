using Kosher.Zmanim;
using Kosher.Zmanim.HebrewCalendar;
using Kosher.Zmanim.Util;
using OtzariaWidget.Calendar.Cities;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.Calendar.Zmanim;

/// <summary>
/// One (city, civil date) computation context: the configured <see cref="ComprehensiveZmanimCalendar"/>
/// (configured to reproduce kosher_dart 2.0.20 / Otzaria, see <see cref="Create"/>) and a lazily built
/// <see cref="JewishCalendar"/> for the same civil date.
/// </summary>
public sealed class ZmanContext
{
    internal const double KosherDartEarthRadiusKm = 6356.9;

    private readonly Lazy<JewishCalendar> _jewish;
    private readonly Lazy<ZmanContext> _tomorrow;

    public CityInfo City { get; }
    public DateOnly Date { get; }
    public TimeZoneInfo Zone { get; }
    public ComprehensiveZmanimCalendar Calendar { get; }
    public JewishCalendar Jewish => _jewish.Value;
    /// <summary>The same city for date + 1 (chatzos layla needs tomorrow's sunrise).</summary>
    public ZmanContext Tomorrow => _tomorrow.Value;

    private ZmanContext(CityInfo city, DateOnly date)
    {
        City = city;
        Date = date;
        Zone = TimeZones.Resolve(city);
        // Otzaria: location.setElevation(elevation > 0 ? elevation : 0)  (zmanim_helpers.dart:45) — negative elevations (Tiberias) become 0.
        var geo = new GeoLocation(city.Name, city.Latitude, city.Longitude, city.Elevation > 0 ? city.Elevation : 0, Zone);
        Calendar = new OtzariaZmanimCalendar(geo) { LocalDate = date };
        // Parity with kosher_dart 2.0.20 (see docs/research/calendar-and-zmanim.md §9.2):
        //  1) kosher_dart uses a fixed 16' solar radius; KosherJava 3 defaults to the date based apparent radius (1-2 s shift).
        Calendar.AstronomicalCalculator.UseApparentSolarRadius = false;
        //  3) kosher_dart's elevation adjustment uses an earth radius of 6356.9 km; Kosher.Zmanim 3 defaults to 6371.0088 km.
        Calendar.AstronomicalCalculator.EarthRadius = KosherDartEarthRadiusKm;
        //  2) kosher_dart chatzos = midpoint of sea-level sunrise/sunset; KosherJava 3 defaults to true astronomical noon (up to ~30 s apart).
        Calendar.UseAstronomicalChatzos = false;
        Calendar.UseAstronomicalChatzosForOtherZmanim = false;
        _jewish = new Lazy<JewishCalendar>(() => new JewishCalendar(date) { InIsrael = city.IsInIsrael });
        _tomorrow = new Lazy<ZmanContext>(() => new ZmanContext(city, date.AddDays(1)));
    }

    public static ZmanContext Create(CityInfo city, DateOnly date) => new(city, date);

    /// <summary>Elevation-adjusted sunset — kosher_dart <c>getSunset()</c>; every fixed-minute Otzaria zman is added to this.</summary>
    public DateTimeOffset? Sunset => Calendar.GetSunset();
}

/// <summary>
/// kosher_dart 2.0.20 builds the sunrise/sunset/degree instants from <c>hours:minutes:seconds</c> with the sub-second
/// part DROPPED (<c>cal.add(Duration(hours, minutes, seconds))</c>); Kosher.Zmanim 3 keeps milliseconds. Every
/// shaah-zmanis based zman is derived from those instants, so keeping the milliseconds shifted ~23% of the
/// derived zmanim by one second (and ~1% across a displayed-minute boundary). Truncating here reproduces Otzaria exactly.
/// </summary>
internal sealed class OtzariaZmanimCalendar : ComprehensiveZmanimCalendar
{
    public OtzariaZmanimCalendar(GeoLocation geoLocation) : base(geoLocation) { }

    protected override DateTimeOffset? GetDateTimeOffsetFromTime(double time, AstronomicalCalendar.SolarEvent solarEvent)
    {
        var t = base.GetDateTimeOffsetFromTime(time, solarEvent);
        if (t == null) return null;
        var v = t.Value;
        if (double.IsNaN(time) || double.IsInfinity(time) || time < 0 || time >= 24) return new DateTimeOffset(v.Ticks - v.Ticks % TimeSpan.TicksPerSecond, v.Offset);

        // Same arithmetic as kosher_dart getDateFromTime (hours / minutes / seconds each truncated from the fractional UTC hour);
        // the library's own conversion can differ by one second right at a whole-second boundary.
        var c = time;
        var hours = (int)c; c -= hours;
        c *= 60; var minutes = (int)c; c -= minutes;
        c *= 60; var seconds = (int)c;
        var hms = new TimeSpan(hours, minutes, seconds);
        var baseUtc = v.UtcDateTime;
        var best = baseUtc.Date + hms;
        foreach (var dayShift in new[] { -1, 1 }) // the library may have rolled the date at the 24:00 edge
        {
            var cand = baseUtc.Date.AddDays(dayShift) + hms;
            if (Math.Abs((cand - baseUtc).Ticks) < Math.Abs((best - baseUtc).Ticks)) best = cand;
        }
        return new DateTimeOffset(best, TimeSpan.Zero).ToOffset(v.Offset);
    }

    // kosher_dart does all shaah-zmanis arithmetic in double milliseconds and truncates (toInt) the final offset;
    // Kosher.Zmanim 3 rounds the temporal hour to 100 ns ticks first. Zmanim derived from whole-second sunrise/sunset
    // often land exactly on a whole second, where that tiny difference flips the displayed seconds marker ('.' vs ':').

    /// <summary>kosher_dart <c>getTemporalHour(start, end)</c>: (end - start) in epoch milliseconds / 12, as a double.</summary>
    private static double TemporalHourMs(DateTimeOffset start, DateTimeOffset end)
        => (end.ToUnixTimeMilliseconds() - start.ToUnixTimeMilliseconds()) / 12.0;

    /// <summary>kosher_dart <c>getTimeOffset</c>: start + offset.toInt() milliseconds.</summary>
    private static DateTimeOffset OffsetMs(DateTimeOffset start, double offsetMs)
        => start.AddTicks((long)Math.Truncate(offsetMs) * TimeSpan.TicksPerMillisecond);

    public override DateTimeOffset? GetShaahZmanisBasedZman(DateTimeOffset? startOfDay, DateTimeOffset? endOfDay, double hours)
        => startOfDay == null || endOfDay == null ? null : OffsetMs(startOfDay.Value, TemporalHourMs(startOfDay.Value, endOfDay.Value) * hours);

    public override DateTimeOffset? GetSunTransit(DateTimeOffset? startOfDay, DateTimeOffset? endOfDay)
    {
        startOfDay ??= GetSeaLevelSunrise();
        endOfDay ??= GetSeaLevelSunset();
        return startOfDay == null || endOfDay == null ? null : OffsetMs(startOfDay.Value, TemporalHourMs(startOfDay.Value, endOfDay.Value) * 6);
    }

    public override DateTimeOffset? GetZmanisBasedOffset(double hours)
    {
        if (hours == 0) return null;
        var sunrise = GetSunriseBasedOnElevationSetting();
        var sunset = GetSunsetBasedOnElevationSetting();
        if (sunrise == null || sunset == null) return null;
        var th = TemporalHourMs(sunrise.Value, sunset.Value);
        return OffsetMs(hours > 0 ? sunset.Value : sunrise.Value, th * hours);
    }
}
