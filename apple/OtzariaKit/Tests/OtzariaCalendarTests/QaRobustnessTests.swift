import XCTest
@testable import OtzariaCalendar

/// QA: environment independence, thread safety, performance, fuzzing, time zone data and day-transition boundaries.
final class QaRobustnessTests: XCTestCase {
    private let svc = CalendarService()
    private lazy var allIds = svc.zmanimRegistry.map(\.id)
    private lazy var jerusalem = svc.findCity("ירושלים")!

    // MARK: environment independence

    private let snapshotInputs = [
        ("ירושלים", "2026-11-13"), ("ירושלים", "2026-11-14"), ("ניו יורק", "2027-04-21"), ("לונדון", "2026-10-25"), ("לוס אנג'לס", "2027-10-11"),
        ("אדמונטון", "2026-12-21"), ("סידני", "2027-03-13"), ("ירושלים", "2028-03-12"), ("ירושלים", "2027-05-09"), ("פריז", "2026-12-04"),
    ]

    private func ms(_ d: Date?) -> String { d.map { String(Int64(($0.timeIntervalSince1970 * 1000).rounded())) } ?? "" }

    /// Everything the service can produce for a few tricky (city, date) pairs, serialized with explicit formatting.
    private func snapshot() -> String {
        var out = ""
        for (cityName, date) in snapshotInputs {
            let city = svc.findCity(cityName) ?? svc.cities[0]
            let d = D(date)
            let i = svc.dayInfo(for: d, city: city)
            out += "\(city.name)|\(date)|\(i.hebrewDate)|\(i.hebrewDayOfWeek)|\(i.gregorianDate)|\(i.parasha ?? "")|\(i.holidays.joined(separator: ";"))|"
            out += "\(i.dafYomiBavli ?? "")|\(i.dafYomiYerushalmi ?? "")|\(i.omer ?? "")|\(ms(i.candleLighting))|\(ms(i.havdalah))|\(i.isShabbat)\(i.isYomTov)\n"
            for z in svc.zmanim(for: d, city: city, ids: allIds) { out += "\(z.id)=\(ms(z.time))/\(svc.utcOffsetSeconds(at: z.time ?? Date(timeIntervalSince1970: 0), city: city))/\(z.displayText ?? "")\n" }
            out += "\(svc.effectiveDate(now: at(2026, 11, 13, 17, 30, offset: 2), city: city, dayTransition: "sunset"))\n"
            out += svc.calendarHeaderDate(for: d, city: city) + "\n"
        }
        return out
    }

    /// The device's default time zone, locale and calendar must not influence any output.
    func testOutputsAreIdenticalUnderOtherDefaultTimeZones() {
        let original = NSTimeZone.default
        defer { NSTimeZone.default = original }
        NSTimeZone.default = TimeZone(identifier: "UTC")!
        let baseline = snapshot()
        XCTAssertTrue(baseline.contains("ג׳ כסלו תשפ״ז"))
        for id in ["Asia/Jerusalem", "America/Los_Angeles", "Pacific/Auckland", "Asia/Tokyo", "Pacific/Kiritimati", "Pacific/Pago_Pago"] {
            NSTimeZone.default = TimeZone(identifier: id)!
            let other = snapshot()
            if other != baseline {
                let a = baseline.components(separatedBy: "\n"), b = other.components(separatedBy: "\n")
                let i = (0..<min(a.count, b.count)).first { a[$0] != b[$0] }!
                XCTFail("output differs under default zone \(id) at line \(i): '\(a[i])' vs '\(b[i])'")
            }
        }
    }

