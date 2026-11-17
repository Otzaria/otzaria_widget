import KosherSwift
import XCTest
@testable import OtzariaCalendar

/// The Hebrew calendar arithmetic, checked against the golden data from Otzaria / Kosher.Zmanim (never against the OS Hebrew calendar,
/// whose ICU is wrong for Rosh Hashana 5807 on older systems).
final class HebrewArithmeticTests: XCTestCase {
    private typealias Arithmetic = HebrewCalendarArithmetic

    override func tearDown() {
        Arithmetic.osCalendarIsCorrect = { _ in true }
        super.tearDown()
    }

    private func ymd(_ d: CivilDate) -> String {
        let h = HebrewDate(epochDay: d.epochDay)
        return "\(h.year)-\(h.month)-\(h.day)"
    }

    /// Every day of 1924-2100: the date round-trips, days and months follow on from each other and every year has a legal shape.
    func testEveryDay1924To2100IsAConsistentCalendar() {
        let first = D("1924-01-01").epochDay, last = D("2100-12-31").epochDay
        var previous = HebrewDate(epochDay: first)
        var failures: [String] = []
        for day in first...last {
            let h = HebrewDate(epochDay: day)
            if h.epochDay != day { failures.append("\(CivilDate(epochDay: day)) round trip \(h)") }
            if day > first {
                let order = Arithmetic.monthOrder(previous.year)
                if h.year == previous.year && h.month == previous.month {
                    if h.day != previous.day + 1 { failures.append("\(CivilDate(epochDay: day)) day \(h.day) after \(previous.day)") }
                } else if h.day != 1 || previous.day != Arithmetic.daysInMonth(year: previous.year, month: previous.month) {
                    failures.append("\(CivilDate(epochDay: day)) month ended early at \(previous)")
                } else if h.year == previous.year {
                    if order[order.firstIndex(of: previous.month)! + 1] != h.month { failures.append("\(CivilDate(epochDay: day)) month order") }
                } else if h.year != previous.year + 1 || h.month != 7 || previous.month != 6 {
                    failures.append("\(CivilDate(epochDay: day)) year changed outside Rosh Hashana")
                }
            }
            previous = h
        }
        for year in 5684...5861 {
            XCTAssertTrue([353, 354, 355, 383, 384, 385].contains(Arithmetic.daysInYear(year)), "year \(year)")
            XCTAssertEqual(Arithmetic.daysInYear(year) > 380, Arithmetic.isLeapYear(year), "year \(year)")
            XCTAssertFalse([1, 4, 6].contains(CivilDate(epochDay: Arithmetic.roshHashana(year)).dayOfWeek), "Rosh Hashana \(year) on Sunday, Wednesday or Friday")
            XCTAssertEqual(Arithmetic.elapsedDays(year), JewishCalendar().getJewishCalendarElapsedDays(year: year), "year \(year)")
        }
        XCTAssertTrue(failures.isEmpty, "\(failures.count) problems:\n" + failures.prefix(20).joined(separator: "\n"))
    }

    /// Kosher.Zmanim's Hebrew date for every day of 2023-09-01 ... 2031-08-31 ("year-month(Nissan = 1)-day").
    func testEveryDayOf2023To2031MatchesKosherZmanim() throws {
        let rows = try Golden.lines("hebrew_diaspora.txt.gz")
        XCTAssertEqual(rows.count, 2922)
        for r in rows {
            let p = r.split(separator: "|", omittingEmptySubsequences: false).map(String.init)
            XCTAssertEqual(ymd(D(p[0])), p[8], p[0])
        }
    }

    /// Otzaria's calendar header for the QA's 5,800+ sampled days of 1924-2100.
    func testSampledHeadersOf1924To2100MatchOtzaria() throws {
        for file in ["qa_hebrew_israel.txt.gz", "qa_hebrew_diaspora.txt.gz"] {
            let rows = try Golden.lines(file)
            XCTAssertGreaterThan(rows.count, 5000)
            for r in rows {
                let p = r.split(separator: "|", omittingEmptySubsequences: false).map(String.init)
                XCTAssertEqual(HebrewDateText.formatCalendarHeader(HebrewDateText.Parts(D(p[0]))), p[1], p[0])
            }
        }
    }

