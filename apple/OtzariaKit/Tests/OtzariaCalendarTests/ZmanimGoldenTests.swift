import XCTest
@testable import OtzariaCalendar

/// Parity with Otzaria's real zmanim output (Dart ground truth): 7 cities x 21 dates x every relevant registry entry.
final class ZmanimGoldenTests: XCTestCase {
    private let svc = CalendarService()
    private lazy var allIds = svc.zmanimRegistry.map(\.id)

    // Exact to the second: instants and shaah-zmanis arithmetic reproduce kosher_dart.
    private let toleranceSeconds = 0

    private struct Line { let city: String; let date: String; let id: String; let clock: String?; let label: String? }

    private func parse() throws -> [Line] {
        try Golden.lines("zmanim_full.txt.gz").map { l in
            let p = l.split(separator: "|", omittingEmptySubsequences: false).map(String.init)
            return p[3].hasPrefix("=") ? Line(city: p[0], date: p[1], id: p[2], clock: nil, label: String(p[3].dropFirst()))
                                       : Line(city: p[0], date: p[1], id: p[2], clock: p[3], label: nil)
        }
    }

    func testEveryCityDateMatchesOtzariaItemSetAndDisplayedTimes() throws {
        var failures: [String] = []
        var checked = 0, displayedMinuteMismatch = 0, polarDays = 0, levanaDstShifts = 0, polarCells = 0
        struct Key: Hashable { let city: String; let date: String }
        let groups = Dictionary(grouping: try parse()) { Key(city: $0.city, date: $0.date) }
        for (key, group) in groups.sorted(by: { ($0.key.city, $0.key.date) < ($1.key.city, $1.key.date) }) {
            let city = try XCTUnwrap(svc.findCity(key.city))
            let date = D(key.date)
            let oursList = svc.zmanim(for: date, city: city, ids: allIds)
            let ours = Dictionary(uniqueKeysWithValues: oursList.map { ($0.id, $0) })
            let theirs = Dictionary(uniqueKeysWithValues: group.map { ($0.id, $0) })
            // Known difference: where the sun never gets 16.1 degrees below the horizon (London in summer) KosherJava returns nil
            // for the degree-based zmanim while kosher_dart returns a clamped number.
            let noDawn16 = ours["alos72Degrees"].map { $0.time == nil } ?? false
            let polarSeason = city.latitude > 50 && (5...8).contains(date.month)
            if noDawn16 {
                XCTAssertTrue(polarSeason, "unexpected polar-like day \(key.city) \(key.date)")
                polarDays += 1
            }
            for (id, z) in ours where theirs[id] == nil && z.time != nil {
                failures.append("\(key.city) \(key.date) \(id): we return a time Otzaria does not show (\(z.displayText ?? ""))")
            }
            for (id, line) in theirs {
                guard let z = ours[id], let t = z.time else {
                    if polarSeason { polarCells += 1; continue }
                    failures.append("\(key.city) \(key.date) \(id): missing (Otzaria \(line.clock ?? line.label ?? ""))")
                    continue
                }
                checked += 1
                if let label = line.label {
                    if noDawn16 || z.displayText == label { continue }
                    // Otzaria adds a Duration to a DateTime in the MACHINE's zone (the ground truth was produced on an Israel machine),
                    // so a molad span crossing an Israel DST change shifts its wall clock by an hour. We add plain elapsed time.
                    if crossesIsraelDst(date: date, city: city, id: id) { levanaDstShifts += 1; continue }
                    failures.append("\(key.city) \(key.date) \(id): kidush levana '\(z.displayText ?? "")' != Otzaria '\(label)'")
                    continue
                }
                let dart = secondsOfDay(line.clock!)
                let c = svc.clock(t, city)
                let mine = c.h * 3600 + c.m * 60 + c.s
                let diff = (((mine - dart + 43200) % 86400) + 86400) % 86400 - 43200
                if abs(diff) > toleranceSeconds {
                    failures.append("\(key.city) \(key.date) \(id): \(c.h):\(c.m):\(c.s) vs Otzaria \(line.clock!) (diff \(diff)s)")
                    continue
                }
                let marker = dart % 60 < 30 ? "." : ":"
                let dartText = String(format: "%02d:%02d", dart / 3600, dart / 60 % 60) + marker
                let myText = (z.displayText ?? "").trimmingCharacters(in: CharacterSet(charactersIn: "\u{2066}\u{2069}"))
                if myText != dartText {
                    let s = dart % 60
                    let nearBoundary = s <= toleranceSeconds || s >= 60 - toleranceSeconds || abs(s - 30) <= toleranceSeconds
                    displayedMinuteMismatch += 1
                    if !nearBoundary { failures.append("\(key.city) \(key.date) \(id): displayed '\(myText)' vs Otzaria '\(dartText)' (\(line.clock!))") }
                }
            }
        }
        XCTAssertGreaterThan(checked, 11000, "only \(checked) cells compared")
        XCTAssertTrue(failures.isEmpty, "\(failures.count) failures (of \(checked) cells; \(displayedMinuteMismatch) boundary-case display diffs):\n" + failures.prefix(40).joined(separator: "\n"))
        print("parity: \(checked) cells compared, \(displayedMinuteMismatch) boundary-case display diffs, \(levanaDstShifts) kidush levana DST-quirk cells skipped, \(polarDays) polar-like days (\(polarCells) null cells)")
        XCTAssertLessThanOrEqual(levanaDstShifts, 40)
        XCTAssertLessThanOrEqual(polarDays, 12)
        XCTAssertLessThan(displayedMinuteMismatch * 50, checked)
    }