    /// Library code that reads the device zone (Daf Yomi) must not leak it: 16 years of days under UTC+14 / UTC-11 equal UTC.
    func testDayInfoOverYearsIsIndependentOfTheDefaultTimeZone() {
        let original = NSTimeZone.default
        defer { NSTimeZone.default = original }
        func run() -> [DayInfo] {
            var d = D("2023-09-01")
            var out: [DayInfo] = []
            while d < D("2039-09-01") {
                out.append(svc.dayInfo(for: d, city: jerusalem))
                d = d.addingDays(5)
            }
            return out
        }
        NSTimeZone.default = TimeZone(identifier: "UTC")!
        let baseline = run()
        for id in ["Pacific/Kiritimati", "Pacific/Auckland", "Pacific/Pago_Pago"] {
            NSTimeZone.default = TimeZone(identifier: id)!
            XCTAssertEqual(run(), baseline, id)
        }
    }

    // MARK: thread safety

    func testConcurrentCallsGiveTheSameAnswersAsSerialCalls() {
        let cities = svc.cities.enumerated().filter { $0.offset % 7 == 0 }.map(\.element)
        let dates = (0..<12).map { D("2026-09-01").addingDays($0 * 29) }
        func key(_ c: CityInfo, _ d: CivilDate) -> String {
            let i = svc.dayInfo(for: d, city: c)
            var s = i.hebrewDate + (i.parasha ?? "") + i.holidays.joined(separator: ";") + (i.dafYomiBavli ?? "") + (i.omer ?? "") + ms(i.candleLighting) + ms(i.havdalah)
            for z in svc.zmanim(for: d, city: c, ids: allIds) { s += z.id + ms(z.time) + (z.displayText ?? "") }
            s += "\(svc.effectiveDate(now: at(d.year, d.month, d.day, 15, 0, offset: 0), city: c, dayTransition: "tzais"))"
            return s
        }
        var expected: [String: String] = [:]
        for c in cities { for d in dates { expected["\(c.name)|\(d)"] = key(c, d) } }

        let lock = NSLock()
        var errors: [String] = []
        DispatchQueue.concurrentPerform(iterations: 32) { worker in
            var rng = SystemRandomNumberGenerator()
            for _ in 0..<60 {
                let c = cities[Int.random(in: 0..<cities.count, using: &rng)]
                let d = dates[Int.random(in: 0..<dates.count, using: &rng)]
                if key(c, d) != expected["\(c.name)|\(d)"] {
                    lock.lock(); errors.append("\(c.name) \(d): differs under concurrency (worker \(worker))"); lock.unlock()
                }
            }
        }
        XCTAssertTrue(errors.isEmpty, errors.prefix(10).joined(separator: "\n"))
    }

    // MARK: performance

    /// Targets (release-like speed is not required here, the bounds are generous for a debug build):
    /// all 98 zmanim well under 20 ms, a day info under 5 ms, an effective date under 5 ms.
    func testPerformanceTargets() {
        let ny = svc.findCity("ניו יורק")!
        for _ in 0..<5 { _ = svc.zmanim(for: D("2026-11-13"), city: jerusalem, ids: allIds); _ = svc.dayInfo(for: D("2026-11-13"), city: ny) }

        func median(_ f: (Int) -> Void) -> Double {
            var samples: [Double] = []
            for _ in 0..<7 {
                let start = DispatchTime.now().uptimeNanoseconds
                for i in 0..<20 { f(i) }
                samples.append(Double(DispatchTime.now().uptimeNanoseconds - start) / 1e6 / 20)
            }
            return samples.sorted()[samples.count / 2]
        }
        let base = D("2026-11-13")
        let zmanim = median { _ = svc.zmanim(for: base.addingDays($0 % 9), city: $0 % 2 == 0 ? jerusalem : ny, ids: allIds) }
        let dayInfo = median { _ = svc.dayInfo(for: base.addingDays($0 % 9), city: $0 % 2 == 0 ? jerusalem : ny) }
        let defaults = median { _ = svc.zmanim(for: base.addingDays($0 % 9), city: jerusalem, ids: svc.defaultEnabledZmanim) }
        let effective = median { _ = svc.effectiveDate(now: at(2026, 11, 13, 12, 0, offset: 0).addingTimeInterval(Double($0) * 17 * 60), city: jerusalem, dayTransition: "tzais") }
        let moments = median { _ = svc.refreshMoments(after: at(2026, 11, 13, 12, 0, offset: 0).addingTimeInterval(Double($0) * 3600), count: 8, city: jerusalem, ids: svc.defaultEnabledZmanim, dayTransition: "sunset") }
        print(String(format: "zmanim(98) %.2f ms, zmanim(31 defaults) %.2f ms, dayInfo %.2f ms, effectiveDate %.3f ms, refreshMoments(8) %.2f ms (medians)", zmanim, defaults, dayInfo, effective, moments))
        XCTAssertLessThan(zmanim, 20, "zmanim(98) \(zmanim) ms")
        XCTAssertLessThan(dayInfo, 5, "dayInfo \(dayInfo) ms")
        XCTAssertLessThan(effective, 5, "effectiveDate \(effective) ms")
    }

