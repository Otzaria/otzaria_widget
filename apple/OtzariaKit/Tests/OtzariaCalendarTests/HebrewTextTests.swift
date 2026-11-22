import XCTest
@testable import OtzariaCalendar

final class HebrewTextTests: XCTestCase {
    func testGematriaWithGereshAndGershayim() {
        let cases: [(Int, String)] = [
            (1, "א׳"), (3, "ג׳"), (9, "ט׳"), (10, "י׳"), (11, "י״א"), (14, "י״ד"), (15, "ט״ו"), (16, "ט״ז"), (17, "י״ז"),
            (20, "כ׳"), (25, "כ״ה"), (29, "כ״ט"), (30, "ל׳"), (56, "נ״ו"), (134, "קל״ד"),
            (5787, "תשפ״ז"), (5786, "תשפ״ו"), (5790, "תש״צ"), (5789, "תשפ״ט"), (5700, "ת״ש"), (5515, "תקט״ו"),
        ]
        for (n, expected) in cases { XCTAssertEqual(HebrewNumbers.format(n), expected, "\(n)") }
    }

    func testUsesHebrewPunctuationCharactersNotAscii() {
        let s = HebrewNumbers.format(15) + HebrewNumbers.format(3)
        XCTAssertFalse(s.contains("\""))
        XCTAssertFalse(s.contains("'"))
        XCTAssertTrue(s.contains("\u{05F4}"))
        XCTAssertTrue(s.contains("\u{05F3}"))
    }

    func testLettersOnly() {
        for (n, expected) in [(56, "נו"), (15, "טו"), (134, "קלד"), (3, "ג")] { XCTAssertEqual(HebrewNumbers.letters(n), expected) }
    }

    func testParseRoundTrip() {
        for (text, expected) in [("תשפ״ז", 787), ("ט״ו", 15), ("קלד", 134), ("ג׳", 3), ("xyz", 0)] {
            XCTAssertEqual(HebrewNumbers.parse(text), expected, text)
        }
    }

    func testGematriaRoundTripsFor1To999() {
        for n in 1..<1000 { XCTAssertEqual(HebrewNumbers.parse(HebrewNumbers.format(n)), n) }
    }

    func testOmerTextMatchesOtzaria() {
        let cases: [(Int, String)] = [
            (1, "היום יום אחד בעומר"), (2, "היום שני ימים בעומר"), (6, "היום ששה ימים בעומר"),
            (7, "היום שבעה ימים שהם שבוע אחד בעומר"), (8, "היום שמונה ימים שהם שבוע אחד ויום אחד בעומר"),
            (14, "היום ארבעה עשר יום שהם שני שבועות בעומר"), (19, "היום תשעה עשר יום שהם שני שבועות וחמשה ימים בעומר"),
            (20, "היום עשרים יום שהם שני שבועות וששה ימים בעומר"),
            (33, "היום שלשה ושלשים יום שהם ארבעה שבועות וחמשה ימים בעומר"),
            (42, "היום שנים וארבעים יום שהם ששה שבועות בעומר"), (49, "היום תשעה וארבעים יום שהם שבעה שבועות בעומר"),
        ]
        for (day, expected) in cases { XCTAssertEqual(OmerText.build(day), expected, "\(day)") }
    }

    func testOmerOutsideRangeIsNil() {
        for day in [0, 50, -1] { XCTAssertNil(OmerText.build(day)) }
    }

    func testOmerTextIsUniqueForEveryDay() {
        XCTAssertEqual(Set((1...49).map { OmerText.build($0)! }).count, 49)
    }

    func testHeaderMonthNamesUseTheCalendarScreenSpellingAndLeapAdar() {
        XCTAssertEqual(HebrewDateText.monthName(8, leapYear: false), "חשון")
        XCTAssertEqual(HebrewDateText.monthName(3, leapYear: false), "סיון")
        XCTAssertEqual(HebrewDateText.monthName(12, leapYear: false), "אדר")
        XCTAssertEqual(HebrewDateText.monthName(12, leapYear: true), "אדר א׳")
        XCTAssertEqual(HebrewDateText.monthName(13, leapYear: true), "אדר ב׳")
        XCTAssertEqual(HebrewDateText.monthName(7, leapYear: false), "תשרי")
    }

