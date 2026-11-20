import Compression
import Foundation
import XCTest
@testable import OtzariaCalendar

enum GoldenError: Error { case missing(String), badGzip(String) }

/// Reads a gzip file from the test bundle's Golden folder and returns its lines.
enum Golden {
    static func lines(_ name: String) throws -> [String] {
        guard let url = Bundle.module.url(forResource: name, withExtension: nil, subdirectory: "Golden") else { throw GoldenError.missing(name) }
        let data = try Data(contentsOf: url)
        let text = String(decoding: try gunzip(data, name), as: UTF8.self)
        return text.components(separatedBy: .newlines)
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
            .filter { !$0.isEmpty }
    }

    /// Minimal gzip reader: header parsing + raw DEFLATE through the Compression framework.
    static func gunzip(_ data: Data, _ name: String) throws -> Data {
        let b = [UInt8](data)
        guard b.count > 18, b[0] == 0x1f, b[1] == 0x8b, b[2] == 8 else { throw GoldenError.badGzip(name) }
        let flags = b[3]
        var pos = 10
        if flags & 4 != 0 { pos += 2 + Int(b[pos]) + Int(b[pos + 1]) << 8 }
        if flags & 8 != 0 { while b[pos] != 0 { pos += 1 }; pos += 1 }
        if flags & 16 != 0 { while b[pos] != 0 { pos += 1 }; pos += 1 }
        if flags & 2 != 0 { pos += 2 }
        let isize = Int(b[b.count - 4]) | Int(b[b.count - 3]) << 8 | Int(b[b.count - 2]) << 16 | Int(b[b.count - 1]) << 24
        let payload = Array(b[pos..<(b.count - 8)])
        var out = [UInt8](repeating: 0, count: max(isize, 1))
        let n = compression_decode_buffer(&out, out.count, payload, payload.count, nil, COMPRESSION_ZLIB)
        guard n == isize else { throw GoldenError.badGzip(name) }
        return Data(out)
    }
}

func D(_ s: String) -> CivilDate { CivilDate(s)! }

/// Seconds since local midnight of an "HH:mm:ss" value.
func secondsOfDay(_ hms: String) -> Int {
    let p = hms.split(separator: ":").map { Int($0)! }
    return p[0] * 3600 + p[1] * 60 + p[2]
}

extension CalendarService {
    func zman(_ date: String, _ city: CityInfo, _ id: String) -> Date {
        zmanim(for: D(date), city: city, ids: [id]).first!.time!
    }

    /// (hour, minute, second) of an instant in the city's zone.
    func clock(_ t: Date, _ city: CityInfo) -> (h: Int, m: Int, s: Int) {
        let s = zone(for: city).local(atMs: Int64((t.timeIntervalSince1970 * 1000).rounded(.down))).secondOfDay
        return (s / 3600, s / 60 % 60, s % 60)
    }

    func hhmm(_ t: Date, _ city: CityInfo) -> String { plainTime(t, city: city, twelveHour: false) }
}

/// An instant from a wall-clock reading with a fixed UTC offset (hours).
func at(_ y: Int, _ mo: Int, _ d: Int, _ h: Int, _ mi: Int, _ s: Int = 0, offset: Double) -> Date {
    Date(timeIntervalSince1970: Double(CivilDate(year: y, month: mo, day: d).epochDay) * 86400 + Double(h * 3600 + mi * 60 + s) - offset * 3600)
}

/// Time zone ground truth: offsets of every city zone at noon local on sample dates, from the Dart `timezone` package (tzdata 2026e).
enum ZoneGolden {
    struct Row { let iana: String; let date: CivilDate; let offset: Int }

    static func rows() throws -> [Row] {
        try Golden.lines("qa_timezones.txt.gz").map { l in
            let p = l.split(separator: "|").map(String.init)
            let ymd = p[1].split(separator: "-").map { Int($0)! }
            return Row(iana: p[0], date: CivilDate(year: ymd[0], month: ymd[1], day: ymd[2]), offset: Int(p[2])!)
        }
    }

    /// Our offset at noon local of the row's date.
    static func ours(_ r: Row) -> Int {
        let zone = CityZone.resolve(r.iana)
        return zone.offsetSeconds(atMs: zone.startOfDayMs(r.date) + 12 * 3_600_000)
    }

    /// Zones whose offsets from this process's OS tzdata differ from the golden (the OS data can be older than tzdata 2026e).
    static func driftZones() throws -> [String: [String]] {
        var drift: [String: [String]] = [:]
        for r in try rows() where ours(r) != r.offset {
            drift[r.iana, default: []].append("\(r.date): \(ours(r)) vs tzdata \(r.offset)")
        }
        return drift
    }
}
