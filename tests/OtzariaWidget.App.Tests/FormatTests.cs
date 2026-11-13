using System.Globalization;
using OtzariaWidget.App.Provider.Cards;

namespace OtzariaWidget.App.Tests;

public class FormatTests
{
    [Fact]
    public void Time_24h_and_12h()
    {
        Assert.Equal("04:05", CardFormat.Time(new DateTime(2026, 11, 13, 4, 5, 0), false));
        Assert.Equal("17:15", CardFormat.Time(new DateTime(2026, 11, 13, 17, 15, 0), false));
        Assert.Equal("4:05 AM", CardFormat.Time(new DateTime(2026, 11, 13, 4, 5, 0), true));
        Assert.Equal("5:15 PM", CardFormat.Time(new DateTime(2026, 11, 13, 17, 15, 0), true));
        Assert.Equal("12:00 AM", CardFormat.Time(new DateTime(2026, 11, 13, 0, 0, 0), true));
        Assert.Equal("12:30 PM", CardFormat.Time(new DateTime(2026, 11, 13, 12, 30, 0), true));
    }

    [Fact]
    public void Formatting_ignores_machine_culture_even_with_hebrew_calendar()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            var he = new CultureInfo("he-IL");
            CultureInfo.CurrentCulture = he;   // the real machine: Hebrew calendar as optional/default calendar
            Assert.Equal("13 בנובמבר 2026", CardFormat.GregorianLong(new DateOnly(2026, 11, 13)));
            Assert.Equal("13 נוב׳", CardFormat.GregorianShort(new DateOnly(2026, 11, 13)));
            Assert.Equal("יום שישי", CardFormat.Weekday(new DateOnly(2026, 11, 13)));
            Assert.Equal("שבת", CardFormat.Weekday(new DateOnly(2026, 11, 14)));
            Assert.Equal("09:07", CardFormat.Time(new DateTime(2026, 11, 13, 9, 7, 0), false));
            Assert.Equal("45%", CardFormat.Percent(0.449));
            Assert.Equal("100%", CardFormat.Percent(1.7));
            Assert.Equal("0%", CardFormat.Percent(-3));
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void Time_converts_to_city_zone()
    {
        var utc = new DateTimeOffset(2026, 11, 13, 14, 37, 0, TimeSpan.Zero);
        Assert.Equal("16:37", CardFormat.Time(utc, "Asia/Jerusalem", false));
        Assert.Equal("09:37", CardFormat.Time(utc, "America/New_York", false));
        // unknown zone => the instant's own wall clock
        Assert.Equal("14:37", CardFormat.Time(utc, "Not/AZone", false));
    }

    [Fact]
    public void RelativeDay_buckets()
    {
        var now = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 11, 13)));
        Assert.Equal("היום", CardFormat.RelativeDay(now.AddHours(-2), now));
        Assert.Equal("אתמול", CardFormat.RelativeDay(now.AddDays(-1), now));
        Assert.Equal("לפני 3 ימים", CardFormat.RelativeDay(now.AddDays(-3), now));
        Assert.Equal("1 נוב׳", CardFormat.RelativeDay(now.AddDays(-12), now));
        Assert.Equal("", CardFormat.RelativeDay(null, now));
        Assert.Equal("", CardFormat.RelativeDay(now.AddDays(2), now));
    }

    [Fact]
    public void Clip_prefers_word_boundaries()
    {
        Assert.Equal("שולחן ערוך…", CardFormat.Clip("שולחן ערוך אורח חיים הלכות תפילה", 14));
        Assert.Equal("קצר", CardFormat.Clip("  קצר ", 10));
        Assert.Equal("", CardFormat.Clip(null, 10));
    }
}