    private let jerusalemZone = CityZone.resolve("Asia/Jerusalem")

    private func levanaOffsetMs(_ id: String) -> Int64 {
        switch id {
        case "tchilasKidushLevana3": return 3 * 86_400_000
        case "tchilasKidushLevana7": return 7 * 86_400_000
        case "sofKidushLevanaMoldos": return ((14 * 24 + 18) * 3600 + 22 * 60 + 1) * 1000 + 666
        default: return 15 * 86_400_000
        }
    }

    /// True when the naive wall-clock span molad ... molad+offset contains an Israel DST change.
    private func crossesIsraelDst(date: CivilDate, city: CityInfo, id: String) -> Bool {
        let ctx = ZmanContext(city: city, date: date)
        // rawInstant carries the -2h reading; add it back to get the naive wall clock as a UTC value
        let start = KidushLevana.rawInstant(ctx.date, offsetMs: 0) + 7_200_000
        let end = KidushLevana.rawInstant(ctx.date, offsetMs: levanaOffsetMs(id)) + 7_200_000
        return jerusalemZone.offsetSeconds(atMs: start - 7_200_000) != jerusalemZone.offsetSeconds(atMs: end - 7_200_000)
    }

    func testToday2026_11_13JerusalemAndModiinIllitDefaultZmanimMatchTheResearchTable() {
        let date = D("2026-11-13")
        let jer = svc.findCity("ירושלים")!, modi = svc.findCity("מודיעין עילית")!
        let j = Dictionary(uniqueKeysWithValues: svc.zmanim(for: date, city: jer, ids: svc.defaultEnabledZmanim).map { ($0.id, $0) })
        let m = Dictionary(uniqueKeysWithValues: svc.zmanim(for: date, city: modi, ids: svc.defaultEnabledZmanim).map { ($0.id, $0) })

        XCTAssertNotNil(j["candleLighting"])
        XCTAssertNil(j["motzeiShabbos"])
        XCTAssertNil(j["achilasChametzGRA"])
        XCTAssertNil(j["fastEndItimLenient"])

        XCTAssertEqual(svc.hhmm(j["candleLighting"]!.time!, jer), "16:05")
        XCTAssertEqual(svc.hhmm(m["candleLighting"]!.time!, modi), "16:14")
        XCTAssertEqual(svc.hhmm(j["sunset"]!.time!, jer), "16:45")
        XCTAssertEqual(svc.hhmm(m["sunset"]!.time!, modi), "16:44")
        XCTAssertEqual(svc.hhmm(j["sunrise"]!.time!, jer), "06:05")
        XCTAssertEqual(svc.hhmm(j["sofZmanShmaGRA"]!.time!, jer), "08:44")
        XCTAssertEqual(svc.hhmm(j["chatzos"]!.time!, jer), "11:23")
        XCTAssertEqual(svc.hhmm(j["chatzosLayla"]!.time!, jer), "23:23")
        XCTAssertEqual(svc.hhmm(j["rt72Shavos"]!.time!, jer), "17:57")
        XCTAssertEqual(svc.hhmm(j["tzeitTikochinsky"]!.time!, jer), "16:59")
        XCTAssertEqual(svc.hhmm(j["tzeitItimLeVina"]!.time!, jer), "17:03")
        XCTAssertEqual(svc.utcOffsetSeconds(at: j["sunset"]!.time!, city: jer), 7200)
    }