    /// 5806 has a 29-day Cheshvan, so 5807 starts on Monday 2046-10-01 (an OS with the ICU error starts it a day late).
    func testYears5806And5807() {
        XCTAssertEqual(CivilDate(epochDay: Arithmetic.roshHashana(5806)).description, "2045-09-12")
        XCTAssertEqual(CivilDate(epochDay: Arithmetic.roshHashana(5807)).description, "2046-10-01")
        XCTAssertEqual(CivilDate(epochDay: Arithmetic.roshHashana(5807)).dayOfWeek, 2)
        XCTAssertEqual(CivilDate(epochDay: Arithmetic.roshHashana(5808)).description, "2047-09-21")
        XCTAssertEqual(Arithmetic.daysInYear(5806), 384)
        XCTAssertEqual(Arithmetic.daysInYear(5807), 355)
        XCTAssertTrue(Arithmetic.isLeapYear(5806))
        XCTAssertEqual(Arithmetic.daysInMonth(year: 5806, month: 8), 29) // Cheshvan
        XCTAssertEqual(ymd(D("2045-11-09")), "5806-8-29")
        XCTAssertEqual(ymd(D("2045-11-10")), "5806-9-1") // 1 Kislev, not "30 Cheshvan"
        XCTAssertEqual(ymd(D("2046-09-30")), "5806-6-29")
        XCTAssertEqual(ymd(D("2046-10-01")), "5807-7-1")
        XCTAssertEqual(ymd(D("2046-10-02")), "5807-7-2")
    }

    /// The molad falls within three days of the first of its month (Rosh Hashana is delayed up to two days), for every month of 1924-2100.
    func testMoladIsNextToTheStartOfItsMonth() {
        for year in 5684...5861 {
            for month in Arithmetic.monthOrder(year) {
                let first = HebrewDate(year: year, month: month, day: 1).epochDay
                let molad = Arithmetic.molad(year: year, month: month)
                XCTAssertLessThanOrEqual(abs(molad.day.epochDay - first), 3, "\(year)-\(month)")
                XCTAssertTrue((0..<24).contains(molad.hours) && (0..<60).contains(molad.minutes) && (0..<18).contains(molad.chalakim))
            }
        }
    }

    // MARK: faulty OS Hebrew calendar

    /// The Gregorian days whose Hebrew date an older OS gets wrong: from 1 Kislev 5806 through 5807.
    private func simulateFaultyOS(from: String = "2045-11-10", to: String = "2047-09-20") {
        let range = D(from).epochDay...D(to).epochDay
        Arithmetic.osCalendarIsCorrect = { !range.contains($0.epochDay) }
    }

    /// Where this machine's OS is right, the stand-in (another year of the same shape) answers like the real date does.
    func testStandInAnswersLikeTheRealDate() {
        let days = D("2045-11-01").epochDay...D("2047-10-15").epochDay
        for israel in [true, false] {
            var checked = 0
            for day in days {
                let date = CivilDate(epochDay: day)
                let direct = JewishCalendar(localDate: LocalDate(year: date.year, month: date.month, day: date.day), inIsrael: israel)
                guard agreesWithOS(direct, day) else { continue }
                simulateFaultyOS(from: "2045-11-01", to: "2047-10-15")
                let standIn = Arithmetic.jewishCalendar(for: date, inIsrael: israel)
                Arithmetic.osCalendarIsCorrect = { _ in true }
                checked += 1
                XCTAssertNotEqual(standIn.getJewishYear(), HebrewDate(epochDay: day).year, "\(date) should be a stand-in")
                XCTAssertEqual(standIn.getYomTovIndex(), direct.getYomTovIndex(), "\(date)")
                XCTAssertEqual(standIn.getDayOfWeek(), direct.getDayOfWeek(), "\(date)")
                XCTAssertEqual(standIn.getDayOfOmer(), direct.getDayOfOmer(), "\(date)")
                XCTAssertEqual(standIn.getDayOfChanukah(), direct.getDayOfChanukah(), "\(date)")
                XCTAssertEqual(standIn.isRoshChodesh(), direct.isRoshChodesh(), "\(date)")
                XCTAssertEqual(standIn.isAssurBemelacha(), direct.isAssurBemelacha(), "\(date)")
                XCTAssertEqual(standIn.isTomorrowShabbosOrYomTov(), direct.isTomorrowShabbosOrYomTov(), "\(date)")
                XCTAssertEqual(standIn.getParshah(), direct.getParshah(), "\(date)")
                XCTAssertEqual(standIn.getJewishDayOfMonth(), direct.getJewishDayOfMonth(), "\(date)")
            }
            XCTAssertGreaterThan(checked, 700)
        }
    }

