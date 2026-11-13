using System.Globalization;
using Kosher.Zmanim.HebrewCalendar;

namespace OtzariaWidget.Calendar.Hebrew;

/// <summary>Hebrew date / weekday / Gregorian formatting as Otzaria's calendar shows it.</summary>
public static class HebrewDateText
{
    /// <summary>Otzaria calendar-screen month names (kHebrewMonths, calendar_date_helpers.dart:14-27), index = Nissan-first month - 1.
    /// NOTE: "סיון"/"חשון" (the calendar header spelling), not the library chip's "סיוון"/"חשוון".</summary>
    private static readonly string[] Months =
        ["ניסן", "אייר", "סיון", "תמוז", "אב", "אלול", "תשרי", "חשון", "כסלו", "טבת", "שבט", "אדר"];

    /// <summary>Month name for a Nissan-first month number (1..13), with Otzaria's leap-year override
    /// (month 12 = "אדר א׳", month 13 = "אדר ב׳"; getHebrewMonthNameFor).</summary>
    public static string MonthName(int month, bool leapYear)
    {
        if (leapYear && month == 12) return "אדר א׳";
        if (leapYear && month == 13) return "אדר ב׳";
        return Months[Math.Clamp(month - 1, 0, Months.Length - 1)];
    }

    /// <summary>"ג׳ כסלו תשפ״ז": gematria day with geresh/gershayim (ט״ו, ט״ז), Otzaria's header month spelling, year without the thousands.</summary>
    public static string Format(JewishDate date)
        => $"{HebrewNumbers.Format(date.JewishDayOfMonth)} {MonthName(date.JewishMonth, date.IsJewishLeapYear())} {HebrewNumbers.Year(date.JewishYear)}";

    /// <summary>Otzaria's calendar-header variant: day WITHOUT geresh (ג, טו, כה) and year with the thousands marker (ה׳תשפ״ז).</summary>
    public static string FormatCalendarHeader(JewishDate date)
    {
        var year = date.JewishYear;
        var rest = HebrewNumbers.Format(year); // already without thousands
        var y = year / 1000 == 5 ? "ה׳" + rest : rest;
        return $"{HebrewNumbers.Letters(date.JewishDayOfMonth)} {MonthName(date.JewishMonth, date.IsJewishLeapYear())} {y}";
    }

    /// <summary>"יום ראשון" .. "יום שישי", "שבת קודש" (dayOfWeek 1 = Sunday .. 7 = Shabbat).</summary>
    public static string DayOfWeekName(int dayOfWeek) => dayOfWeek switch
    {
        1 => "יום ראשון",
        2 => "יום שני",
        3 => "יום שלישי",
        4 => "יום רביעי",
        5 => "יום חמישי",
        6 => "יום שישי",
        7 => "שבת קודש",
        _ => "",
    };

    /// <summary>"dd/MM/yyyy" with the Gregorian calendar and invariant culture (the machine's he-IL culture would use the Hebrew calendar).</summary>
    public static string Gregorian(DateOnly date)
        => date.ToDateTime(TimeOnly.MinValue).ToString("dd/MM/yyyy", GregorianInvariant);

    private static readonly CultureInfo GregorianInvariant = CreateInvariantGregorian();

    private static CultureInfo CreateInvariantGregorian()
    {
        var ci = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        ci.DateTimeFormat.Calendar = new GregorianCalendar();
        return ci;
    }
}
