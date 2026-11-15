import XCTest
@testable import OtzariaCalendar

final class CityTests: XCTestCase {
    private let svc = CalendarService()

    func testTableHas129Cities55InIsraelWithUniqueNames() {
        XCTAssertEqual(svc.cities.count, 129)
        XCTAssertEqual(svc.cities.filter(\.isInIsrael).count, 55)
        XCTAssertEqual(Set(svc.cities.map(\.name)).count, 129)
        XCTAssertEqual(Set(svc.cities.map(\.timeZoneId)).count, 52)
        for c in svc.cities where c.isInIsrael {
            XCTAssertEqual(c.country, "ארץ ישראל")
            XCTAssertEqual(c.timeZoneId, "Asia/Jerusalem")
        }
        for c in svc.cities where !c.isInIsrael { XCTAssertNotEqual(c.country, "ארץ ישראל") }
    }

    func testOrderIsOtzariasSourceOrderIsraelFirst() {
        XCTAssertEqual(svc.cities[0].name, "אופקים")
        XCTAssertTrue(svc.cities.prefix(55).allSatisfy(\.isInIsrael))
        XCTAssertEqual(CityTable.defaultCityName, "ירושלים")
        XCTAssertNotNil(svc.findCity(CityTable.defaultCityName))
    }

    func testEveryCityHasAValidRealTimeZoneConsistentWithItsLongitude() {
        let probe = Date(timeIntervalSince1970: Double(D("2026-01-15").epochDay) * 86400 + 43200)
        for c in svc.cities {
            let zone = CityZone.resolve(c)
            XCTAssertFalse(zone.usesFallback, "\(c.name): IANA id \(c.timeZoneId) did not resolve")
            let hours = Double(zone.offsetSeconds(at: probe)) / 3600
            XCTAssertLessThanOrEqual(abs(hours - c.longitude / 15), 3.5, "\(c.name): offset \(hours) vs longitude \(c.longitude)")
            XCTAssertTrue((-60.0...70.0).contains(c.latitude))
            XCTAssertTrue((-180.0...180.0).contains(c.longitude))
        }
    }

    func testUnknownTimeZoneIdFallsBackToALongitudeOffsetWithoutFailing() {
        let zone = CityZone.resolve("Mars/Olympus_Mons", longitude: 35)
        XCTAssertTrue(zone.usesFallback)
        XCTAssertEqual(zone.offsetSeconds(atMs: 0), 2 * 3600)
    }

    func testCandleLightingMinutesPerCity() {
        let expected: [(String, Int)] = [
            ("ירושלים", 40), ("מודיעין עילית", 30), ("בני ברק", 30), ("חיפה", 30), ("בית שמש", 30), ("תל אביב", 18),
            ("אשדוד", 18), ("ניו יורק", 18), ("לונדון", 18), ("אופקים", 20), ("חצור הגלילית", 25), ("ביתר עילית", 40),
        ]
        for (name, minutes) in expected { XCTAssertEqual(svc.findCity(name)?.candleLightingMinutes, minutes, name) }
    }

    func testCandleLightingFallbacksByCountryThen30() {
        let us = svc.cities.filter { $0.country == "ארצות הברית" }
        XCTAssertFalse(us.isEmpty)
        XCTAssertTrue(us.allSatisfy { $0.candleLightingMinutes == 18 })
        XCTAssertEqual(svc.findCity("פראג")?.candleLightingMinutes ?? 30, 30)
    }

    func testFindCityExactTrimAndGereshVariants() throws {
        let la = try XCTUnwrap(svc.cities.first { $0.name.hasPrefix("לוס אנג") })
        XCTAssertEqual(svc.findCity(la.name), la)
        XCTAssertEqual(svc.findCity("  " + la.name + "  "), la)
        XCTAssertEqual(svc.findCity("לוס אנג\u{2019}לס"), la) // typographic apostrophe
        XCTAssertEqual(svc.findCity("לוס אנג\u{05F3}לס"), la) // Hebrew geresh
        XCTAssertEqual(svc.findCity("לוס  אנג'לס"), la)       // doubled space
        let kiryat = try XCTUnwrap(svc.cities.first { $0.name.hasPrefix("קרית יערים") })
        XCTAssertEqual(svc.findCity("\u{200F}קרית יערים (טלז\u{05BE}סטון)\u{200F}"), kiryat) // RLM marks + maqaf instead of hyphen
        XCTAssertNil(svc.findCity("אטלנטיס"))
        XCTAssertNil(svc.findCity(""))
        XCTAssertNil(svc.findCity("   "))
        XCTAssertNil(svc.findCity(nil))
    }

