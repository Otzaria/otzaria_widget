import XCTest
@testable import OtzariaCalendar

final class EffectiveDateAndDstTests: XCTestCase {
    private let svc = CalendarService()
    private lazy var jerusalem = svc.findCity("ירושלים")!
    private lazy var newYork = svc.findCity("ניו יורק")!
    private lazy var london = svc.findCity("לונדון")!

    private func zman(_ date: String, _ city: CityInfo, _ id: String) -> Date { svc.zman(date, city, id) }
    private func offsetHours(_ t: Date, _ city: CityInfo) -> Double { Double(svc.utcOffsetSeconds(at: t, city: city)) / 3600 }
    private func effective(_ now: Date, _ city: CityInfo, _ mode: String) -> CivilDate { svc.effectiveDate(now: now, city: city, dayTransition: mode) }

    // MARK: effective date (day transition)

    func testSunsetTransitionFlipsTheDateAtTheElevationAdjustedSunset() {
        let sunset = zman("2026-11-13", jerusalem, "sunset") // 16:45:32
        XCTAssertEqual(effective(sunset.addingTimeInterval(-1), jerusalem, "sunset"), D("2026-11-13"))
        XCTAssertEqual(effective(sunset, jerusalem, "sunset"), D("2026-11-14"))
        XCTAssertEqual(effective(sunset.addingTimeInterval(60), jerusalem, "sunset"), D("2026-11-14"))
        XCTAssertEqual(effective(at(2026, 11, 13, 0, 5, offset: 2), jerusalem, "sunset"), D("2026-11-13"))
        XCTAssertEqual(effective(at(2026, 11, 13, 12, 0, offset: 2), jerusalem, "sunset"), D("2026-11-13"))
        // after sunset the Jewish date is already the next civil date, and it stays so after civil midnight (before that day's sunset)
        XCTAssertEqual(effective(at(2026, 11, 13, 23, 59, offset: 2), jerusalem, "sunset"), D("2026-11-14"))
        XCTAssertEqual(effective(at(2026, 11, 14, 0, 1, offset: 2), jerusalem, "sunset"), D("2026-11-14"))
    }

    func testTzaisAndRabbeinuTamTransitions() {
        let tzais = zman("2026-11-13", jerusalem, "tzeitGeonim8point5")
        XCTAssertEqual(effective(tzais.addingTimeInterval(-2), jerusalem, "tzais"), D("2026-11-13"))
        XCTAssertEqual(effective(tzais.addingTimeInterval(1), jerusalem, "tzais"), D("2026-11-14"))

        let rt = zman("2026-11-13", jerusalem, "sunset").addingTimeInterval(72 * 60)
        XCTAssertEqual(effective(rt.addingTimeInterval(-1), jerusalem, "rabbeinuTam"), D("2026-11-13"))
        XCTAssertEqual(effective(rt, jerusalem, "rabbeinuTam"), D("2026-11-14"))
        let between = zman("2026-11-13", jerusalem, "sunset").addingTimeInterval(30 * 60)
        XCTAssertEqual(effective(between, jerusalem, "rabbeinuTam"), D("2026-11-13"))
        XCTAssertEqual(effective(between, jerusalem, "sunset"), D("2026-11-14"))
    }

    func testMidnightTransitionIsTheCivilDate() {
        XCTAssertEqual(effective(at(2026, 11, 13, 23, 59, 59, offset: 2), jerusalem, "midnight"), D("2026-11-13"))
        XCTAssertEqual(effective(at(2026, 11, 14, 0, 0, offset: 2), jerusalem, "midnight"), D("2026-11-14"))
        XCTAssertEqual(effective(at(2026, 11, 13, 18, 30, offset: 2), jerusalem, "midnight"), D("2026-11-13"))
    }

    func testUnknownTransitionBehavesLikeSunsetAsInOtzaria() {
        let after = zman("2026-11-13", jerusalem, "sunset").addingTimeInterval(300)
        XCTAssertEqual(effective(after, jerusalem, "whatever"), D("2026-11-14"))
        XCTAssertEqual(effective(after, jerusalem, ""), D("2026-11-14"))
    }

