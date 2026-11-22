import XCTest
import OtzariaCalendar
import OtzariaCore
@testable import OtzariaWidgetsUI

final class CalendarModelTests: XCTestCase {
    let ids = ZmanimPreset.core

    func testZmanimNextAndPerSize() {
        let o = ZmanimOptions(city: "ירושלים", customIds: ids)
        let small = ZmanimModel.make(T.ctx(.small), o)
        XCTAssertEqual(small.state, .ok)
        XCTAssertNotNil(small.next)
        XCTAssertTrue(small.next!.isNext)
        XCTAssertGreaterThanOrEqual(small.next!.time!, T.now)
        XCTAssertTrue(small.rows.isEmpty)
        XCTAssertTrue(small.headerEnd.hasPrefix("ירושלים"))

        let medium = ZmanimModel.make(T.ctx(.medium), o)
        XCTAssertLessThanOrEqual(medium.after.count, 3)
        XCTAssertTrue(medium.after.allSatisfy { $0.time! > medium.next!.time! })

        let large = ZmanimModel.make(T.ctx(.large), o)
        XCTAssertEqual(large.rows.filter(\.isNext).count, 1)
        XCTAssertLessThanOrEqual(large.rows.count, 9)
        let times = large.rows.compactMap(\.time)
        XCTAssertEqual(times, times.sorted())
    }

    func testZmanimPastAreDimmedBeforeNext() {
        let o = ZmanimOptions(city: "ירושלים", customIds: ids)
        let large = ZmanimModel.make(T.ctx(.large), o)
        let nextIdx = large.rows.firstIndex(where: \.isNext)!
        XCTAssertTrue(large.rows[..<nextIdx].allSatisfy(\.isPast))
        XCTAssertTrue(large.rows[(nextIdx + 1)...].allSatisfy { !$0.isPast })
    }

    func testZmanimTomorrowAfterAllPassed() {
        // 23:00 local: every selected zman of today is over
        let late = Date(timeIntervalSince1970: 1_794_906_000 + 12 * 3600)
        let m = ZmanimModel.make(T.ctx(.medium, now: late), ZmanimOptions(city: "ירושלים", customIds: ["sunrise", "sunset"]))
        XCTAssertTrue(m.tomorrow)
        XCTAssertTrue(m.headerEnd.contains("מחר"))
        XCTAssertNotNil(m.next)
    }

    func testZmanimCityFollowsSettingsThenSnapshot() {
        var settings = WidgetSettings()
        settings.cityOverride = "תל אביב"
        let o = ZmanimOptions()
        let c = T.ctx(.small, settings: settings)
        XCTAssertEqual(c.city(widgetCity: nil).name, "תל אביב")
        XCTAssertEqual(c.city(widgetCity: "חיפה").name, "חיפה")
        XCTAssertEqual(T.ctx(.small).city(widgetCity: "לא קיימת").name, "ירושלים")
        XCTAssertEqual(ZmanimModel.make(c, o).cityName, "תל אביב")
        XCTAssertEqual(T.ctx(.small, snapshot: nil).city(widgetCity: nil).name, "ירושלים")
    }

    func testZmanimIdsPrecedence() {
        var settings = WidgetSettings()
        let cal = WidgetContext.calendarService
        XCTAssertEqual(T.ctx(.small).zmanimIds(custom: []), cal.defaultEnabledZmanim)
        settings.zmanimOverride = ["sunrise"]
        XCTAssertEqual(T.ctx(.small, settings: settings).zmanimIds(custom: []), ["sunrise"])
        XCTAssertEqual(T.ctx(.small, settings: settings).zmanimIds(custom: ["sunset"]), ["sunset"])
        settings.zmanimOverride = []
        XCTAssertEqual(T.ctx(.small, settings: settings).zmanimIds(custom: []), [])
        XCTAssertEqual(ZmanimModel.make(T.ctx(.small, settings: settings), ZmanimOptions()).state, .empty("אין זמנים להצגה"))
    }

    func testTwelveHourClock() {
        var settings = WidgetSettings()
        settings.twelveHourClockOverride = true
        let m = ZmanimModel.make(T.ctx(.small, settings: settings), ZmanimOptions(city: "ירושלים", customIds: ids))
        XCTAssertTrue(m.next!.timeText.hasSuffix("AM") || m.next!.timeText.hasSuffix("PM"))
    }