    func testCalendarHeaderVariantAndDayNames() {
        let svc = CalendarService()
        let jer = svc.findCity("ירושלים")!
        XCTAssertEqual(svc.calendarHeaderDate(for: D("2026-11-13"), city: jer), "ג כסלו ה׳תשפ״ז")
        XCTAssertEqual(svc.calendarHeaderDate(for: D("2026-09-26"), city: jer), "טו תשרי ה׳תשפ״ז")
        XCTAssertEqual(HebrewDateText.dayOfWeekName(1), "יום ראשון")
        XCTAssertEqual(HebrewDateText.dayOfWeekName(6), "יום שישי")
        XCTAssertEqual(HebrewDateText.dayOfWeekName(7), "שבת קודש")
    }

    func testGregorianDateIsDdMMyyyyRegardlessOfTheDeviceLocale() {
        XCTAssertEqual(HebrewDateText.gregorian(D("2026-11-13")), "13/11/2026")
        XCTAssertEqual(HebrewDateText.gregorian(D("2027-03-01")), "01/03/2027")
        let svc = CalendarService()
        XCTAssertEqual(svc.dayInfo(for: D("2026-11-13"), city: svc.cities[0]).gregorianDate, "13/11/2026")
    }

    func testZmanTimeFormatTruncatesAndMarksSeconds() {
        let f = ZmanTimeFormatter.format(secondOfDay:twelveHour:)
        XCTAssertEqual(f(16 * 3600 + 5 * 60 + 32, false), "\u{2066}16:05:\u{2069}")
        XCTAssertEqual(f(16 * 3600 + 5 * 60 + 29, false), "\u{2066}16:05.\u{2069}") // the minute is never rounded up
        XCTAssertEqual(f(16 * 3600 + 5 * 60 + 59, false), "\u{2066}16:05:\u{2069}")
        XCTAssertEqual(f(0, false), "\u{2066}00:00.\u{2069}")
    }

    func testZmanTimeFormat12Hour() {
        let f = ZmanTimeFormatter.format(secondOfDay:twelveHour:)
        XCTAssertEqual(f(16 * 3600 + 5 * 60 + 32, true), "\u{2066}4:05:\u{200E}\u{00A0}PM\u{2069}")
        XCTAssertEqual(f(30 * 60, true), "\u{2066}12:30.\u{200E}\u{00A0}AM\u{2069}")
        XCTAssertEqual(f(12 * 3600, true), "\u{2066}12:00.\u{200E}\u{00A0}PM\u{2069}")
        XCTAssertEqual(f(11 * 3600 + 59 * 60, true), "\u{2066}11:59.\u{200E}\u{00A0}AM\u{2069}")
    }

    func testPlainTimeIn12And24Hour() {
        let svc = CalendarService()
        let jer = svc.findCity("ירושלים")!
        let sunset = svc.zman("2026-11-13", jer, "sunset")
        XCTAssertEqual(svc.plainTime(sunset, city: jer, twelveHour: false), "16:45")
        XCTAssertEqual(svc.plainTime(sunset, city: jer, twelveHour: true), "4:45 PM")
    }

    func testCivilDateRoundTripAndWeekday() {
        XCTAssertEqual(CivilDate(epochDay: 0), CivilDate(year: 1970, month: 1, day: 1))
        XCTAssertEqual(D("2026-11-13").dayOfWeek, 6) // Friday
        XCTAssertEqual(D("2026-11-14").dayOfWeek, 7)
        XCTAssertEqual(D("2026-11-15").dayOfWeek, 1)
        XCTAssertEqual(D("2026-12-31").addingDays(1), D("2027-01-01"))
        XCTAssertEqual(D("2028-02-28").addingDays(1), D("2028-02-29"))
        for n in stride(from: -800_000, through: 800_000, by: 997) { XCTAssertEqual(CivilDate(epochDay: n).epochDay, n) }
    }
}
