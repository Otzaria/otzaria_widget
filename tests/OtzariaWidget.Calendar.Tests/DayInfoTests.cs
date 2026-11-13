using System.Globalization;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.Calendar.Tests;

/// <summary>Hand-checked values for the tricky days (all of them are also covered day-by-day by <see cref="HebrewGoldenTests"/>).</summary>
public class DayInfoTests
{
    private static readonly CalendarService Svc = new();
    private static readonly CityInfo Jerusalem = Svc.FindCity("ירושלים")!;
    private static readonly CityInfo ModiinIllit = Svc.FindCity("מודיעין עילית")!;
    private static readonly CityInfo NewYork = Svc.FindCity("ניו יורק")!;

    private static DateOnly D(string s) => DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static DayInfo Info(string date, CityInfo city) => Svc.GetDayInfo(D(date), city);

    [Fact]
    public void Today_2026_11_13()
    {
        var i = Info("2026-11-13", Jerusalem);
        Assert.Equal("ג׳ כסלו תשפ״ז", i.HebrewDate);
        Assert.Equal("יום שישי", i.HebrewDayOfWeek);
        Assert.Equal("13/11/2026", i.GregorianDate);
        Assert.Equal("תולדות", i.Parasha);
        Assert.Equal("בכורות נו", i.DafYomiBavli);
        Assert.NotNull(i.DafYomiYerushalmi);
        Assert.Null(i.Omer);
        Assert.Empty(i.Holidays);
        Assert.False(i.IsShabbat);
        Assert.False(i.IsYomTov);
        Assert.Equal("16:05", i.CandleLighting!.Value.ToString("HH:mm", CultureInfo.InvariantCulture));
        Assert.Null(i.Havdalah);

        var m = Info("2026-11-13", ModiinIllit);
        Assert.Equal("16:14", m.CandleLighting!.Value.ToString("HH:mm", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Shabbat_has_havdalah_34_minutes_after_sunset_and_no_candle_lighting()
    {
        var i = Info("2026-11-14", Jerusalem);
        Assert.True(i.IsShabbat);
        Assert.Equal("שבת קודש", i.HebrewDayOfWeek);
        Assert.Equal("ד׳ כסלו תשפ״ז", i.HebrewDate);
        Assert.Equal("תולדות", i.Parasha);
        Assert.Empty(i.Holidays); // the parasha is kept in DayInfo.Parasha, not duplicated in Holidays
        Assert.Null(i.CandleLighting);
        var sunset = Svc.GetZmanim(D("2026-11-14"), Jerusalem, ["sunset"]).Single().Time!.Value;
        Assert.Equal(sunset.AddMinutes(34), i.Havdalah);
    }

    [Fact]
    public void Weekday_parasha_is_the_upcoming_shabbat()
    {
        Assert.Equal("תולדות", Info("2026-11-08", Jerusalem).Parasha); // Sunday after Chayei Sarah
        Assert.Equal("חיי שרה", Info("2026-11-07", Jerusalem).Parasha); // Shabbat itself
        Assert.Equal("ויצא", Info("2026-11-15", NewYork).Parasha);
    }

    [Fact]
    public void Rosh_Hashana_and_Erev()
    {
        var erev = Info("2026-09-11", Jerusalem);
        Assert.Equal("כ״ט אלול תשפ״ו", erev.HebrewDate);
        Assert.Equal(["ערב ראש השנה"], erev.Holidays);
        Assert.NotNull(erev.CandleLighting);
        Assert.False(erev.IsYomTov);

        var rh1 = Info("2026-09-12", Jerusalem);
        Assert.Equal("א׳ תשרי תשפ״ז", rh1.HebrewDate);
        Assert.Equal(["ראש השנה"], rh1.Holidays);
        Assert.True(rh1.IsYomTov);
        Assert.True(rh1.IsShabbat);
        Assert.Null(rh1.Havdalah); // next day is Yom Tov

        var rh2 = Info("2026-09-13", NewYork);
        Assert.Equal("ב׳ תשרי תשפ״ז", rh2.HebrewDate);
        Assert.Equal(["ראש השנה"], rh2.Holidays);
        Assert.True(rh2.IsYomTov);
        Assert.NotNull(rh2.Havdalah);
        Assert.Equal("האזינו", rh2.Parasha);
        Assert.Equal(["צום גדליה"], Info("2026-09-14", Jerusalem).Holidays);
    }

    [Fact]
    public void Yom_Kippur()
    {
        Assert.Equal(["ערב יום כיפור"], Info("2026-09-20", Jerusalem).Holidays);
        Assert.NotNull(Info("2026-09-20", Jerusalem).CandleLighting);
        var yk = Info("2026-09-21", Jerusalem);
        Assert.Equal("י׳ תשרי תשפ״ז", yk.HebrewDate);
        Assert.Equal(["יום כיפור"], yk.Holidays);
        Assert.True(yk.IsYomTov);
        Assert.Null(yk.DafYomiYerushalmi); // the Yerushalmi cycle skips Yom Kippur
    }

    [Fact]
    public void Sukkot_and_Shemini_Atzeret_Israel_vs_diaspora()
    {
        // 15 Tishrei 5787 is Shabbat
        Assert.Equal(["סוכות"], Info("2026-09-26", Jerusalem).Holidays);
        Assert.Equal(["א דחוה\"מ סוכות"], Info("2026-09-27", Jerusalem).Holidays);   // Israel: chol hamoed already
        Assert.Equal(["סוכות"], Info("2026-09-27", NewYork).Holidays);                // diaspora: second day yom tov
        Assert.True(Info("2026-09-27", NewYork).IsYomTov);
        Assert.False(Info("2026-09-27", Jerusalem).IsYomTov);
        // Hoshana Rabba: 21 Tishrei, "ו׳ דחוה"מ" in Israel (6th day), "ה׳" in the diaspora (5th day)
        Assert.Equal(["הושענא רבה", "ו' דחוה\"מ"], Info("2026-10-02", Jerusalem).Holidays);
        Assert.Equal(["הושענא רבה", "ה' דחוה\"מ"], Info("2026-10-02", NewYork).Holidays);
        // Shemini Atzeret / Simchat Torah
        Assert.Equal(["שמיני עצרת", "שמחת תורה"], Info("2026-10-03", Jerusalem).Holidays);
        Assert.Equal(["שמיני עצרת"], Info("2026-10-03", NewYork).Holidays);
        Assert.Empty(Info("2026-10-04", Jerusalem).Holidays);
        Assert.Equal(["שמחת תורה"], Info("2026-10-04", NewYork).Holidays);
        Assert.True(Info("2026-10-04", NewYork).IsYomTov);
        Assert.Equal("בראשית", Info("2026-10-04", Jerusalem).Parasha);
    }

    [Fact]
    public void Chanukah_across_Rosh_Chodesh_Tevet()
    {
        Assert.Equal(["נר א דחנוכה"], Info("2026-12-05", Jerusalem).Holidays);   // 25 Kislev
        Assert.Equal(["נר ב דחנוכה"], Info("2026-12-06", Jerusalem).Holidays);
        Assert.Equal(["ר\"ח", "נר ו דחנוכה"], Info("2026-12-10", Jerusalem).Holidays); // 30 Kislev is Rosh Chodesh
        Assert.Equal(["ר\"ח", "נר ז דחנוכה"], Info("2026-12-11", Jerusalem).Holidays); // 1 Tevet
        Assert.Equal(["נר ח דחנוכה"], Info("2026-12-12", Jerusalem).Holidays);          // 2 Tevet
        Assert.Empty(Info("2026-12-13", Jerusalem).Holidays);
        Assert.Equal("א׳ טבת תשפ״ז", Info("2026-12-11", Jerusalem).HebrewDate);
        Assert.Equal(["עשרה בטבת"], Info("2026-12-20", NewYork).Holidays);
    }

    [Fact]
    public void Purim_and_Shushan_Purim_in_leap_year()
    {
        Assert.Equal("י״ג אדר ב׳ תשפ״ז", Info("2027-03-22", Jerusalem).HebrewDate);
        Assert.Equal(["תענית אסתר"], Info("2027-03-22", Jerusalem).Holidays);
        Assert.Equal(["פורים"], Info("2027-03-23", Jerusalem).Holidays);
        Assert.Equal(["פורים שושן"], Info("2027-03-24", Jerusalem).Holidays);   // Otzaria's (kosher_dart) spelling
        Assert.Equal("כ״ז אדר א׳ תשפ״ז", Info("2027-03-06", Jerusalem).HebrewDate);
        // Purim Katan in the leap year's Adar I
        Assert.Equal(["פורים קטן"], Info("2027-02-21", Jerusalem).Holidays);
        Assert.Equal(["פורים שושן קטן"], Info("2027-02-22", Jerusalem).Holidays);
    }

    [Fact]
    public void Pesach_chol_hamoed_and_seventh_day()
    {
        Assert.Equal(["ערב פסח"], Info("2026-04-01", Jerusalem).Holidays);
        Assert.Equal(["פסח"], Info("2026-04-02", Jerusalem).Holidays);
        Assert.Equal(["א דחוה\"מ פסח"], Info("2026-04-03", Jerusalem).Holidays);
        Assert.Equal(["פסח"], Info("2026-04-03", NewYork).Holidays);             // diaspora second day
        Assert.Equal(["ב דחוה\"מ פסח"], Info("2026-04-04", Jerusalem).Holidays); // Shabbat chol hamoed
        Assert.Equal(["שביעי של פסח"], Info("2026-04-08", Jerusalem).Holidays);
        Assert.Equal(["שביעי של פסח"], Info("2026-04-08", NewYork).Holidays);
        Assert.Empty(Info("2026-04-09", Jerusalem).Holidays);                    // Israel: Isru Chag is filtered out
        Assert.Equal(["פסח"], Info("2026-04-09", NewYork).Holidays);             // diaspora: last day
        Assert.Equal("היום יום אחד בעומר", Info("2026-04-03", Jerusalem).Omer);
        Assert.Equal("היום שבעה ימים שהם שבוע אחד בעומר", Info("2026-04-09", Jerusalem).Omer);
    }

    [Fact]
    public void Shavuot_and_Lag_Baomer()
    {
        Assert.Equal(["שבועות"], Info("2026-05-22", Jerusalem).Holidays);
        Assert.Empty(Info("2026-05-23", Jerusalem).Holidays);
        Assert.Equal(["שבועות"], Info("2026-05-23", NewYork).Holidays);
        Assert.Equal(["ל״ג בעומר"], Info("2026-05-05", Jerusalem).Holidays);
        Assert.Equal("היום שלשה ושלשים יום שהם ארבעה שבועות וחמשה ימים בעומר", Info("2026-05-05", Jerusalem).Omer);
        Assert.Null(Info("2026-05-22", Jerusalem).Omer); // day 50 is Shavuot itself
    }

    [Fact]
    public void Fasts_17_Tammuz_and_9_Av_including_postponement_from_Shabbat()
    {
        Assert.Equal(["שבעה עשר בתמוז"], Info("2026-07-02", Jerusalem).Holidays);
        Assert.Equal(["תשעה באב"], Info("2026-07-23", Jerusalem).Holidays);
        Assert.Null(Info("2026-07-23", Jerusalem).DafYomiYerushalmi); // Yerushalmi skips Tisha B'Av
        // 17 Tammuz 5789 falls on Shabbat 2029-06-30 -> the fast is Sunday 18 Tammuz
        Assert.Empty(Info("2029-06-30", Jerusalem).Holidays);
        Assert.Equal(["שבעה עשר בתמוז"], Info("2029-07-01", Jerusalem).Holidays);
        Assert.Equal("י״ח תמוז תשפ״ט", Info("2029-07-01", Jerusalem).HebrewDate);
        // 9 Av 5785 falls on Shabbat 2025-08-02 -> the fast is Sunday 10 Av
        Assert.Empty(Info("2025-08-02", Jerusalem).Holidays);
        Assert.Equal(["תשעה באב"], Info("2025-08-03", Jerusalem).Holidays);
        var fastEnd = Svc.GetZmanim(D("2025-08-03"), Jerusalem, ["fastEndTikLenient", "fastEndItimStringent"]);
        Assert.Equal(2, fastEnd.Count);
        Assert.Empty(Svc.GetZmanim(D("2025-08-02"), Jerusalem, ["fastEndTikLenient"]));
    }

    [Fact]
    public void Rosh_Chodesh_label_and_double_day()
    {
        Assert.Contains("ר\"ח", Info("2026-12-11", Jerusalem).Holidays);
        Assert.Contains("ר\"ח", Info("2026-12-10", Jerusalem).Holidays); // 30 Kislev
    }

    [Fact]
    public void Double_parashiyot_leap_and_non_leap_years_are_space_joined_like_otzaria()
    {
        // 5786 (non-leap): Vayakhel-Pekudei = 2026-03-14 (Shabbat), Behar-Bechukotai 2026-05-09 (Israel & diaspora)
        Assert.Equal("ויקהל פקודי", Info("2026-03-14", Jerusalem).Parasha);
        Assert.Equal("בהר בחקתי", Info("2026-05-09", NewYork).Parasha);
        // 5787 (leap): Vayakhel and Pekudei are separate Shabbatot (2027-03-06, 2027-03-13)
        Assert.Equal("ויקהל", Info("2027-03-06", Jerusalem).Parasha);
        Assert.Equal("פקודי", Info("2027-03-13", Jerusalem).Parasha);
        // Israel vs diaspora after a Shabbat yom tov (Pesach 5786 ends Thursday in Israel, Friday in the diaspora -> parsha shifts)
        Assert.Equal("מטות מסעי", Info("2026-07-11", Jerusalem).Parasha);
        Assert.Equal("ניצבים וילך", Info("2026-09-05", Jerusalem).Parasha); // Otzaria spelling (kosher_dart), not KosherJava's נצבים
        Assert.Equal("ניצבים", Info("2025-09-20", Jerusalem).Parasha);
    }

    [Fact]
    public void Shabbat_that_is_a_yom_tov_has_no_regular_parasha_so_the_upcoming_parasha_is_null_on_the_days_before()
    {
        Assert.Null(Info("2026-10-02", Jerusalem).Parasha); // upcoming Shabbat = Shemini Atzeret in Israel
        Assert.Null(Info("2026-09-26", Jerusalem).Parasha); // Shabbat Sukkot
    }

    [Fact]
    public void Daf_yomi_before_the_first_cycle_is_null_and_does_not_throw()
    {
        var i = Svc.GetDayInfo(new DateOnly(1900, 1, 1), Jerusalem);
        Assert.Null(i.DafYomiBavli);
        Assert.False(string.IsNullOrEmpty(i.HebrewDate));
    }

    [Fact]
    public void Candle_lighting_follows_city_minutes()
    {
        var friday = D("2026-11-13");
        foreach (var city in new[] { Jerusalem, ModiinIllit, NewYork, Svc.FindCity("תל אביב")!, Svc.FindCity("בני ברק")! })
        {
            var sunset = Svc.GetZmanim(friday, city, ["sunset"]).Single().Time!.Value;
            Assert.Equal(sunset.AddMinutes(-city.CandleLightingMinutes), Svc.GetDayInfo(friday, city).CandleLighting);
        }
    }
}