    func testZmanimRefreshDatesAscendingAndAfterNow() {
        let ctx = T.ctx(.medium)
        let dates = ZmanimModel.refreshDates(ctx, ZmanimOptions(city: "ירושלים", customIds: ids), count: 10)
        XCTAssertEqual(dates.count, 10)
        XCTAssertEqual(dates, dates.sorted())
        XCTAssertTrue(dates.allSatisfy { $0 > T.now })
        // the first moment is a second past a real zman or day switch
        let model = ZmanimModel.make(T.ctx(.medium, now: dates[0]), ZmanimOptions(city: "ירושלים", customIds: ids))
        XCTAssertNotNil(model.next)
    }

    func testNextHighlightMovesAtRefreshMoment() {
        let o = ZmanimOptions(city: "ירושלים", customIds: ids)
        let before = ZmanimModel.make(T.ctx(.small), o).next!
        let moment = ZmanimModel.refreshDates(T.ctx(.small), o, count: 1)[0]
        XCTAssertEqual(moment.timeIntervalSince(before.time!), 1, accuracy: 0.001)
        XCTAssertNotEqual(ZmanimModel.make(T.ctx(.small, now: moment), o).next?.name, before.name)
    }

    func testTimelinePlan() {
        let now = T.now
        let moments = [now.addingTimeInterval(-5), now.addingTimeInterval(300), now.addingTimeInterval(100)]
        let plan = TimelinePlan.dates(now: now, moments: moments)
        XCTAssertEqual(plan.entries, [now, now.addingTimeInterval(100), now.addingTimeInterval(300)])
        XCTAssertEqual(plan.reloadAt, now.addingTimeInterval(300))
        let none = TimelinePlan.dates(now: now, moments: [])
        XCTAssertEqual(none.entries, [now])
        XCTAssertEqual(none.reloadAt, now.addingTimeInterval(15 * 60))
    }

    func testHebrewCalendarLineCapsAndToggles() {
        // Friday 2026-11-20 mid-day in Jerusalem: parasha + candle lighting + daf
        let friday = Date(timeIntervalSince1970: 1_794_906_000 + 3 * 86400)
        func lines(_ size: WidgetSize, _ o: CalendarOptions = CalendarOptions()) -> [CalendarLine] {
            HebrewCalendarModel.make(T.ctx(size, now: friday), o).lines
        }
        XCTAssertEqual(lines(.small).count, 1)
        XCTAssertEqual(lines(.medium).count, 3)
        XCTAssertLessThanOrEqual(lines(.large).count, 6)
        XCTAssertTrue(lines(.large).contains { $0.label == "הדלקת נרות" })
        XCTAssertFalse(lines(.large, CalendarOptions(showCandle: false)).contains { $0.label == "הדלקת נרות" })
        XCTAssertFalse(lines(.large, CalendarOptions(showDaf: false)).contains { $0.label == "דף יומי" })
        XCTAssertTrue(lines(.large, CalendarOptions(showYerushalmi: true)).contains { $0.label == "ירושלמי" })
        XCTAssertFalse(lines(.large).contains { $0.label == "ירושלמי" })
    }

    func testHebrewCalendarFollowsGlobalYerushalmi() {
        var settings = WidgetSettings()
        settings.showYerushalmiDaf = true
        let m = HebrewCalendarModel.make(T.ctx(.large, settings: settings), CalendarOptions())
        XCTAssertTrue(m.lines.contains { $0.label == "ירושלמי" })
        let off = HebrewCalendarModel.make(T.ctx(.large, settings: settings), CalendarOptions(showYerushalmi: false))
        XCTAssertFalse(off.lines.contains { $0.label == "ירושלמי" })
    }

    func testHebrewDateHeaderAndSplit() {
        let m = HebrewCalendarModel.make(T.ctx(.medium), CalendarOptions())
        XCTAssertFalse(m.day.isEmpty)
        XCTAssertFalse(m.year.isEmpty)
        XCTAssertEqual(m.gregorian, "17 בנובמבר 2026")
        XCTAssertEqual(m.weekday, "יום שלישי")
        XCTAssertEqual(HebrewCalendarModel.splitHebrewDate("ג׳ כסלו תשפ״ז").day, "ג׳ כסלו")
        XCTAssertEqual(HebrewCalendarModel.splitHebrewDate("ג׳ כסלו תשפ״ז").year, "תשפ״ז")
        XCTAssertEqual(HebrewCalendarModel.splitHebrewDate("כסלו").year, "")
    }

    func testCalendarWidgetsWorkWithoutSnapshot() {
        let c = T.ctx(.medium, snapshot: nil)
        XCTAssertEqual(HebrewCalendarModel.make(c, CalendarOptions()).state, .ok)
        XCTAssertEqual(ZmanimModel.make(c, ZmanimOptions()).state, .ok)
        let dash = DashboardModel.make(c, DashboardOptions(sections: [.date, .zman]))
        XCTAssertEqual(dash.sections.map(\.section), [.date, .zman])
    }

