using OtzariaWidget.Calendar.Hebrew;
using OtzariaWidget.Calendar.Zmanim;

namespace OtzariaWidget.Calendar.Tests;

public class HebrewTextTests
{
    [Theory]
    [InlineData(1, "א׳")]
    [InlineData(3, "ג׳")]
    [InlineData(9, "ט׳")]
    [InlineData(10, "י׳")]
    [InlineData(11, "י״א")]
    [InlineData(14, "י״ד")]
    [InlineData(15, "ט״ו")]
    [InlineData(16, "ט״ז")]
    [InlineData(17, "י״ז")]
    [InlineData(20, "כ׳")]
    [InlineData(25, "כ״ה")]
    [InlineData(29, "כ״ט")]
    [InlineData(30, "ל׳")]
    [InlineData(56, "נ״ו")]
    [InlineData(134, "קל״ד")]
    [InlineData(5787, "תשפ״ז")]   // thousands dropped, like Otzaria's date chip
    [InlineData(5786, "תשפ״ו")]
    [InlineData(5790, "תש״צ")]
    [InlineData(5789, "תשפ״ט")]
    [InlineData(5700, "ת״ש")]
    [InlineData(5515, "תקט״ו")]
    public void Gematria_with_geresh_and_gershayim(int n, string expected)
        => Assert.Equal(expected, HebrewNumbers.Format(n));

    [Fact]
    public void Uses_hebrew_punctuation_characters_not_ascii()
    {
        var s = HebrewNumbers.Format(15) + HebrewNumbers.Format(3);
        Assert.DoesNotContain('"', s);
        Assert.DoesNotContain('\'', s);
        Assert.Contains('״', s); // gershayim
        Assert.Contains('׳', s); // geresh
    }

    [Theory]
    [InlineData(56, "נו")]
    [InlineData(15, "טו")]
    [InlineData(134, "קלד")]
    [InlineData(3, "ג")]
    public void Letters_only(int n, string expected) => Assert.Equal(expected, HebrewNumbers.Letters(n));

    [Theory]
    [InlineData("תשפ״ז", 787)]
    [InlineData("ט״ו", 15)]
    [InlineData("קלד", 134)]
    [InlineData("ג׳", 3)]
    [InlineData("xyz", 0)]
    public void Parse_round_trip(string text, int expected) => Assert.Equal(expected, HebrewNumbers.Parse(text));

    [Fact]
    public void Gematria_round_trips_for_1_to_999()
    {
        for (var n = 1; n < 1000; n++)
            Assert.Equal(n, HebrewNumbers.Parse(HebrewNumbers.Format(n)));
    }

