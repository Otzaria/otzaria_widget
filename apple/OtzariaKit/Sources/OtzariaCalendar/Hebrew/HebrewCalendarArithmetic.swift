import Foundation
import KosherSwift

/// A Hebrew date. `month` follows KosherJava: Nissan = 1 ... Elul = 6, Tishrei = 7 ... Shevat = 11, Adar (Adar I in a leap year) = 12,
/// Adar II = 13.
struct HebrewDate: Equatable {
    let year: Int
    let month: Int
    let day: Int

    var isLeapYear: Bool { HebrewCalendarArithmetic.isLeapYear(year) }

    init(year: Int, month: Int, day: Int) {
        self.year = year
        self.month = month
        self.day = day
    }

    /// The Hebrew date of a Gregorian day.
    init(epochDay: Int) {
        let year = HebrewCalendarArithmetic.year(containing: epochDay)
        var rest = epochDay - HebrewCalendarArithmetic.roshHashana(year)
        for month in HebrewCalendarArithmetic.monthOrder(year) {
            let length = HebrewCalendarArithmetic.daysInMonth(year: year, month: month)
            if rest < length {
                self.init(year: year, month: month, day: rest + 1)
                return
            }
            rest -= length
        }
        self.init(year: year, month: 6, day: 29) // unreachable: the months add up to the year length
    }

    /// Days since 1970-01-01 (the `CivilDate.epochDay` scale).
    var epochDay: Int {
        var days = HebrewCalendarArithmetic.roshHashana(year) + day - 1
        for m in HebrewCalendarArithmetic.monthOrder(year) {
            if m == month { break }
            days += HebrewCalendarArithmetic.daysInMonth(year: year, month: m)
        }
        return days
    }
}

/// The Hebrew calendar by pure arithmetic (mean molad and the four dechiyos), so results never depend on the OS's ICU.
/// KosherSwift converts dates through Foundation's Hebrew calendar, which is wrong for Rosh Hashana 5807 on older systems
/// (macOS 14/15, iOS 17/18). It is still used for the holiday and parsha rules, but only on a date this file has confirmed it converts correctly.
enum HebrewCalendarArithmetic {
    private static let chalakimPerDay = 25_920
    private static let chalakimPerMonth = 765_433
    private static let chalakimAtMoladTohu = 31_524
    /// KosherJava's absolute day number (1 Jan 0001 = 1) of the day before the Hebrew epoch, and the offset to `CivilDate.epochDay`.
    private static let jewishEpochAbsDay = -1_373_429
    private static let absDayOfUnixEpoch = 719_163

    static func isLeapYear(_ year: Int) -> Bool { (7 * year + 1) % 19 < 7 }

    private static func monthsBeforeYear(_ year: Int) -> Int {
        235 * ((year - 1) / 19) + 12 * ((year - 1) % 19) + (7 * ((year - 1) % 19) + 1) / 19
    }

    /// Days from the Sunday before the molad of Tishrei of year 1 to Rosh Hashana of `year`, dechiyos applied.
    static func elapsedDays(_ year: Int) -> Int {
        let chalakim = chalakimAtMoladTohu + chalakimPerMonth * monthsBeforeYear(year)
        let moladDay = chalakim / chalakimPerDay
        let moladParts = chalakim % chalakimPerDay
        var day = moladDay
        if moladParts >= 19_440 // molad zaken
            || (moladDay % 7 == 2 && moladParts >= 9_924 && !isLeapYear(year)) // GaTRaD
            || (moladDay % 7 == 1 && moladParts >= 16_789 && isLeapYear(year - 1)) { // BeTuTaKFoT
            day += 1
        }
        if [0, 3, 5].contains(day % 7) { day += 1 } // lo ADU rosh
        return day
    }

    static func roshHashana(_ year: Int) -> Int { elapsedDays(year) + 1 + jewishEpochAbsDay - absDayOfUnixEpoch }

    static func daysInYear(_ year: Int) -> Int { elapsedDays(year + 1) - elapsedDays(year) }

    /// The hebrew year whose Rosh Hashana is the latest one on or before the day.
    static func year(containing epochDay: Int) -> Int {
        var year = 1 + Int(Double(epochDay - roshHashana(1)) / 365.24682)
        while roshHashana(year) > epochDay { year -= 1 }
        while roshHashana(year + 1) <= epochDay { year += 1 }
        return year
    }

    /// The year's months in calendar order, in KosherJava numbering.
    static func monthOrder(_ year: Int) -> [Int] {
        isLeapYear(year) ? [7, 8, 9, 10, 11, 12, 13, 1, 2, 3, 4, 5, 6] : [7, 8, 9, 10, 11, 12, 1, 2, 3, 4, 5, 6]
    }

