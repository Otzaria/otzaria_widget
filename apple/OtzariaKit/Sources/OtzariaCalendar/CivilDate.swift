import Foundation

/// A Gregorian calendar day without a time zone (the Swift counterpart of .NET's `DateOnly`).
public struct CivilDate: Hashable, Comparable, Sendable, CustomStringConvertible {
    public let year: Int
    public let month: Int
    public let day: Int

    public init(year: Int, month: Int, day: Int) {
        self.year = year
        self.month = month
        self.day = day
    }

    /// Days since 1970-01-01 (proleptic Gregorian).
    public init(epochDay: Int) {
        let z = epochDay + 719468
        let era = (z >= 0 ? z : z - 146096) / 146097
        let doe = z - era * 146097
        let yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365
        let doy = doe - (365 * yoe + yoe / 4 - yoe / 100)
        let mp = (5 * doy + 2) / 153
        let d = doy - (153 * mp + 2) / 5 + 1
        let m = mp < 10 ? mp + 3 : mp - 9
        self.init(year: yoe + era * 400 + (m <= 2 ? 1 : 0), month: m, day: d)
    }

    public var epochDay: Int {
        let y = month <= 2 ? year - 1 : year
        let era = (y >= 0 ? y : y - 399) / 400
        let yoe = y - era * 400
        let doy = (153 * (month > 2 ? month - 3 : month + 9) + 2) / 5 + day - 1
        let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy
        return era * 146097 + doe - 719468
    }

    /// 1 = Sunday ... 7 = Saturday.
    public var dayOfWeek: Int {
        let r = (epochDay + 4) % 7 // 1970-01-01 was a Thursday
        return (r < 0 ? r + 7 : r) + 1
    }

    public func addingDays(_ n: Int) -> CivilDate { CivilDate(epochDay: epochDay + n) }

    public static func < (a: CivilDate, b: CivilDate) -> Bool { a.epochDay < b.epochDay }

    /// "yyyy-MM-dd".
    public var description: String { String(format: "%04d-%02d-%02d", year, month, day) }

    /// Parses "yyyy-MM-dd".
    public init?(_ text: String) {
        let p = text.split(separator: "-").compactMap { Int($0) }
        guard p.count == 3, (1...12).contains(p[1]), (1...31).contains(p[2]) else { return nil }
        self.init(year: p[0], month: p[1], day: p[2])
    }
}

func floorDiv(_ a: Int64, _ b: Int64) -> Int64 {
    let q = a / b
    return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q
}

func floorMod(_ a: Int64, _ b: Int64) -> Int64 { a - floorDiv(a, b) * b }