    [Theory]
    [InlineData(1, "היום יום אחד בעומר")]
    [InlineData(2, "היום שני ימים בעומר")]
    [InlineData(6, "היום ששה ימים בעומר")]
    [InlineData(7, "היום שבעה ימים שהם שבוע אחד בעומר")]
    [InlineData(8, "היום שמונה ימים שהם שבוע אחד ויום אחד בעומר")]
    [InlineData(14, "היום ארבעה עשר יום שהם שני שבועות בעומר")]
    [InlineData(19, "היום תשעה עשר יום שהם שני שבועות וחמשה ימים בעומר")]
    [InlineData(20, "היום עשרים יום שהם שני שבועות וששה ימים בעומר")]
    [InlineData(33, "היום שלשה ושלשים יום שהם ארבעה שבועות וחמשה ימים בעומר")]
    [InlineData(42, "היום שנים וארבעים יום שהם ששה שבועות בעומר")]
    [InlineData(49, "היום תשעה וארבעים יום שהם שבעה שבועות בעומר")]
    public void Omer_text_matches_otzaria(int day, string expected) => Assert.Equal(expected, OmerText.Build(day));

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(-1)]
    public void Omer_outside_range_is_null(int day) => Assert.Null(OmerText.Build(day));

    [Fact]
    public void Omer_text_is_unique_for_every_day()
        => Assert.Equal(49, Enumerable.Range(1, 49).Select(OmerText.Build).Distinct().Count());

    [Fact]
    public void Header_month_names_use_the_calendar_screen_spelling_and_leap_adar()
    {
        Assert.Equal("חשון", HebrewDateText.MonthName(8, false));
        Assert.Equal("סיון", HebrewDateText.MonthName(3, false));
        Assert.Equal("אדר", HebrewDateText.MonthName(12, false));
        Assert.Equal("אדר א׳", HebrewDateText.MonthName(12, true));
        Assert.Equal("אדר ב׳", HebrewDateText.MonthName(13, true));
        Assert.Equal("תשרי", HebrewDateText.MonthName(7, false));
    }

    [Fact]
    public void Calendar_header_variant_and_day_names()
    {
        var svc = new CalendarService();
        var jer = svc.FindCity("ירושלים")!;
        Assert.Equal("ג כסלו ה׳תשפ״ז", svc.GetCalendarHeaderDate(new DateOnly(2026, 11, 13), jer));
        Assert.Equal("טו תשרי ה׳תשפ״ז", svc.GetCalendarHeaderDate(new DateOnly(2026, 9, 26), jer));
        Assert.Equal("יום ראשון", HebrewDateText.DayOfWeekName(1));
        Assert.Equal("יום שישי", HebrewDateText.DayOfWeekName(6));
        Assert.Equal("שבת קודש", HebrewDateText.DayOfWeekName(7));
    }

    [Fact]
    public void Gregorian_date_is_dd_MM_yyyy_regardless_of_the_machine_culture()
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("he-IL");
            Assert.Equal("13/11/2026", HebrewDateText.Gregorian(new DateOnly(2026, 11, 13)));
            Assert.Equal("01/03/2027", HebrewDateText.Gregorian(new DateOnly(2027, 3, 1)));
            Assert.Equal("13/11/2026", new CalendarService().GetDayInfo(new DateOnly(2026, 11, 13), new CalendarService().Cities[0]).GregorianDate);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void Zman_time_format_truncates_and_marks_seconds()
    {
        var off = TimeSpan.FromHours(2);
        // 16:05:32 -> truncated minute, ':' marker (seconds 30-59)
        Assert.Equal("⁦16:05:⁩", ZmanTimeFormatter.Format(new DateTimeOffset(2026, 11, 13, 16, 5, 32, off)));
        // 16:05:29 -> '.' marker; the minute is never rounded up
        Assert.Equal("⁦16:05.⁩", ZmanTimeFormatter.Format(new DateTimeOffset(2026, 11, 13, 16, 5, 29, off)));
        Assert.Equal("⁦16:05:⁩", ZmanTimeFormatter.Format(new DateTimeOffset(2026, 11, 13, 16, 5, 59, 900, off)));
        Assert.Equal("⁦00:00.⁩", ZmanTimeFormatter.Format(new DateTimeOffset(2026, 11, 13, 0, 0, 0, off)));
        Assert.Equal("16:05", ZmanTimeFormatter.FormatPlain(new DateTimeOffset(2026, 11, 13, 16, 5, 59, off)));
    }

    [Fact]
    public void Zman_time_format_12_hour()
    {
        var off = TimeSpan.FromHours(2);
        Assert.Equal("⁦4:05:‎ PM⁩", ZmanTimeFormatter.Format(new DateTimeOffset(2026, 11, 13, 16, 5, 32, off), twelveHour: true));
        Assert.Equal("⁦12:30.‎ AM⁩", ZmanTimeFormatter.Format(new DateTimeOffset(2026, 11, 13, 0, 30, 0, off), twelveHour: true));
        Assert.Equal("⁦12:00.‎ PM⁩", ZmanTimeFormatter.Format(new DateTimeOffset(2026, 11, 13, 12, 0, 0, off), twelveHour: true));
        Assert.Equal("⁦11:59.‎ AM⁩", ZmanTimeFormatter.Format(new DateTimeOffset(2026, 11, 13, 11, 59, 0, off), twelveHour: true));
    }
}