    private func agreesWithOS(_ jc: JewishCalendar, _ day: Int) -> Bool {
        let h = HebrewDate(epochDay: day)
        let month = jc.getNissanStartingJewishMonth()
        return jc.getJewishYear() == h.year && (h.isLeapYear ? month : min(month, 12)) == h.month && jc.getJewishDayOfMonth() == h.day
    }

    /// With the OS reporting 30 Cheshvan 5806 and a late Rosh Hashana 5807, the service still returns Otzaria's values.
    func testServiceKeepsOtzariasValuesAcrossTheFaultyRange() throws {
        simulateFaultyOS()
        let svc = CalendarService()
        let jerusalem = try XCTUnwrap(svc.findCity("ירושלים"))
        let cases: [(String, String, String?, [String])] = [
            ("2045-11-09", "כט חשון ה׳תת״ו", "תולדות", []),
            ("2045-11-10", "א כסלו ה׳תת״ו", "תולדות", ["ר\"ח"]),
            ("2046-09-30", "כט אלול ה׳תת״ו", "וילך", ["ערב ראש השנה"]),
            ("2046-10-01", "א תשרי ה׳תת״ז", "וילך", ["ראש השנה"]),
            ("2046-10-06", "ו תשרי ה׳תת״ז", "וילך", []),
        ]
        for (date, header, parasha, holidays) in cases {
            let info = svc.dayInfo(for: D(date), city: jerusalem)
            XCTAssertEqual(svc.calendarHeaderDate(for: D(date), city: jerusalem), header, date)
            XCTAssertEqual(info.parasha, parasha, date)
            XCTAssertEqual(info.holidays, holidays, date)
        }
        XCTAssertTrue(svc.dayInfo(for: D("2046-10-01"), city: jerusalem).isYomTov)
        XCTAssertFalse(svc.dayInfo(for: D("2046-09-30"), city: jerusalem).isYomTov)
    }

    func testQaSampleOf1924To2100HoldsWithTheOSWrongInTwoThirdsOfTheYears() throws {
        for wrongRemainder in [0, 1, 2] {
            Arithmetic.osCalendarIsCorrect = { $0.year % 3 != wrongRemainder }
            try checkQaHebrewCalendar(CalendarService())
        }
    }

    func testYerushalmiDoesNotUseTheOSCalendar() throws {
        let svc = CalendarService()
        let jerusalem = try XCTUnwrap(svc.findCity("ירושלים"))
        let days = (D("2045-10-01").epochDay...D("2047-12-31").epochDay).map { CivilDate(epochDay: $0) }
        let reference = days.map { svc.dayInfo(for: $0, city: jerusalem).dafYomiYerushalmi }
        simulateFaultyOS()
        XCTAssertEqual(days.map { svc.dayInfo(for: $0, city: jerusalem).dafYomiYerushalmi }, reference)
        XCTAssertNil(svc.dayInfo(for: D("2046-10-10"), city: jerusalem).dafYomiYerushalmi) // Yom Kippur 5807
        XCTAssertNotNil(svc.dayInfo(for: D("2046-10-11"), city: jerusalem).dafYomiYerushalmi)
    }
}

/// The whole day-by-day golden of 2023-2031 again, with the OS calendar distrusted for every one of those days.
final class HebrewGoldenStandInTests: HebrewGoldenTests {
    override func setUp() {
        super.setUp()
        HebrewCalendarArithmetic.osCalendarIsCorrect = { !(2020...2035).contains($0.year) }
    }

    override func tearDown() {
        HebrewCalendarArithmetic.osCalendarIsCorrect = { _ in true }
        super.tearDown()
    }
}
