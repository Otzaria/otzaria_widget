using Kosher.Zmanim.HebrewCalendar;
using OtzariaWidget.Calendar.Hebrew;

namespace OtzariaWidget.Calendar.Zmanim;

/// <summary>
/// Kidush levana zmanim exactly as Otzaria shows them (zmanim_helpers.dart formatKidushLevanaNight).
/// Otzaria/kosher_dart quirk reproduced on purpose: <c>getMoladAsDateTime()</c> forgets to subtract the
/// 20m56.5s local-mean-time offset, so the "raw" instant is the naive molad wall-clock fields + the offset,
/// interpreted as Jerusalem standard time (UTC+2). Kosher.Zmanim's own GetTchilasZmanKidushLevana* are
/// LMT-corrected (~21 min earlier) and are NOT used. The returned label is "ליל &lt;weekday&gt; &lt;day&gt; לחודש &lt;time&gt;".
/// </summary>
internal static class KidushLevana
{
    private static readonly string[] NightNames =
        ["", "ליל ראשון", "ליל שני", "ליל שלישי", "ליל רביעי", "ליל חמישי", "ליל שישי", "ליל שבת"];

    public static ZmanValue Compute(ZmanContext c, TimeSpan offset, bool pushToTzais)
    {
        var rawNaive = RawNaive(c.Jewish, offset);
        // Dart: DateTime.utc(y,M,d,h,m,s) (milliseconds dropped) minus 2 hours  ==  the same wall-clock at UTC+2.
        var rawInstant = new DateTimeOffset(
            new DateTime(rawNaive.Year, rawNaive.Month, rawNaive.Day, rawNaive.Hour, rawNaive.Minute, rawNaive.Second),
            TimeSpan.FromHours(2));
        var local = TimeZoneInfo.ConvertTime(rawInstant, c.Zone);

        // The civil day (in the city) on which the moment falls → alos / tzais / sunset of that day.
        var day = ZmanContext.Create(c.City, DateOnly.FromDateTime(local.DateTime));
        var alos = day.Calendar.GetAlos16Point1Degrees();   // kosher_dart getAlosHashachar()
        var tzais = day.Calendar.GetTzaisGeonim8Point5Degrees(); // kosher_dart getTzais()
        var sunset = day.Calendar.GetSunset();

        var chosen = local;
        DateTimeOffset? sunsetLocal = null;
        if (alos != null && tzais != null && sunset != null)
        {
            sunsetLocal = sunset;
            if (rawInstant > alos.Value && rawInstant < tzais.Value)
                chosen = pushToTzais ? tzais.Value : alos.Value;
        }

        var advance = sunsetLocal != null && chosen > sunsetLocal.Value;
        return new ZmanValue(chosen, NightLabel(chosen, advance));
    }

    /// <summary>The naive molad wall-clock (Jerusalem LMT fields, NOT corrected) of the month containing <paramref name="jc"/>, plus <paramref name="offset"/>.</summary>
    internal static DateTime RawNaive(JewishCalendar jc, TimeSpan offset)
    {
        var molad = jc.GetMolad();
        var seconds = (int)(molad.MoladChalakim * 10 / 3.0); // kosher_dart: moladSeconds.toInt(), milliseconds always 0
        return new DateTime(molad.LocalDate.Year, molad.LocalDate.Month, molad.LocalDate.Day,
            molad.MoladHours, molad.MoladMinutes, seconds) + offset;
    }

    private static string NightLabel(DateTimeOffset moment, bool advanceToNextDay)
    {
        var date = DateOnly.FromDateTime(moment.DateTime);
        if (advanceToNextDay) date = date.AddDays(1);
        var jd = new JewishDate(date);
        var dow = jd.DayOfWeek;
        var night = dow is >= 1 and <= 7 ? NightNames[dow] : "";
        return $"{night} {HebrewNumbers.Format(jd.JewishDayOfMonth)} לחודש {ZmanTimeFormatter.Format(moment)}";
    }
}