    func testEffectiveDateUsesTheCityZone() {
        // 21:30 UTC is 23:30 in Jerusalem (after sunset) but 16:30 in New York (before sunset)
        let instant = at(2026, 11, 13, 21, 30, offset: 0)
        XCTAssertEqual(effective(instant, jerusalem, "sunset"), D("2026-11-14"))
        XCTAssertEqual(effective(instant, newYork, "sunset"), D("2026-11-13"))
        // 2026-11-14 01:00 UTC is Nov 14 03:00 in Jerusalem and Nov 13 20:00 in NY (after sunset -> Nov 14)
        let late = at(2026, 11, 14, 1, 0, offset: 0)
        XCTAssertEqual(effective(late, jerusalem, "sunset"), D("2026-11-14"))
        XCTAssertEqual(effective(late, newYork, "sunset"), D("2026-11-14"))
    }

    func testEffectiveDateOnDstChangeDays() {
        // Israel DST starts Friday 2026-03-27 02:00 (UTC+2 -> +3)
        XCTAssertEqual(effective(at(2026, 3, 27, 12, 0, offset: 3), jerusalem, "sunset"), D("2026-03-27"))
        let sunset = zman("2026-03-27", jerusalem, "sunset")
        XCTAssertEqual(offsetHours(sunset, jerusalem), 3)
        XCTAssertEqual(effective(sunset.addingTimeInterval(60), jerusalem, "sunset"), D("2026-03-28"))
        XCTAssertEqual(effective(sunset.addingTimeInterval(-60), jerusalem, "sunset"), D("2026-03-27"))
        XCTAssertEqual(effective(at(2026, 3, 27, 0, 30, offset: 2), jerusalem, "midnight"), D("2026-03-27"))
        // US: 2026-11-01 DST ends at 02:00 EDT -> 01:00 EST (25 hour day)
        let nySunset = zman("2026-11-01", newYork, "sunset")
        XCTAssertEqual(offsetHours(nySunset, newYork), -5)
        XCTAssertEqual(effective(nySunset.addingTimeInterval(-60), newYork, "sunset"), D("2026-11-01"))
        XCTAssertEqual(effective(nySunset.addingTimeInterval(60), newYork, "sunset"), D("2026-11-02"))
    }

    func testOhrPrefixShowsBetweenTheTransitionAnd90MinutesBeforeSunrise() {
        let sunsetNow = zman("2026-11-13", jerusalem, "sunset").addingTimeInterval(600)
        XCTAssertTrue(svc.shouldShowOhrPrefix(now: sunsetNow, city: jerusalem, dayTransition: "sunset"))
        XCTAssertFalse(svc.shouldShowOhrPrefix(now: sunsetNow, city: jerusalem, dayTransition: "midnight"))
        XCTAssertTrue(svc.shouldShowOhrPrefix(now: at(2026, 11, 14, 4, 0, offset: 2), city: jerusalem, dayTransition: "midnight"))
        XCTAssertFalse(svc.shouldShowOhrPrefix(now: at(2026, 11, 14, 8, 0, offset: 2), city: jerusalem, dayTransition: "midnight"))
    }

    // MARK: DST boundaries

    func testIsraelDstStartAndEndZmanimCarryTheRightOffsets() {
        XCTAssertEqual(offsetHours(zman("2026-03-26", jerusalem, "sunrise"), jerusalem), 2)
        XCTAssertEqual(offsetHours(zman("2026-03-27", jerusalem, "sunrise"), jerusalem), 3)
        XCTAssertEqual(offsetHours(zman("2026-10-24", jerusalem, "sunset"), jerusalem), 3)
        XCTAssertEqual(offsetHours(zman("2026-10-25", jerusalem, "sunset"), jerusalem), 2)

        // sunrise moves by the 1h jump minus 1-2 minutes of seasonal drift
        let before = svc.clock(zman("2026-03-26", jerusalem, "sunrise"), jerusalem)
        let after = svc.clock(zman("2026-03-27", jerusalem, "sunrise"), jerusalem)
        let wallDelta = Double((after.h * 3600 + after.m * 60 + after.s) - (before.h * 3600 + before.m * 60 + before.s)) / 60
        XCTAssertTrue((56.0...60.0).contains(wallDelta), "\(wallDelta)")
        let candle = svc.zmanim(for: D("2026-03-27"), city: jerusalem, ids: ["candleLighting"])[0].time!
        XCTAssertEqual(offsetHours(candle, jerusalem), 3)
        XCTAssertEqual(candle, zman("2026-03-27", jerusalem, "sunset").addingTimeInterval(-40 * 60))
    }

