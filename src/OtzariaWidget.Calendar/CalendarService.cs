using Kosher.Zmanim.HebrewCalendar;
using OtzariaWidget.Calendar.Cities;
using OtzariaWidget.Calendar.Hebrew;
using OtzariaWidget.Calendar.Zmanim;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.Calendar;

/// <summary>
/// Otzaria-consistent Hebrew calendar + zmanim engine (see docs/research/calendar-and-zmanim.md).
/// Stateless and thread-safe; every call builds its own calendars. Machine formatting always uses invariant/Gregorian.
/// </summary>
public sealed class CalendarService : ICalendarService
{
    public const string DayTransitionSunset = "sunset";
    public const string DayTransitionTzais = "tzais";
    public const string DayTransitionRabbeinuTam = "rabbeinuTam";
    public const string DayTransitionMidnight = "midnight";

    public IReadOnlyList<CityInfo> Cities => CityTable.All;

    public CityInfo? FindCity(string name) => CityTable.Find(name);

    public IReadOnlyList<ZmanDefinition> ZmanimRegistry => Zmanim.ZmanimRegistry.Definitions;

    public IReadOnlyList<string> DefaultEnabledZmanim => Zmanim.ZmanimRegistry.DefaultEnabled;

    /// <summary>Full registry detail (subtitle, category, pairing, default flag) beyond the contract's ZmanDefinition.</summary>
    public IReadOnlyList<ZmanRegistryEntry> ZmanimRegistryDetails => Zmanim.ZmanimRegistry.All;

    public DayInfo GetDayInfo(DateOnly date, CityInfo city)
    {
        try { return BuildDayInfo(date, city); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or ArithmeticException)
        {
            // far outside the supported range (the library or DateOnly cannot represent the date): an empty-but-valid answer instead of an exception
            string gregorian;
            try { gregorian = HebrewDateText.Gregorian(date); } catch (ArgumentException) { gregorian = ""; }
            return new DayInfo { Date = date, HebrewDate = "", GregorianDate = gregorian };
        }
    }

    private static DayInfo BuildDayInfo(DateOnly date, CityInfo city)
    {
        var jc = new JewishCalendar(date) { InIsrael = city.IsInIsrael };
        var dow = jc.DayOfWeek;

        DateTimeOffset? candle = null, havdalah = null;
        if (ZmanCalculator.IsErevShabbosOrYomTov(jc) || ZmanCalculator.IsMotzeiShabbos(jc) || ZmanCalculator.IsMotzeiYomTov(jc))
        {
            try
            {
                var ctx = ZmanContext.Create(city, date);
                var sunset = ctx.Sunset;
                if (sunset != null)
                {
                    if (ZmanCalculator.IsErevShabbosOrYomTov(jc)) candle = sunset.Value.AddMinutes(-city.CandleLightingMinutes);
                    // Otzaria's default motzei (motzeiShabbos / motzeiYomTov "רגיל") = sunset + 34
                    if (ZmanCalculator.IsMotzeiShabbos(jc) || ZmanCalculator.IsMotzeiYomTov(jc)) havdalah = sunset.Value.AddMinutes(34);
                }
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or ArithmeticException)
            {
                // dates at the very edge of DateTime's range: no sun times, the rest of the day info is still valid
            }
        }

        var omerDay = jc.GetDayOfOmer();
        return new DayInfo
        {
            Date = date,
            HebrewDate = HebrewDateText.Format(jc),
            HebrewDayOfWeek = HebrewDateText.DayOfWeekName(dow),
            GregorianDate = HebrewDateText.Gregorian(date),
            Parasha = HolidayLabels.Parasha(jc),
            Holidays = HolidayLabels.Calc(jc),
            DafYomiBavli = DafYomiText.Bavli(jc),
            DafYomiYerushalmi = DafYomiText.Yerushalmi(jc),
            Omer = omerDay == -1 ? null : OmerText.Build(omerDay),
            CandleLighting = candle,
            Havdalah = havdalah,
            IsShabbat = dow == 7,
            IsYomTov = jc.IsYomTovAssurBemelacha(),
        };
    }

    public IReadOnlyList<ZmanTime> GetZmanim(DateOnly date, CityInfo city, IReadOnlyList<string> ids)
        => ZmanimEngine.Compute(date, city, ids);

    /// <summary>Port of <c>resolveCalendarDayForTransition</c> (calendar_cubit.dart:1621-1654): the civil day of
    /// <paramref name="now"/> in the city's zone, advanced by one day once the transition instant of that civil day has passed
    /// (sunset = elevation-adjusted sunset, tzais = 8.5°, rabbeinuTam = sunset + 72 min, midnight = never). Unknown values behave as "sunset".</summary>
    public DateOnly GetEffectiveDate(DateTimeOffset now, CityInfo city, string dayTransition)
    {
        var zone = TimeZones.Resolve(city);
        DateTimeOffset nowInCity;
        try { nowInCity = TimeZoneInfo.ConvertTime(now, zone); }
        catch (ArgumentException) { return DateOnly.FromDateTime(now.UtcDateTime); } // instant at the very edge of DateTime's range
        var civilToday = DateOnly.FromDateTime(nowInCity.DateTime);
        if (string.Equals(dayTransition, DayTransitionMidnight, StringComparison.Ordinal)) return civilToday;

        DateTimeOffset? transition;
        try { transition = TransitionInstant(civilToday, city, dayTransition); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or ArithmeticException) { return civilToday; }
        if (transition == null || nowInCity < transition.Value || civilToday == DateOnly.MaxValue) return civilToday;
        return civilToday.AddDays(1);
    }

    /// <summary>The instant at which the Hebrew day changes for <paramref name="civilDate"/> (null for midnight / uncomputable).</summary>
    public DateTimeOffset? GetDayTransitionInstant(DateOnly civilDate, CityInfo city, string dayTransition)
        => string.Equals(dayTransition, DayTransitionMidnight, StringComparison.Ordinal) ? null : TransitionInstant(civilDate, city, dayTransition);

    private static DateTimeOffset? TransitionInstant(DateOnly civilDate, CityInfo city, string dayTransition)
    {
        var ctx = ZmanContext.Create(city, civilDate);
        return dayTransition switch
        {
            DayTransitionTzais => ctx.Calendar.GetTzaisGeonim8Point5Degrees(),
            DayTransitionRabbeinuTam => ctx.Sunset?.AddMinutes(72),
            _ => ctx.Sunset, // "sunset" and anything unknown (calendarDayTransitionFromString default)
        };
    }

    /// <summary>Otzaria's "אור ל" header prefix (shouldShowOhrPrefixForCalendarHeader, calendar_cubit.dart:1687-1711):
    /// true while <paramref name="now"/> is before (elevation-adjusted sunrise − 90 min) of the effective "today".
    /// The header then reads "אור ל" + the Hebrew date.</summary>
    public bool ShouldShowOhrPrefix(DateTimeOffset now, CityInfo city, string dayTransition)
    {
        var today = GetEffectiveDate(now, city, dayTransition);
        var sunrise = ZmanContext.Create(city, today).Calendar.GetSunrise();
        if (sunrise == null) return false;
        return TimeZoneInfo.ConvertTime(now, TimeZones.Resolve(city)) < sunrise.Value.AddMinutes(-90);
    }

    /// <summary>The Hebrew date chip in Otzaria's calendar-header style ("ג כסלו ה׳תשפ״ז").</summary>
    public string GetCalendarHeaderDate(DateOnly date, CityInfo city)
        => HebrewDateText.FormatCalendarHeader(new JewishCalendar(date) { InIsrael = city.IsInIsrael });
}
