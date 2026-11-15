import XCTest
@testable import OtzariaCalendar

/// QA second-opinion parity: all 129 cities x 8 dates x every relevant zman to the SECOND, registry/city tables, 1924-2100 Hebrew calendar.
final class QaParityTests: XCTestCase {
    private let svc = CalendarService()
    private lazy var allIds = svc.zmanimRegistry.map(\.id)

    /// Zmanim that depend on the sun reaching 16.1 / 19.8 / 26 / 10.2-11.5 degrees below the horizon (or derive from such a dawn).
    /// kosher_dart returns a clamped garbage time where the sun never gets that low; KosherJava returns nil and we show "--".
    private func isDegreeBased(_ id: String) -> Bool {
        id.hasSuffix("Degrees") || id.contains("16point1") || id.hasPrefix("misheyakir") || id == "plagAlosToSunset" || id == "plagAlos16ToGeonim"
    }

    func testAllCitiesMatchOtzariaToTheSecondOnEightDates() throws {
        let lines = try Golden.lines("qa_zmanim_all_cities.txt.gz")
        XCTAssertGreaterThan(lines.count, 80000, "golden too small: \(lines.count)")
        struct Key: Hashable { let city: String; let date: String }
        let rows = lines.map { $0.split(separator: "|", omittingEmptySubsequences: false).map(String.init) }
        let groups = Dictionary(grouping: rows) { Key(city: $0[0], date: $0[1]) }
        var failures: [String] = []
        var garbageCells = 0, nearDegenerate = 0, compared = 0, driftCells = 0
        // The OS tzdata can be older than the tzdata 2026e behind the golden (see testZoneOffsets...).
        let drift = Set(try ZoneGolden.driftZones().keys)
        for (key, group) in groups.sorted(by: { ($0.key.city, $0.key.date) < ($1.key.city, $1.key.date) }) {
            let city = try XCTUnwrap(svc.findCity(key.city), key.city)
            let drifting = drift.contains(city.timeZoneId)
            let date = D(key.date)
            let ours = Dictionary(uniqueKeysWithValues: svc.zmanim(for: date, city: city, ids: allIds)
                .filter { !$0.id.contains("KidushLevana") }.map { ($0.id, $0) })
            let theirIds = Set(group.map { $0[2] })
            for p in group {
                let id = p[2]
                guard let z = ours[id], let t = z.time else {
                    if isDegreeBased(id) && city.latitude > 35 && (3...9).contains(date.month) { garbageCells += 1; continue }
                    failures.append("\(p[0]) \(p[1]) \(id): missing (Otzaria \(p[3]))")
                    continue
                }
                let c = svc.clock(t, city)
                let mine = String(format: "%02d:%02d:%02d", c.h, c.m, c.s)
                if drifting {
                    // stale OS zone data shifts the wall clock by whole hours only: the instant must still agree to the second
                    driftCells += 1
                    let delta = ((secondsOfDay(mine) - secondsOfDay(p[3])) % 86400 + 86400) % 86400
                    if delta % 3600 != 0 { failures.append("\(p[0]) \(p[1]) \(id): \(mine) vs Otzaria \(p[3]) (not a whole-hour shift)") }
                    continue
                }
                compared += 1
                if mine == p[3] { continue }
                // alos/plag 26 degrees at Warsaw on 21 April: the sun barely reaches -26 degrees, acos is ill-conditioned
                let diff = abs(secondsOfDay(mine) - secondsOfDay(p[3]))
                if diff <= 60 && city.latitude > 50 && isDegreeBased(id) { nearDegenerate += 1; continue }
                failures.append("\(p[0]) \(p[1]) \(id): \(mine) vs Otzaria \(p[3])")
            }
            for extra in ours.values where extra.time != nil && !theirIds.contains(extra.id) {
                failures.append("\(city.name) \(key.date) \(extra.id): we return a time Otzaria does not (\(extra.displayText ?? ""))")
            }
        }
        print("\(driftCells) cells compared modulo whole hours for OS tzdata drift in zones \(drift.sorted())")
        print("\(compared) cells identical to the second, \(garbageCells) high-latitude cells where kosher_dart returns garbage and we return nil, \(nearDegenerate) ill-conditioned cells")
        XCTAssertTrue(failures.isEmpty, "\(failures.count) mismatches:\n" + failures.prefix(40).joined(separator: "\n"))
        XCTAssertTrue((0...800).contains(garbageCells), "\(garbageCells)")
        XCTAssertTrue((0...5).contains(nearDegenerate), "\(nearDegenerate)")
        XCTAssertGreaterThan(compared, 79000, "only \(compared) cells compared")
        XCTAssertTrue(drift.isSubset(of: ["Africa/Casablanca"]), "unexpected tzdata drift: \(drift)")
        XCTAssertLessThanOrEqual(driftCells, 700, "\(driftCells) drift cells")
    }

