// Agent D - city / date / zmanim-selection resolution shared by the calendar-based widgets.
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.Provider.Cards;

internal static class CalendarContext
{
    public static CityInfo ResolveCity(CardContext ctx, string? widgetCity)
    {
        var cal = ctx.Calendar;
        string? name = !string.IsNullOrWhiteSpace(widgetCity) ? widgetCity
            : !string.IsNullOrWhiteSpace(ctx.Settings.CityOverride) ? ctx.Settings.CityOverride
            : ctx.Prefs.SelectedCity;
        var city = (name is null ? null : cal.FindCity(name))
                   ?? cal.FindCity(ctx.Prefs.SelectedCity)
                   ?? cal.FindCity("ירושלים")
                   ?? cal.Cities.FirstOrDefault();
        return city ?? throw new InvalidOperationException("no city available");
    }

    /// <summary>The Jewish current day (honours the day-transition setting).</summary>
    public static DateOnly Today(CardContext ctx, CityInfo city) =>
        ctx.Calendar.GetEffectiveDate(ctx.Now, city, ctx.DayTransition);

    public static IReadOnlyList<string> ZmanimIds(CardContext ctx)
    {
        if (ctx.State.ZmanimMode == "custom" && ctx.State.ZmanimIds.Count > 0) return ctx.State.ZmanimIds;
        // An override (even an empty one: the settings UI says "no times selected - the widget shows none") wins over Otzaria's own list.
        if (ctx.Settings.ZmanimOverride is { } o) return o;
        if (ctx.Prefs.EnabledZmanim.Count > 0) return ctx.Prefs.EnabledZmanim;
        return ctx.Calendar.DefaultEnabledZmanim;
    }

    /// <summary>Next instant at which the Jewish date or the Gregorian (city) date changes. Null on failure.</summary>
    public static DateTimeOffset? NextDayBoundary(CardContext ctx, CityInfo city)
    {
        DateTimeOffset? best = NextLocalMidnight(ctx.Now, city.TimeZoneId);
        try
        {
            var d0 = ctx.Calendar.GetEffectiveDate(ctx.Now, city, ctx.DayTransition);
            var lo = ctx.Now;
            var hi = ctx.Now.AddHours(26);
            if (ctx.Calendar.GetEffectiveDate(hi, city, ctx.DayTransition) != d0)
            {
                while ((hi - lo).TotalSeconds > 2)
                {
                    var m = lo + (hi - lo) / 2;
                    if (ctx.Calendar.GetEffectiveDate(m, city, ctx.DayTransition) == d0) lo = m; else hi = m;
                }
                if (best is null || hi < best) best = hi;
            }
        }
        catch (Exception) { /* keep the midnight fallback */ }
        return best;
    }

    public static DateTimeOffset NextLocalMidnight(DateTimeOffset now, string? timeZoneId)
    {
        TimeZoneInfo tz;
        try { tz = string.IsNullOrWhiteSpace(timeZoneId) ? TimeZoneInfo.Local : TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (Exception) { tz = TimeZoneInfo.Local; }
        var local = TimeZoneInfo.ConvertTime(now, tz).DateTime;
        var next = local.Date.AddDays(1);
        return new DateTimeOffset(next, tz.GetUtcOffset(next));
    }

    /// <summary>Earlier of two optional instants.</summary>
    public static DateTimeOffset? Min(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null ? a : (a < b ? a : b);
}
