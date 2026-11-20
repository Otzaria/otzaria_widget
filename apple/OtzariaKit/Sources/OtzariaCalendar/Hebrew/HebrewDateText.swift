import Foundation
import KosherSwift

/// Hebrew date / weekday / Gregorian formatting as Otzaria's calendar shows it.
public enum HebrewDateText {
    /// Otzaria calendar-screen month names (kHebrewMonths), index = Nissan-first month - 1.
    /// "סיון"/"חשון" (the calendar header spelling), not the library chip's "סיוון"/"חשוון".
    private static let months = ["ניסן", "אייר", "סיון", "תמוז", "אב", "אלול", "תשרי", "חשון", "כסלו", "טבת", "שבט", "אדר"]

    /// Month name for a Nissan-first month number (1...13) with Otzaria's leap-year override
    /// (month 12 = "אדר א׳", month 13 = "אדר ב׳").
    public static func monthName(_ month: Int, leapYear: Bool) -> String {
        if leapYear && month == 12 { return "אדר א\u{05F3}" }
        if leapYear && month == 13 { return "אדר ב\u{05F3}" }
        return months[min(max(month - 1, 0), months.count - 1)]
    }

    struct Parts {
        let year: Int
        let month: Int // Nissan-first, 1...13
        let day: Int
        let leap: Bool

        init(_ date: CivilDate) {
            let h = HebrewDate(epochDay: date.epochDay)
            year = h.year
            month = h.month
            day = h.day
            leap = h.isLeapYear
        }
    }

    /// "ג׳ כסלו תשפ״ז": gematria day with geresh/gershayim, Otzaria's header month spelling, year without the thousands.
    static func format(_ p: Parts) -> String {
        "\(HebrewNumbers.format(p.day)) \(monthName(p.month, leapYear: p.leap)) \(HebrewNumbers.year(p.year))"
    }

    /// Otzaria's calendar-header variant: day WITHOUT geresh (ג, טו, כה) and year with the thousands marker (ה׳תשפ״ז).
    static func formatCalendarHeader(_ p: Parts) -> String {
        let rest = HebrewNumbers.format(p.year)
        let y = p.year / 1000 == 5 ? "ה\u{05F3}" + rest : rest
        return "\(HebrewNumbers.letters(p.day)) \(monthName(p.month, leapYear: p.leap)) \(y)"
    }

    /// "יום ראשון" ... "יום שישי", "שבת קודש" (1 = Sunday ... 7 = Shabbat).
    public static func dayOfWeekName(_ dayOfWeek: Int) -> String {
        switch dayOfWeek {
        case 1: return "יום ראשון"
        case 2: return "יום שני"
        case 3: return "יום שלישי"
        case 4: return "יום רביעי"
        case 5: return "יום חמישי"
        case 6: return "יום שישי"
        case 7: return "שבת קודש"
        default: return ""
        }
    }

    /// "dd/MM/yyyy" (Gregorian, independent of the device locale and calendar).
    public static func gregorian(_ date: CivilDate) -> String {
        String(format: "%02d/%02d/%04d", date.day, date.month, date.year)
    }
}