    // MARK: fuzzing

    func testFuzzEveryCityRandomDates1900To2100NeverFails() {
        var rng = SeededGenerator(seed: 20261113)
        let edge = [D("1900-01-01"), D("2100-12-31"), D("1923-09-10"), D("1923-09-11"), D("2000-02-29"), D("2026-03-27"), D("2026-10-25")]
        let transitions = ["sunset", "tzais", "rabbeinuTam", "midnight", "", "garbage"]
        for city in svc.cities {
            let dates = edge + (0..<12).map { _ in CivilDate(year: 1900, month: 1, day: 1).addingDays(Int.random(in: 0..<73000, using: &rng)) }
            for d in dates {
                let z = svc.zmanim(for: d, city: city, ids: allIds)
                for x in z where x.time != nil { XCTAssertNotNil(x.displayText) }
                let info = svc.dayInfo(for: d, city: city)
                XCTAssertFalse(info.hebrewDate.isEmpty, "\(city.name) \(d)")
                let now = Date(timeIntervalSince1970: Double(d.epochDay) * 86400 + Double(Int.random(in: 0..<24, using: &rng) * 3600 + Int.random(in: 0..<60, using: &rng) * 60))
                for t in transitions { _ = svc.effectiveDate(now: now, city: city, dayTransition: t) }
            }
        }
    }

    func testEmptyDuplicateAndUnknownIdsAreHarmless() {
        let d = D("2026-11-13")
        XCTAssertTrue(svc.zmanim(for: d, city: jerusalem, ids: []).isEmpty)
        XCTAssertTrue(svc.zmanim(for: d, city: jerusalem, ids: ["", "nope", "SUNSET", " sunset"]).isEmpty)
        let r = svc.zmanim(for: d, city: jerusalem, ids: ["sunset", "sunset", "zzz", "alos72Degrees"])
        XCTAssertEqual(r.map(\.id), ["alos72Degrees", "sunset"])
        XCTAssertNil(svc.findCity(nil))
        XCTAssertNil(svc.findCity(""))
        XCTAssertNil(svc.findCity("אטלנטיס"))
    }

    func testExtremeDatesDegradeInsteadOfFailing() {
        let min = CivilDate(year: 1, month: 1, day: 1), max = CivilDate(year: 9999, month: 12, day: 31)
        XCTAssertTrue(svc.zmanim(for: min, city: jerusalem, ids: allIds).isEmpty)
        XCTAssertTrue(svc.zmanim(for: max, city: jerusalem, ids: allIds).isEmpty)
        XCTAssertEqual(svc.dayInfo(for: max, city: jerusalem).date, max)
        XCTAssertEqual(svc.dayInfo(for: min, city: jerusalem).gregorianDate, "01/01/0001")
        _ = svc.effectiveDate(now: .distantPast, city: jerusalem, dayTransition: "sunset")
        _ = svc.effectiveDate(now: .distantFuture, city: jerusalem, dayTransition: "midnight")
        _ = svc.effectiveDate(now: .distantFuture, city: jerusalem, dayTransition: "sunset")
        XCTAssertTrue(svc.refreshMoments(after: .distantFuture, count: 3, city: jerusalem, ids: allIds, dayTransition: "sunset").count <= 3)
    }

    // MARK: time zones