    func testCalendarRefreshDatesAreOrdered() {
        let d = HebrewCalendarModel.refreshDates(T.ctx(.small), count: 3)
        XCTAssertEqual(d.count, 3)
        XCTAssertEqual(d, d.sorted())
    }
}

final class DashboardModelTests: XCTestCase {
    func testPositionsOrderAndHidden() {
        let s = DashboardOptions.sections(fromPositions: [.date: 3, .zman: 1, .reading: 0, .progress: 2, .bookmarks: 3])
        XCTAssertEqual(s, [.zman, .progress, .date, .bookmarks])
        XCTAssertEqual(DashboardOptions.sections(fromPositions: [:]), [])
        XCTAssertEqual(DashboardOptions().sections, [.date, .zman, .reading, .progress, .bookmarks])
    }

    func testSectionCapsPerSize() {
        XCTAssertEqual(DashboardModel.make(T.ctx(.medium), DashboardOptions()).sections.map(\.section), [.date, .zman, .reading])
        XCTAssertEqual(DashboardModel.make(T.ctx(.large), DashboardOptions()).sections.count, 5)
    }

    func testOrderFollowsOptions() {
        let o = DashboardOptions(sections: [.bookmarks, .date])
        XCTAssertEqual(DashboardModel.make(T.ctx(.large), o).sections.map(\.section), [.bookmarks, .date])
    }

    func testMediumIsCompactOneLine() {
        let m = DashboardModel.make(T.ctx(.medium), DashboardOptions())
        XCTAssertTrue(m.compact)
        for line in m.sections.flatMap(\.lines) {
            XCTAssertEqual(line.sub, "")
            XCTAssertNil(line.bar)
            XCTAssertNil(line.countdownTo)
        }
        XCTAssertEqual(m.sections.flatMap(\.lines).count, 3)
    }

    func testLargeAddsExtraLinesAndLiveCountdown() {
        let m = DashboardModel.make(T.ctx(.large), DashboardOptions())
        XCTAssertFalse(m.compact)
        let lines = m.sections.reduce(0) { $0 + $1.lines.count }
        XCTAssertGreaterThan(lines, 5)
        XCTAssertNotNil(m.sections.first { $0.section == .zman }?.lines.first?.countdownTo)
        XCTAssertNotNil(m.sections.first { $0.section == .progress }?.lines.first?.bar)
    }

    func testEmptyAndMissingStates() {
        XCTAssertEqual(DashboardModel.make(T.ctx(.large), DashboardOptions(sections: [])).state, .empty("לא נבחרו חלקים להצגה — התאם את הווידג'ט"))
        let none = DashboardModel.make(T.ctx(.large, snapshot: nil), DashboardOptions(sections: [.reading, .progress, .bookmarks]))
        XCTAssertEqual(none.state, .missing(DataText.noSnapshot))
        var empty = Fixtures.snapshot(now: T.now)
        empty.recentBooks = []; empty.currentTab = nil; empty.bookmarks = []; empty.shamorZachor = []
        XCTAssertEqual(DashboardModel.make(T.ctx(.large, snapshot: empty), DashboardOptions(sections: [.reading, .bookmarks])).state, .empty("אין מה להציג כרגע"))
    }

    func testDuplicateSectionsCollapse() {
        let m = DashboardModel.make(T.ctx(.large), DashboardOptions(sections: [.date, .date, .zman]))
        XCTAssertEqual(m.sections.map(\.section), [.date, .zman])
    }

    func testReadingSectionStartsWithOpenTab() {
        let m = DashboardModel.make(T.ctx(.large), DashboardOptions(sections: [.reading]))
        XCTAssertEqual(m.sections[0].lines[0].text, "ברכות")
        XCTAssertEqual(m.sections[0].lines.count, 2)
        XCTAssertNotEqual(m.sections[0].lines[1].text, "ברכות")
    }
}

final class ZmanimWindowTests: XCTestCase {
    func testLargeShowsAtMostTwoPastWithFullRegistry() {
        let all = WidgetContext.calendarService.zmanimRegistry.map(\.id)
        let m = ZmanimModel.make(T.ctx(.large), ZmanimOptions(city: "ירושלים", customIds: all))
        XCTAssertEqual(m.rows.count, 9)
        XCTAssertLessThanOrEqual(m.rows.filter(\.isPast).count, 2)
        XCTAssertEqual(m.rows.filter(\.isNext).count, 1)
    }
}