    func testResultsComeInRegistryOrderAndOnlyForRequestedIds() {
        let ids = ["sunset", "alos72Degrees", "chatzos", "nonexistent", "candleLighting"]
        let r = svc.zmanim(for: D("2026-11-13"), city: svc.findCity("ירושלים")!, ids: ids)
        XCTAssertEqual(r.map(\.id), ["alos72Degrees", "chatzos", "sunset", "candleLighting"])
        XCTAssertTrue(r.allSatisfy { $0.time != nil })
    }

    func testIrrelevantDayDependentZmanimAreOmitted() {
        let jer = svc.findCity("ירושלים")!
        let tuesday = Set(svc.zmanim(for: D("2026-11-10"), city: jer, ids: allIds).map(\.id))
        for id in ["candleLighting", "motzeiShabbos", "motzeiYomTov", "achilasChametzGRA", "fastEndItimLenient"] {
            XCTAssertFalse(tuesday.contains(id), id)
        }
        XCTAssertTrue(tuesday.contains("sunset"))
    }

    func testDayDependentZmanimAppearExactlyOnTheirDays() {
        let jer = svc.findCity("ירושלים")!
        func ids(_ d: String) -> Set<String> { Set(svc.zmanim(for: D(d), city: jer, ids: allIds).map(\.id)) }

        let motzei = ids("2026-11-14")
        XCTAssertTrue(motzei.contains("motzeiShabbos"))
        XCTAssertTrue(motzei.contains("motzeiShabbosChazonIsh"))
        XCTAssertFalse(motzei.contains("candleLighting"))

        let erevPesach = ids("2026-04-01")
        XCTAssertTrue(erevPesach.contains("achilasChametzGRA"))
        XCTAssertTrue(erevPesach.contains("biurChametzMGAShavos"))
        XCTAssertTrue(erevPesach.contains("candleLighting"))

        let tammuz17 = ids("2026-07-02")
        XCTAssertTrue(tammuz17.contains("fastEndItimLenient"))
        XCTAssertTrue(tammuz17.contains("fastEndTikLenient"))

        XCTAssertFalse(ids("2026-09-21").contains("fastEndItimLenient")) // taanis but NOT Yom Kippur
    }

    func testDisplayValuesUseTheCityOffsetAndTheOtzariaMarkerFormat() {
        let z = svc.zmanim(for: D("2026-11-13"), city: svc.findCity("ירושלים")!, ids: ["candleLighting"])[0]
        XCTAssertEqual(z.displayText, "\u{2066}16:05:\u{2069}") // 16:05:32 -> seconds 30-59 give the ':' marker
    }
}