    /// Offsets of every zone at 13 sample dates from tzdata 2026e. Only Africa/Casablanca is a known exception: Foundation inside
    /// `swift test` (binary linked against the macOS 14 SDK) still reports the pre-2026e Morocco rules; a normal process reports +0.
    func testZoneOffsetsMatchTheTzdataOtzariaUses() throws {
        let drift = try ZoneGolden.driftZones()
        print("zone drift vs tzdata 2026e in this process:", drift)
        XCTAssertTrue(Set(drift.keys).isSubset(of: ["Africa/Casablanca"]), "\(drift)")
    }

    func testRegistryMatchesOtzariaFieldByField() throws {
        let gold = try Golden.lines("qa_registry.txt.gz").map { $0.split(separator: "|", omittingEmptySubsequences: false).map(String.init) }
        XCTAssertEqual(gold.count, 98)
        let detail = svc.zmanimRegistryDetails
        XCTAssertEqual(detail.count, 98)
        for (g, e) in zip(gold, detail) {
            XCTAssertEqual(e.id, g[0])
            XCTAssertEqual(e.title, g[1])
            XCTAssertEqual(e.subtitle, g[2])
            XCTAssertEqual(e.category, g[3])
            XCTAssertEqual(e.defaultEnabled, g[4] == "1")
            XCTAssertEqual(e.showHebrewDate, g[5] == "1")
            XCTAssertEqual(e.pairId, g[7].isEmpty ? nil : g[7])
            XCTAssertEqual(e.pairLabel, g[8])
            XCTAssertEqual(e.description, g[9]) // first line of Otzaria's explanation (Dart escape \' already resolved)
            XCTAssertFalse(e.description.contains("\\"))
        }
        for (i, g) in gold.enumerated() {
            XCTAssertEqual(svc.zmanimRegistry[i].title, g[1] + (g[2].isEmpty ? "" : " — " + g[2]))
        }
        XCTAssertEqual(gold.filter { $0[5] == "1" }.map { $0[0] },
                       ["tchilasKidushLevana3", "tchilasKidushLevana7", "sofKidushLevanaMoldos", "sofKidushLevana15"])
        XCTAssertEqual(gold.filter { $0[5] == "1" && $0[6] == "1" }.map { $0[0] }, ["tchilasKidushLevana3", "tchilasKidushLevana7"])
    }

    func testCityTableMatchesOtzariaForAll129Cities() throws {
        let gold = try Golden.lines("qa_cities.txt.gz").map { $0.split(separator: "|", omittingEmptySubsequences: false).map(String.init) }
        XCTAssertEqual(gold.count, 129)
        XCTAssertEqual(svc.cities.count, 129)
        for g in gold {
            let c = try XCTUnwrap(svc.findCity(g[0]), g[0])
            XCTAssertEqual(c.country, g[1])
            XCTAssertEqual(c.latitude, Double(g[2]))
            XCTAssertEqual(c.longitude, Double(g[3]))
            XCTAssertEqual(c.elevation, Double(g[4]))
            XCTAssertEqual(c.timeZoneId, g[5])
            XCTAssertEqual(c.isInIsrael, g[6] == "1")
            XCTAssertEqual(c.candleLightingMinutes, Int(g[7]))
        }
    }

