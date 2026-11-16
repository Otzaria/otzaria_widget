import Foundation

/// The system zone for a city's IANA id, corrected for tzdata 2026 (Alberta UTC-6 / British Columbia UTC-7 permanent from
/// 2026-11-01) on older OS tzdata. Unknown ids fall back to a fixed offset from the longitude.
public struct CityZone: Sendable {
    public let timeZone: TimeZone
    private let permanentFrom: Int64? // epoch ms of 2026-11-01 00:00 at the permanent offset
    private let permanentOffset: Int

    private final class Cache: @unchecked Sendable {
        let lock = NSLock()
        var zones: [String: CityZone] = [:]
    }

    private static let cache = Cache()

    public static func resolve(_ city: CityInfo) -> CityZone { resolve(city.timeZoneId, longitude: city.longitude) }

    public static func resolve(_ ianaId: String, longitude: Double = 0) -> CityZone {
        let key = ianaId.isEmpty ? "|\(longitude)" : ianaId
        cache.lock.lock()
        defer { cache.lock.unlock() }
        if let z = cache.zones[key] { return z }
        let z = make(ianaId, longitude: longitude)
        cache.zones[key] = z
        return z
    }

    private static func make(_ ianaId: String, longitude: Double) -> CityZone {
        if !ianaId.trimmingCharacters(in: .whitespaces).isEmpty, let tz = TimeZone(identifier: ianaId) {
            let permanent: Int? = ianaId == "America/Edmonton" ? -6 * 3600 : ianaId == "America/Vancouver" ? -7 * 3600 : nil
            if let permanent {
                let probe = Date(timeIntervalSince1970: Double(CivilDate(year: 2027, month: 1, day: 15).epochDay) * 86400 + 43200)
                if tz.secondsFromGMT(for: probe) != permanent {
                    let from = Int64(CivilDate(year: 2026, month: 11, day: 1).epochDay) * 86_400_000 - Int64(permanent) * 1000
                    return CityZone(timeZone: tz, permanentFrom: from, permanentOffset: permanent)
                }
            }
            return CityZone(timeZone: tz, permanentFrom: nil, permanentOffset: 0)
        }
        let hours = min(max((longitude / 15).rounded(), -12), 14)
        let tz = TimeZone(secondsFromGMT: Int(hours) * 3600) ?? TimeZone(identifier: "UTC")!
        return CityZone(timeZone: tz, permanentFrom: nil, permanentOffset: 0, fallback: true)
    }

    private let fallback: Bool

    private init(timeZone: TimeZone, permanentFrom: Int64?, permanentOffset: Int, fallback: Bool = false) {
        self.timeZone = timeZone
        self.permanentFrom = permanentFrom
        self.permanentOffset = permanentOffset
        self.fallback = fallback
    }

    /// True when the IANA id was not known and the longitude fallback is in use.
    public var usesFallback: Bool { fallback }

    /// UTC offset in seconds at an instant given as epoch milliseconds.
    public func offsetSeconds(atMs ms: Int64) -> Int {
        if let from = permanentFrom, ms >= from { return permanentOffset }
        return timeZone.secondsFromGMT(for: Date(timeIntervalSince1970: Double(floorDiv(ms, 1000))))
    }

    public func offsetSeconds(at date: Date) -> Int { offsetSeconds(atMs: Int64((date.timeIntervalSince1970 * 1000).rounded(.down))) }

    /// Civil date and second of the local day for an instant.
    func local(atMs ms: Int64) -> (date: CivilDate, secondOfDay: Int, offset: Int) {
        let off = offsetSeconds(atMs: ms)
        let localSeconds = floorDiv(ms, 1000) + Int64(off)
        let day = floorDiv(localSeconds, 86400)
        return (CivilDate(epochDay: Int(day)), Int(localSeconds - day * 86400), off)
    }

    /// Epoch milliseconds of local midnight starting `date` (the first instant of that civil day).
    func startOfDayMs(_ date: CivilDate) -> Int64 {
        let naive = Int64(date.epochDay) * 86_400_000
        // two-pass: guess with the offset at naive-UTC, then correct
        var off = offsetSeconds(atMs: naive)
        var ms = naive - Int64(off) * 1000
        let off2 = offsetSeconds(atMs: ms)
        if off2 != off { off = off2; ms = naive - Int64(off) * 1000 }
        return ms
    }
}