    func testEveryCityZoneResolvesToARealZoneNotTheLongitudeFallback() {
        for city in svc.cities { XCTAssertFalse(CityZone.resolve(city).usesFallback, "\(city.name) (\(city.timeZoneId)) fell back to a fixed offset") }
    }

    /// tzdata 2026: Alberta moves to permanent UTC-6 and British Columbia to permanent UTC-7 on 2026-11-01. A stale OS zone
    /// must not shift every zman of Edmonton / Calgary / Vancouver by an hour.
    func testAlbertaAndBritishColumbiaFollowTzdata2026() {
        let cases: [(String, String, Int)] = [
            ("America/Edmonton", "2026-03-07", -7), ("America/Edmonton", "2026-07-01", -6), ("America/Edmonton", "2026-10-31", -6),
            ("America/Edmonton", "2026-11-13", -6), ("America/Edmonton", "2027-01-15", -6), ("America/Edmonton", "2027-07-15", -6),
            ("America/Vancouver", "2026-03-07", -8), ("America/Vancouver", "2026-07-01", -7), ("America/Vancouver", "2026-11-13", -7),
            ("America/Vancouver", "2027-01-15", -7), ("America/Vancouver", "2027-07-15", -7), ("America/Denver", "2026-11-13", -7),
        ]
        for (iana, date, hours) in cases {
            let zone = CityZone.resolve(iana)
            XCTAssertEqual(zone.offsetSeconds(atMs: zone.startOfDayMs(D(date)) + 12 * 3_600_000), hours * 3600, "\(iana) \(date)")
        }
    }

    func testEdmontonZmanimCarryThePermanentOffset() throws {
        let edmonton = try XCTUnwrap(svc.findCity("אדמונטון"))
        let sunset = svc.zman("2026-11-13", edmonton, "sunset")
        XCTAssertEqual(svc.utcOffsetSeconds(at: sunset, city: edmonton), -6 * 3600)
        let c = svc.clock(sunset, edmonton)
        XCTAssertEqual(c.h, 17) // Otzaria: 17:45:40 (permanent UTC-6), not 16:45
        XCTAssertEqual(c.m, 45)
        let vancouver = try XCTUnwrap(svc.findCity("ונקובר"))
        XCTAssertEqual(svc.utcOffsetSeconds(at: svc.zman("2026-11-13", vancouver, "sunset"), city: vancouver), -7 * 3600)
    }

    /// Both resolution paths agree on the permanent-offset cut-over instant (2026-11-01 00:00 local).
    func testAlbertaCutOverHasNoOffsetChangeAtTheSwitchItself() {
        let zone = CityZone.resolve("America/Edmonton")
        let start = zone.startOfDayMs(D("2026-11-01"))
        XCTAssertEqual(zone.offsetSeconds(atMs: start - 1000), -6 * 3600)
        XCTAssertEqual(zone.offsetSeconds(atMs: start), -6 * 3600)
        XCTAssertEqual(zone.local(atMs: start).date, D("2026-11-01"))
        XCTAssertEqual(zone.local(atMs: start - 1000).date, D("2026-10-31"))
    }

    // MARK: day transition boundaries (all four transitions, every city, DST days, high latitude)

