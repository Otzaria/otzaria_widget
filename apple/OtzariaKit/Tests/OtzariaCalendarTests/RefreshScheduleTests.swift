import XCTest
@testable import OtzariaCalendar

final class RefreshScheduleTests: XCTestCase {
    private let svc = CalendarService()
    private lazy var jerusalem = svc.findCity("ירושלים")!
    private let core = ["alos72Degrees", "sunrise", "sofZmanShmaGRA", "sofZmanTfilaGRA", "chatzos", "minchaGedolaGreater30", "sunset", "tzeitTikochinsky"]

    func testMomentsAreAscendingUniqueAndAfterNow() {
        let now = at(2026, 11, 13, 12, 0, offset: 2)
        let m = svc.refreshMoments(after: now, count: 12, city: jerusalem, ids: core, dayTransition: "sunset")
        XCTAssertEqual(m.count, 12)
        XCTAssertEqual(m, m.sorted())
        XCTAssertEqual(Set(m).count, m.count)
        XCTAssertTrue(m.allSatisfy { $0 > now })
    }

    func testNextMomentsAreTheRemainingZmanimOfTheDayThenTheDaySwitchThenMidnight() {
        let now = at(2026, 11, 13, 12, 0, offset: 2)
        let m = svc.refreshMoments(after: now, count: 4, city: jerusalem, ids: core, dayTransition: "sunset")
        XCTAssertEqual(m[0], svc.zman("2026-11-13", jerusalem, "sunset"))           // also the Hebrew-day switch
        XCTAssertEqual(m[1], svc.zman("2026-11-13", jerusalem, "tzeitTikochinsky"))
        XCTAssertEqual(m[2], at(2026, 11, 14, 0, 0, offset: 2))                     // civil midnight
        XCTAssertEqual(m[3], svc.zman("2026-11-14", jerusalem, "alos72Degrees"))
    }

    func testTheHebrewDaySwitchAndMidnightArePresentEvenWithoutZmanim() {
        let now = at(2026, 11, 13, 12, 0, offset: 2)
        let m = svc.refreshMoments(after: now, count: 3, city: jerusalem, ids: [], dayTransition: "tzais")
        XCTAssertEqual(m[0], svc.dayTransitionInstant(civilDate: D("2026-11-13"), city: jerusalem, dayTransition: "tzais"))
        XCTAssertEqual(m[1], at(2026, 11, 14, 0, 0, offset: 2))
        XCTAssertEqual(m[2], svc.dayTransitionInstant(civilDate: D("2026-11-14"), city: jerusalem, dayTransition: "tzais"))
    }

    func testMidnightTransitionHasNoDaySwitchBesidesMidnight() {
        let now = at(2026, 11, 13, 20, 0, offset: 2)
        let m = svc.refreshMoments(after: now, count: 2, city: jerusalem, ids: [], dayTransition: "midnight")
        XCTAssertEqual(m[0], at(2026, 11, 14, 0, 0, offset: 2))
        XCTAssertEqual(m[1], at(2026, 11, 15, 0, 0, offset: 2))
    }

    func testMidnightFollowsTheCityZoneAcrossDst() {
        let ny = svc.findCity("ניו יורק")!
        let m = svc.refreshMoments(after: at(2026, 11, 1, 12, 0, offset: -5), count: 1, city: ny, ids: [], dayTransition: "midnight")
        XCTAssertEqual(m, [at(2026, 11, 2, 0, 0, offset: -5)])
        let before = svc.refreshMoments(after: at(2026, 3, 8, 0, 30, offset: -5), count: 1, city: ny, ids: [], dayTransition: "midnight")
        XCTAssertEqual(before, [at(2026, 3, 9, 0, 0, offset: -4)]) // the 23 hour day
    }

    func testOhrPrefixBoundaryIsSunriseMinus90() {
        let now = at(2026, 11, 14, 3, 0, offset: 2)
        let m = svc.refreshMoments(after: now, count: 1, city: jerusalem, ids: [], dayTransition: "midnight", includeOhrPrefixBoundary: true)
        let sunrise = svc.zman("2026-11-14", jerusalem, "sunriseAstronomical")
        XCTAssertEqual(m, [sunrise.addingTimeInterval(-90 * 60)])
    }

    func testEarlyMorningStillSeesYesterdaysLateZmanim() {
        // chatzos layla of 12 Nov lands after local midnight on 13 Nov
        let layla = svc.zman("2026-11-12", jerusalem, "chatzosLayla")
        let now = layla.addingTimeInterval(-60)
        let m = svc.refreshMoments(after: now, count: 1, city: jerusalem, ids: ["chatzosLayla"], dayTransition: "midnight")
        XCTAssertEqual(m, [layla])
    }

    func testZmanimDayPicksTodayAndMarksTheNextZman() {
        let now = at(2026, 11, 13, 12, 0, offset: 2)
        let day = svc.zmanimDay(now: now, city: jerusalem, ids: core, dayTransition: "sunset")
        XCTAssertEqual(day.date, D("2026-11-13"))
        XCTAssertFalse(day.isTomorrow)
        XCTAssertEqual(day.next?.id, "sunset")
        XCTAssertEqual(day.times.map(\.id), ["alos72Degrees", "sunrise", "sofZmanShmaGRA", "sofZmanTfilaGRA", "chatzos", "minchaGedolaGreater30", "sunset", "tzeitTikochinsky"])
        XCTAssertEqual(day.nextIndex, 6)
        XCTAssertTrue(day.untimed.isEmpty)
    }

    func testZmanimDayRollsToTomorrowWhenTodaysAreOver() {
        // 19:00 Jerusalem: after sunset the Hebrew day is already the 14th; tomorrow's list is shown, the next one is its first zman
        let now = at(2026, 11, 13, 19, 0, offset: 2)
        let day = svc.zmanimDay(now: now, city: jerusalem, ids: core, dayTransition: "midnight")
        XCTAssertEqual(day.date, D("2026-11-14"))
        XCTAssertTrue(day.isTomorrow)
        XCTAssertEqual(day.next?.id, "alos72Degrees")
        XCTAssertEqual(day.nextIndex, 0)
    }

    func testZmanimOnAnotherCivilDateGoToOtherDaysAndMissingOnesToUntimed() throws {
        let reykjavik = try XCTUnwrap(svc.findCity("רייקיאוויק"))
        let now = at(2027, 6, 21, 8, 0, offset: 0)
        let day = svc.zmanimDay(now: now, city: reykjavik, ids: ["tzeitGeonim8point5", "sunset"], dayTransition: "midnight")
        XCTAssertEqual(day.untimed.map(\.id), ["tzeitGeonim8point5"])
        XCTAssertTrue(day.times.isEmpty)
        XCTAssertEqual(day.otherDays.map(\.id), ["sunset"]) // the Reykjavik midsummer sunset falls after local midnight
    }
}