    func testChatzosLaylaAcrossTheDstJumpIsTheTrueMidpointOfTheNight() {
        let sunset = zman("2026-03-26", jerusalem, "sunset")
        let sunriseTomorrow = zman("2026-03-27", jerusalem, "sunriseAstronomical")
        let mid = zman("2026-03-26", jerusalem, "chatzosLayla")
        XCTAssertLessThanOrEqual(abs(mid.timeIntervalSince(sunset) - sunriseTomorrow.timeIntervalSince(mid)) / 60, 0.1)
        XCTAssertEqual(offsetHours(mid, jerusalem), 2)
        XCTAssertEqual(offsetHours(zman("2026-10-24", jerusalem, "chatzosLayla"), jerusalem), 3)
    }

    func testUsDstStartAndEnd() {
        XCTAssertEqual(offsetHours(zman("2026-03-07", newYork, "sunrise"), newYork), -5)
        XCTAssertEqual(offsetHours(zman("2026-03-08", newYork, "sunrise"), newYork), -4)
        XCTAssertEqual(offsetHours(zman("2026-10-31", newYork, "sunset"), newYork), -4)
        XCTAssertEqual(offsetHours(zman("2026-11-01", newYork, "sunset"), newYork), -5)
        let a = svc.clock(zman("2026-03-07", newYork, "sunset"), newYork)
        let b = svc.clock(zman("2026-03-08", newYork, "sunset"), newYork)
        let delta = Double((b.h * 3600 + b.m * 60 + b.s) - (a.h * 3600 + a.m * 60 + a.s)) / 60
        XCTAssertTrue((58.0...63.0).contains(delta), "\(delta)")
        let friday = svc.zmanim(for: D("2026-11-13"), city: newYork, ids: ["candleLighting"])[0].time!
        XCTAssertEqual(offsetHours(friday, newYork), -5)
    }

    func testUkDstStartAndEnd() {
        XCTAssertEqual(offsetHours(zman("2026-03-28", london, "sunrise"), london), 0)
        XCTAssertEqual(offsetHours(zman("2026-03-29", london, "sunrise"), london), 1)
        XCTAssertEqual(offsetHours(zman("2026-10-24", london, "sunset"), london), 1)
        XCTAssertEqual(offsetHours(zman("2026-10-25", london, "sunset"), london), 0)
    }

    func testDayLengthIsPlausibleEveryDayOfAYearInJerusalemAndNewYork() {
        for city in [jerusalem, newYork] {
            var offsets = Set<Double>()
            var flips = 0
            var last: Double?
            var d = D("2026-01-01")
            while d.year == 2026 {
                let z = Dictionary(uniqueKeysWithValues: svc.zmanim(for: d, city: city, ids: ["sunrise", "chatzos", "sunset"]).map { ($0.id, $0.time!) })
                let sr = z["sunrise"]!, ch = z["chatzos"]!, ss = z["sunset"]!
                XCTAssertTrue(sr < ch && ch < ss, "\(city.name) \(d)")
                XCTAssertTrue((9.0...15.5).contains(ss.timeIntervalSince(sr) / 3600), "\(city.name) \(d)")
                offsets.insert(offsetHours(sr, city))
                let o = offsetHours(ss, city)
                if let last, last != o { flips += 1 }
                last = o
                d = d.addingDays(1)
            }
            XCTAssertEqual(offsets.count, 2, city.name)
            XCTAssertEqual(flips, 2, city.name)
        }
    }
}