    func testHebrewCalendar1924To2100SampledMatchesOtzaria() throws {
        let dows = ["", "ראשון", "שני", "שלישי", "רביעי", "חמישי", "שישי", "שבת"]
        for israel in [true, false] {
            let city = try XCTUnwrap(svc.findCity(israel ? "ירושלים" : "ניו יורק"))
            var failures: [String] = []
            let rows = try Golden.lines(israel ? "qa_hebrew_israel.txt.gz" : "qa_hebrew_diaspora.txt.gz")
            XCTAssertGreaterThan(rows.count, 5000)
            for r in rows {
                let p = r.split(separator: "|", omittingEmptySubsequences: false).map(String.init) // date|header|dow|events|parasha|daf|omer|yom tov
                let date = D(p[0])
                let info = svc.dayInfo(for: date, city: city)
                let dow = date.dayOfWeek

                let header = svc.calendarHeaderDate(for: date, city: city)
                if header != p[1] { failures.append("\(p[0]) header '\(header)' != '\(p[1])'") }
                if dows[dow] != p[2] { failures.append("\(p[0]) dow") }
                if info.hebrewDayOfWeek != (dow == 7 ? "שבת קודש" : "יום " + p[2]) { failures.append("\(p[0]) dow name \(info.hebrewDayOfWeek)") }

                var events = p[3].isEmpty ? [] : p[3].components(separatedBy: ";")
                if dow == 7 && !p[4].isEmpty, let i = events.firstIndex(of: p[4]) { events.remove(at: i) } // parsha is kept in DayInfo.parasha
                let isFast: (String) -> Bool = { $0 == "שבעה עשר בתמוז" || $0 == "תשעה באב" }
                let fastOnShabbat = dow == 7 && events.contains(where: isFast) && !info.holidays.contains(where: isFast)
                if fastOnShabbat { events.removeAll(where: isFast) } // documented kosher_dart bug
                if info.holidays != events { failures.append("\(p[0]) holidays \(info.holidays) != \(events)") }

                if (info.parasha ?? "") != p[4] { failures.append("\(p[0]) parasha '\(info.parasha ?? "")' != '\(p[4])'") }
                if (info.dafYomiBavli ?? "") != p[5] { failures.append("\(p[0]) daf '\(info.dafYomiBavli ?? "")' != '\(p[5])'") }
                if (info.omer ?? "") != p[6] { failures.append("\(p[0]) omer '\(info.omer ?? "")' != '\(p[6])'") }
                if info.isYomTov != (p[7] == "1") { failures.append("\(p[0]) yom tov") }
            }
            XCTAssertTrue(failures.isEmpty, "\(israel ? "Israel" : "diaspora"): \(failures.count) mismatches:\n" + failures.prefix(30).joined(separator: "\n"))
        }
    }

    /// First day of each masechta of the 14th Bavli cycle (started 2020-01-05): two-word names, the Shekalim / Meilah / Kinnim /
    /// Tamid / Midos special cases and no geresh/gershayim in the daf number.
    func testBavliDafYomiSpecialMasechtot() {
        let cases = [
            ("2020-01-05", "ברכות ב"), ("2020-03-08", "שבת ב"), ("2021-03-23", "שקלים ב"), ("2021-10-11", "ראש השנה ב"),
            ("2022-01-14", "מועד קטן ב"), ("2023-11-04", "בבא קמא ב"), ("2024-03-01", "בבא מציעא ב"), ("2024-06-27", "בבא בתרא ב"),
            ("2025-06-20", "עבודה זרה ב"), ("2025-09-03", "הוריות ב"), ("2026-11-19", "ערכין ב"), ("2027-02-20", "מעילה ב"),
            ("2027-03-13", "קינים כג"), ("2027-03-16", "תמיד כו"), ("2027-03-24", "מידות לד"), ("2027-03-28", "נדה ב"),
            ("2026-11-13", "בכורות נו"),
        ]
        let jer = svc.findCity("ירושלים")!
        for (date, expected) in cases {
            let daf = svc.dayInfo(for: D(date), city: jer).dafYomiBavli
            XCTAssertEqual(daf, expected, date)
            XCTAssertFalse(daf?.contains(HebrewNumbers.geresh) ?? true)
            XCTAssertFalse(daf?.contains(HebrewNumbers.gershayim) ?? true)
        }
    }

    func testTheCycleHas40DistinctMasechtaNamesInARow() {
        let jer = svc.findCity("ירושלים")!
        var seen: [String] = []
        var d = D("2020-01-05")
        let end = D("2027-06-01")
        while d < end {
            let daf = svc.dayInfo(for: d, city: jer).dafYomiBavli!
            let masechta = String(daf[..<daf.lastIndex(of: " ")!])
            if seen.last != masechta { seen.append(masechta) }
            d = d.addingDays(1)
        }
        XCTAssertEqual(seen.count, 40)
        XCTAssertEqual(Set(seen).count, 40)
    }

    /// Yerushalmi is not shown by Otzaria; real-world anchors instead: the second cycle began Monday 14 Nov 2022 (Berachos 1)
    /// and there is no daf on Yom Kippur / Tisha B'Av.
    func testYerushalmiDafYomiAnchors() {
        let jer = svc.findCity("ירושלים")!
        XCTAssertEqual(svc.dayInfo(for: D("2022-11-14"), city: jer).dafYomiYerushalmi, "ברכות א")
        XCTAssertEqual(svc.dayInfo(for: D("2022-11-15"), city: jer).dafYomiYerushalmi, "ברכות ב")
        XCTAssertNil(svc.dayInfo(for: D("2026-09-21"), city: jer).dafYomiYerushalmi) // Yom Kippur 5787
        XCTAssertNil(svc.dayInfo(for: D("2026-07-23"), city: jer).dafYomiYerushalmi) // Tisha B'Av 5786
        XCTAssertNotNil(svc.dayInfo(for: D("2026-11-13"), city: jer).dafYomiYerushalmi)
    }
}
