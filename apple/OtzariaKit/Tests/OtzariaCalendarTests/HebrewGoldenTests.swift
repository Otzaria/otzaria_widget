import XCTest
@testable import OtzariaCalendar

/// Day-by-day parity with Otzaria for 2023-09-01 ... 2031-08-31 (2,922 days), Israel (Jerusalem) and diaspora (New York):
/// Hebrew date, holiday labels, upcoming parsha, daf yomi, omer, Yom Tov flag and the day-dependent zmanim shown.
class HebrewGoldenTests: XCTestCase {
    private let svc = CalendarService()

    private struct Line {
        let date: String, chip: String, events: [String], parasha: String, daf: String, omer: String
        let assur: Bool, dow: Int, ymd: String, relevant: [String]
    }

    private func lines(israel: Bool) throws -> [Line] {
        try Golden.lines(israel ? "hebrew_israel.txt.gz" : "hebrew_diaspora.txt.gz").map { l in
            let p = l.split(separator: "|", omittingEmptySubsequences: false).map(String.init)
            return Line(date: p[0], chip: p[1], events: p[2].isEmpty ? [] : p[2].components(separatedBy: ";"), parasha: p[3], daf: p[4],
                        omer: p[5], assur: p[6] == "1", dow: Int(p[7])!, ymd: p[8],
                        relevant: p.count > 9 && !p[9].isEmpty ? p[9].components(separatedBy: ",") : [])
        }
    }

    private func isFastEvent(_ e: String) -> Bool { e == "שבעה עשר בתמוז" || e == "תשעה באב" }

    /// Documented kosher_dart 2.0.20 bug: it labels BOTH Saturday 17 Tammuz / 9 Av and the postponed Sunday as the fast.
    private func isKosherDartPostponedFastBug(_ l: Line, ours: [String]) -> Bool {
        if l.dow != 7 { return false }
        let ymd = l.ymd.split(separator: "-").map { Int($0)! } // year-month(Nissan=1)-day
        let fastOnShabbat = (ymd[1] == 4 && ymd[2] == 17) || (ymd[1] == 5 && ymd[2] == 9)
        return fastOnShabbat && l.events.contains(where: isFastEvent) && !ours.contains(where: isFastEvent)
    }

    private func check(israel: Bool, cityName: String) throws {
        let city = try XCTUnwrap(svc.findCity(cityName))
        XCTAssertEqual(city.isInIsrael, israel)
        var failures: [String] = []
        let rows = try lines(israel: israel)
        var shabbatFastBugs = 0
        for l in rows {
            let date = D(l.date)
            let info = svc.dayInfo(for: date, city: city)

            // Otzaria's library chip says חשוון/סיוון, its calendar header says חשון/סיון -> we use the header spelling
            let expectedDate = l.chip.replacingOccurrences(of: "חשוון", with: "חשון").replacingOccurrences(of: "סיוון", with: "סיון")
            if info.hebrewDate != expectedDate { failures.append("\(l.date) date '\(info.hebrewDate)' != '\(expectedDate)'") }

            // Otzaria appends the parsha to the Shabbat's label list; the widget keeps it in DayInfo.parasha
            var expectedHolidays = l.dow == 7 ? l.events.filter { $0 != l.parasha } : l.events
            let dartFastBug = isKosherDartPostponedFastBug(l, ours: info.holidays)
            if dartFastBug { shabbatFastBugs += 1; expectedHolidays.removeAll(where: isFastEvent) }
            if info.holidays != expectedHolidays { failures.append("\(l.date) holidays \(info.holidays) != \(expectedHolidays)") }

            let expectedParasha = l.parasha.isEmpty ? nil : l.parasha
            if info.parasha != expectedParasha { failures.append("\(l.date) parasha '\(info.parasha ?? "nil")' != '\(expectedParasha ?? "nil")'") }
            let expectedDaf = l.daf.isEmpty ? nil : l.daf
            if info.dafYomiBavli != expectedDaf { failures.append("\(l.date) daf '\(info.dafYomiBavli ?? "nil")' != '\(expectedDaf ?? "nil")'") }
            let expectedOmer = l.omer.isEmpty ? nil : l.omer
            if info.omer != expectedOmer { failures.append("\(l.date) omer '\(info.omer ?? "nil")' != '\(expectedOmer ?? "nil")'") }

            if info.isYomTov != l.assur { failures.append("\(l.date) isYomTov \(info.isYomTov) != \(l.assur)") }
            if info.isShabbat != (l.dow == 7) { failures.append("\(l.date) isShabbat") }
            if info.gregorianDate != String(format: "%02d/%02d/%04d", date.day, date.month, date.year) { failures.append("\(l.date) gregorian \(info.gregorianDate)") }

            // which of Otzaria's day-dependent zmanim are relevant
            let ids = ["candleLighting", "motzeiShabbos", "motzeiYomTov", "achilasChametzGRA", "fastEndTikLenient"]
            let shown = Set(svc.zmanim(for: date, city: city, ids: ids).map(\.id))
            for id in ids {
                if dartFastBug && id == "fastEndTikLenient" { XCTAssertFalse(shown.contains(id)); continue } // Otzaria wrongly offers fast-end times on that Shabbat
                let expected = l.relevant.contains(id)
                if shown.contains(id) != expected { failures.append("\(l.date) relevance of \(id): ours \(shown.contains(id)) != Otzaria \(expected)") }
            }

            // DayInfo candle lighting / havdalah follow the same predicates
            if (info.candleLighting != nil) != l.relevant.contains("candleLighting") { failures.append("\(l.date) DayInfo.candleLighting presence") }
            if (info.havdalah != nil) != (l.relevant.contains("motzeiShabbos") || l.relevant.contains("motzeiYomTov")) { failures.append("\(l.date) DayInfo.havdalah presence") }
        }
        XCTAssertEqual(rows.count, 2922)
        XCTAssertTrue(failures.isEmpty, "\(failures.count) mismatches:\n" + failures.prefix(40).joined(separator: "\n"))
        XCTAssertLessThanOrEqual(shabbatFastBugs, 2) // 17 Tammuz 5789 (2029-06-30) is the only one in this range
    }

    func testEveryDayMatchesOtzariaIsrael() throws { try check(israel: true, cityName: "ירושלים") }
    func testEveryDayMatchesOtzariaDiaspora() throws { try check(israel: false, cityName: "ניו יורק") }
}
