import Foundation

/// Otzaria's formatZmanTime: truncated minutes plus a seconds marker ('.' for 0-29, ':' for 30-59), wrapped in an
/// LTR isolate so the marker stays on the correct side in RTL text.
public enum ZmanTimeFormatter {
    public static let ltrIsolateStart: Character = "\u{2066}"
    public static let ltrIsolateEnd: Character = "\u{2069}"
    public static let lrm: Character = "\u{200E}"
    public static let nbsp: Character = "\u{00A0}"

    /// Token for a time given as a second of the local day (0..<86400).
    /// 24-hour: `⁦HH:mm.⁩`; 12-hour: `⁦h:mm.‎ PM⁩`.
    public static func format(secondOfDay s: Int, twelveHour: Bool = false) -> String {
        let hour = s / 3600
        let minute = s / 60 % 60
        let marker: Character = s % 60 < 30 ? "." : ":"
        let min = two(minute)
        if !twelveHour {
            return "\(ltrIsolateStart)\(two(hour)):\(min)\(marker)\(ltrIsolateEnd)"
        }
        let h12 = hour % 12 == 0 ? 12 : hour % 12
        let period = hour < 12 ? "AM" : "PM"
        return "\(ltrIsolateStart)\(h12):\(min)\(marker)\(lrm)\(nbsp)\(period)\(ltrIsolateEnd)"
    }

    /// Same, for an instant expressed in the given zone.
    public static func format(_ instant: Date, zone: CityZone, twelveHour: Bool = false) -> String {
        format(secondOfDay: zone.local(atMs: ms(instant)).secondOfDay, twelveHour: twelveHour)
    }

    /// Plain "HH:mm" (truncated, no marker, no isolates).
    public static func formatPlain(_ instant: Date, zone: CityZone) -> String {
        let s = zone.local(atMs: ms(instant)).secondOfDay
        return "\(two(s / 3600)):\(two(s / 60 % 60))"
    }

    static func ms(_ d: Date) -> Int64 { Int64((d.timeIntervalSince1970 * 1000).rounded(.down)) }

    private static func two(_ v: Int) -> String { v < 10 ? "0\(v)" : "\(v)" }
}