    static func daysInMonth(year: Int, month: Int) -> Int {
        switch month {
        case 8: return daysInYear(year) % 10 == 5 ? 30 : 29 // Cheshvan
        case 9: return daysInYear(year) % 10 == 3 ? 29 : 30 // Kislev
        case 12: return isLeapYear(year) ? 30 : 29
        case 2, 4, 6, 10, 13: return 29
        default: return 30
        }
    }

    /// The molad of the month as KosherJava's `getMolad()` reports it: the civil day, and the hour (Jerusalem mean time
    /// as a 24-hour clock whose day starts at 18:00 the evening before), minute and chalakim.
    static func molad(year: Int, month: Int) -> (day: CivilDate, hours: Int, minutes: Int, chalakim: Int) {
        let index = monthOrder(year).firstIndex(of: month) ?? 0
        let total = chalakimAtMoladTohu + chalakimPerMonth * (monthsBeforeYear(year) + index)
        var parts = total % chalakimPerDay
        var hours = parts / 1080
        parts -= hours * 1080
        let minutes = parts / 18
        var epochDay = total / chalakimPerDay + jewishEpochAbsDay - absDayOfUnixEpoch
        if hours >= 6 { epochDay += 1 }
        hours = (hours + 18) % 24
        return (CivilDate(epochDay: epochDay), hours, minutes, parts - minutes * 18)
    }

    // MARK: KosherSwift stand-in

    /// Whether Foundation's Hebrew calendar is trusted for this Gregorian day. Tests replace it to simulate a faulty OS.
    static var osCalendarIsCorrect: (CivilDate) -> Bool = { _ in true }

    /// A `JewishCalendar` whose holiday, parsha and omer rules answer for `date`. KosherSwift reads the date through the
    /// OS's calendar; when that disagrees with the arithmetic (on `date` or the next day) the rules are run on the same day of
    /// another year with the same Rosh Hashana weekday and length, where the OS is verified to be right. Every rule used
    /// depends only on that shape and the day, never on the year number.
    static func jewishCalendar(for date: CivilDate, inIsrael: Bool) -> JewishCalendar {
        let target = HebrewDate(epochDay: date.epochDay)
        if let calendar = verifiedCalendar(epochDay: date.epochDay, inIsrael: inIsrael) { return calendar }
        let rosh = roshHashana(target.year)
        let offset = date.epochDay - rosh
        let shape = (CivilDate(epochDay: rosh).dayOfWeek, daysInYear(target.year))
        for distance in 1...600 {
            for year in [target.year - distance, target.year + distance] where year > 0 {
                let candidate = roshHashana(year)
                guard CivilDate(epochDay: candidate).dayOfWeek == shape.0, daysInYear(year) == shape.1 else { continue }
                if let calendar = verifiedCalendar(epochDay: candidate + offset, inIsrael: inIsrael) { return calendar }
            }
        }
        return makeCalendar(epochDay: date.epochDay, inIsrael: inIsrael)
    }

    private static func makeCalendar(epochDay: Int, inIsrael: Bool) -> JewishCalendar {
        let d = CivilDate(epochDay: epochDay)
        return JewishCalendar(localDate: LocalDate(year: d.year, month: d.month, day: d.day), inIsrael: inIsrael)
    }

    /// The calendar for the day if the OS converts it and the next day like the arithmetic does.
    private static func verifiedCalendar(epochDay: Int, inIsrael: Bool) -> JewishCalendar? {
        guard osCalendarIsCorrect(CivilDate(epochDay: epochDay)), osCalendarIsCorrect(CivilDate(epochDay: epochDay + 1)) else { return nil }
        let calendar = makeCalendar(epochDay: epochDay, inIsrael: inIsrael)
        let tomorrow = JewishCalendar(copy: calendar)
        tomorrow.forward()
        guard agrees(calendar, HebrewDate(epochDay: epochDay)), agrees(tomorrow, HebrewDate(epochDay: epochDay + 1)) else { return nil }
        return calendar
    }

    private static func agrees(_ calendar: JewishCalendar, _ date: HebrewDate) -> Bool {
        // KosherSwift numbers the Adar of a common year as Adar II (13)
        let month = calendar.getNissanStartingJewishMonth()
        return calendar.getJewishYear() == date.year && (date.isLeapYear ? month : min(month, 12)) == date.month
            && calendar.getJewishDayOfMonth() == date.day
    }
}