    /// The embedded table equals the Windows build's JSON (docs/research/otzaria-cities.json) when the repo docs are next to the tests.
    func testEmbeddedTableEqualsDocsResearchJson() throws {
        var dir = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
        var found: URL?
        for _ in 0..<8 {
            let candidate = dir.appendingPathComponent("docs/research/otzaria-cities.json")
            if FileManager.default.fileExists(atPath: candidate.path) { found = candidate; break }
            dir.deleteLastPathComponent()
        }
        guard let found else { throw XCTSkip("docs/research/otzaria-cities.json not found next to the sources") }
        let root = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(contentsOf: found)) as? [String: Any])
        let json = try XCTUnwrap(root["cities"] as? [[String: Any]])
        XCTAssertEqual(json.count, svc.cities.count)
        for (j, c) in zip(json, svc.cities) {
            XCTAssertEqual(j["name"] as? String, c.name)
            XCTAssertEqual(j["country"] as? String, c.country)
            XCTAssertEqual(j["lat"] as? Double, c.latitude)
            XCTAssertEqual(j["lng"] as? Double, c.longitude)
            XCTAssertEqual(j["elevation"] as? Double, c.elevation)
            XCTAssertEqual(j["timezone"] as? String, c.timeZoneId)
            XCTAssertEqual(j["inIsrael"] as? Bool, c.isInIsrael)
            XCTAssertEqual(j["candleLightingMinutes"] as? Int, c.candleLightingMinutes)
        }
    }

    /// Validates the embedded table against Otzaria's own Dart source when a checkout is available.
    func testEmbeddedTableMatchesOtzariaCalendarLocationDartWhenAvailable() throws {
        let candidates = [
            ProcessInfo.processInfo.environment["OTZARIA_SOURCE_DIR"].map { $0 + "/lib/tools/calendar/models/calendar_location.dart" },
            "/Volumes/T7/Development Projects/otzaria/lib/tools/calendar/models/calendar_location.dart",
        ].compactMap { $0 }
        guard let path = candidates.first(where: { FileManager.default.fileExists(atPath: $0) }) else {
            throw XCTSkip("no Otzaria source checkout available")
        }
        let text = try String(contentsOfFile: path, encoding: .utf8)
        func unescape(_ s: String) -> String { s.replacingOccurrences(of: "\\'", with: "'") }
        func first(_ pattern: String, in line: String) -> [String]? {
            guard let re = try? NSRegularExpression(pattern: pattern),
                  let m = re.firstMatch(in: line, range: NSRange(line.startIndex..., in: line)) else { return nil }
            return (1..<m.numberOfRanges).map { String(line[Range(m.range(at: $0), in: line)!]) }
        }
        var country = "", city = ""
        var lat = 0.0, lng = 0.0, elev = 0.0
        var rows: [(country: String, name: String, lat: Double, lng: Double, elev: Double, tz: String)] = []
        var candle: [String: Int] = [:], countryCandle: [String: Int] = [:]
        var inCandle = false, inCountryCandle = false
        for line in text.components(separatedBy: .newlines) {
            if line.hasPrefix("const Map<String, int> candleLightingMinutesByCity") { inCandle = true; continue }
            if line.hasPrefix("const Map<String, int> candleLightingMinutesByCountry") { inCandle = false; inCountryCandle = true; continue }
            if inCandle || inCountryCandle {
                if let m = first(#"^\s+'((?:[^'\\]|\\.)*)':\s*(\d+),"#, in: line) {
                    if inCandle { candle[unescape(m[0])] = Int(m[1])! } else { countryCandle[unescape(m[0])] = Int(m[1])! }
                }
                if line.hasPrefix("};") { inCandle = false; inCountryCandle = false }
                continue
            }
            if let m = first(#"^  '((?:[^'\\]|\\.)*)': \{$"#, in: line) { country = unescape(m[0]); continue }
            if let m = first(#"^    '((?:[^'\\]|\\.)*)': \{$"#, in: line) { city = unescape(m[0]); continue }
            if let m = first(#"^      'lat': (-?[\d.]+),"#, in: line) { lat = Double(m[0])! }
            else if let m = first(#"^      'lng': (-?[\d.]+),"#, in: line) { lng = Double(m[0])! }
            else if let m = first(#"^      'elevation': (-?[\d.]+),"#, in: line) { elev = Double(m[0])! }
            else if let m = first(#"^      'timezone': '([^']+)',"#, in: line) { rows.append((country, city, lat, lng, elev, m[0])) }
        }
        XCTAssertEqual(rows.count, svc.cities.count)
        for (d, c) in zip(rows, svc.cities) {
            XCTAssertEqual(d.name, c.name)
            XCTAssertEqual(d.country, c.country)
            XCTAssertEqual(d.lat, c.latitude)
            XCTAssertEqual(d.lng, c.longitude)
            XCTAssertEqual(d.elev, c.elevation)
            XCTAssertEqual(d.tz, c.timeZoneId)
            XCTAssertEqual(d.country == "ארץ ישראל", c.isInIsrael)
            XCTAssertEqual(candle[d.name] ?? countryCandle[d.country] ?? 30, c.candleLightingMinutes, d.name)
        }
    }
}