    func testTheTransitionInstantFlipsTheEffectiveDateExactlyForAllCities() {
        for date in ["2026-03-27", "2026-03-29", "2026-10-25", "2026-11-01", "2026-11-13", "2027-06-21", "2027-12-21"] {
            let civil = D(date)
            var failures: [String] = []
            for city in svc.cities {
                let zone = svc.zone(for: city)
                for mode in ["sunset", "tzais", "rabbeinuTam"] {
                    guard let instant = svc.dayTransitionInstant(civilDate: civil, city: city, dayTransition: mode) else {
                        // no tzais / sunset today: Otzaria keeps the civil date all day
                        for hour in [1, 12, 23] {
                            let t = Date(timeIntervalSince1970: Double(zone.startOfDayMs(civil)) / 1000 + Double(hour * 3600))
                            if svc.effectiveDate(now: t, city: city, dayTransition: mode) != civil { failures.append("\(city.name) \(date) \(mode): nil transition but date flipped at \(hour):00") }
                        }
                        continue
                    }
                    if zone.local(atMs: ZmanTimeFormatter.ms(instant)).date != civil { continue } // after local midnight (polar-ish): belongs to the next civil day
                    let before = svc.effectiveDate(now: instant.addingTimeInterval(-1), city: city, dayTransition: mode)
                    let atT = svc.effectiveDate(now: instant, city: city, dayTransition: mode)
                    let after = svc.effectiveDate(now: instant.addingTimeInterval(1), city: city, dayTransition: mode)
                    if before != civil || atT != civil.addingDays(1) || after != civil.addingDays(1) { failures.append("\(city.name) \(date) \(mode): \(before)/\(atT)/\(after)") }
                }
                // midnight: flips exactly at the city's civil midnight
                let m = Date(timeIntervalSince1970: Double(zone.startOfDayMs(civil.addingDays(1))) / 1000)
                if svc.effectiveDate(now: m.addingTimeInterval(-1), city: city, dayTransition: "midnight") != civil
                    || svc.effectiveDate(now: m, city: city, dayTransition: "midnight") != civil.addingDays(1) { failures.append("\(city.name) \(date) midnight") }
            }
            XCTAssertTrue(failures.isEmpty, "\(date): \(failures.count) failures:\n" + failures.prefix(15).joined(separator: "\n"))
        }
    }

    func testAmbiguousLocalHourAtDstEndNeverChangesTheCivilDate() {
        let london = svc.findCity("לונדון")!
        for offset in [1.0, 0.0] { // 2026-10-25 01:30 happens twice in London (BST then GMT)
            let now = at(2026, 10, 25, 1, 30, offset: offset)
            XCTAssertEqual(svc.effectiveDate(now: now, city: london, dayTransition: "sunset"), D("2026-10-25"))
            XCTAssertEqual(svc.effectiveDate(now: now, city: london, dayTransition: "midnight"), D("2026-10-25"))
        }
        XCTAssertEqual(svc.effectiveDate(now: at(2026, 3, 27, 3, 0, offset: 3), city: jerusalem, dayTransition: "tzais"), D("2026-03-27"))
        XCTAssertEqual(svc.effectiveDate(now: at(2026, 3, 27, 0, 0, offset: 2), city: jerusalem, dayTransition: "tzais"), D("2026-03-27"))
    }

    func testHighLatitudeWithoutTzaisKeepsTheCivilDateAndTheOtherTransitionsStillWork() throws {
        let reykjavik = try XCTUnwrap(svc.findCity("רייקיאוויק"))
        let date = D("2027-06-21")
        XCTAssertNil(svc.dayTransitionInstant(civilDate: date, city: reykjavik, dayTransition: "tzais")) // the sun never gets 8.5 degrees below the horizon
        for hour in [0, 6, 12, 18, 23] {
            XCTAssertEqual(svc.effectiveDate(now: at(2027, 6, 21, hour, 30, offset: 0), city: reykjavik, dayTransition: "tzais"), date)
        }
        let tzais = svc.zmanim(for: date, city: reykjavik, ids: ["tzeitGeonim8point5", "sunset"])
        XCTAssertTrue(tzais.contains { $0.id == "tzeitGeonim8point5" && $0.time == nil && $0.displayText == nil })
        XCTAssertTrue(tzais.contains { $0.id == "sunset" })
    }
}

/// Deterministic generator so fuzz failures are reproducible.
struct SeededGenerator: RandomNumberGenerator {
    private var state: UInt64
    init(seed: UInt64) { state = seed }
    mutating func next() -> UInt64 {
        state &+= 0x9E3779B97F4A7C15
        var z = state
        z = (z ^ (z >> 30)) &* 0xBF58476D1CE4E5B9
        z = (z ^ (z >> 27)) &* 0x94D049BB133111EB
        return z ^ (z >> 31)
    }
}
