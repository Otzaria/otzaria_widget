import Foundation

/// Locale-independent Hebrew text helpers (ports of Windows CardFormat).
enum HebrewFormat {
    static let gregorianMonths = ["ינואר", "פברואר", "מרץ", "אפריל", "מאי", "יוני", "יולי", "אוגוסט", "ספטמבר", "אוקטובר", "נובמבר", "דצמבר"]
    static let shortMonths = ["ינו׳", "פבר׳", "מרץ", "אפר׳", "מאי", "יוני", "יולי", "אוג׳", "ספט׳", "אוק׳", "נוב׳", "דצמ׳"]
    /// Index 0 = Sunday.
    static let weekdays = ["יום ראשון", "יום שני", "יום שלישי", "יום רביעי", "יום חמישי", "יום שישי", "שבת"]

    static func gregorianLong(year: Int, month: Int, day: Int) -> String {
        "\(day) ב\(gregorianMonths[month - 1]) \(year)"
    }

    static func gregorianShort(month: Int, day: Int) -> String { "\(day) \(shortMonths[month - 1])" }

    static func percent(_ ratio: Double) -> String {
        let r = ratio.isNaN ? 0 : min(max(ratio, 0), 1)
        return "\(Int((r * 100).rounded(.toNearestOrAwayFromZero)))%"
    }

    /// Truncates on a word boundary when possible.
    static func clip(_ text: String?, _ max: Int) -> String {
        guard let text else { return "" }
        let s = text.trimmingCharacters(in: .whitespacesAndNewlines)
        let chars = Array(s)
        if chars.count <= max { return s }
        var cut = chars[..<max].lastIndex(of: " ").map { $0 } ?? -1
        if cut < max / 2 { cut = max - 1 }
        let head = String(chars[..<cut]).trimmingCharacters(in: CharacterSet(charactersIn: " ,-."))
        return head + "…"
    }

    /// Otzaria's location text starts with the book title; drop that prefix (falls back to the full text).
    static func stripTitle(_ reference: String?, _ title: String?) -> String {
        let r = (reference ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        let t = (title ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        guard !r.isEmpty, !t.isEmpty, r.hasPrefix(t) else { return r }
        let rest = String(r.dropFirst(t.count))
            .drop { " ,:-–—".contains($0) }
        let trimmed = String(rest).trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty ? r : trimmed
    }

    /// Removes bidi isolates and marks (Otzaria wraps its time labels in them).
    static func stripBidi(_ text: String?) -> String {
        guard let text else { return "" }
        let bidi: Set<UInt32> = [0x200E, 0x200F, 0x202A, 0x202B, 0x202C, 0x202D, 0x202E, 0x2066, 0x2067, 0x2068, 0x2069, 0x061C]
        return String(String.UnicodeScalarView(text.unicodeScalars.filter { !bidi.contains($0.value) }))
    }

    /// "היום" / "אתמול" / "לפני N ימים" / "13 נוב׳" for history timestamps.
    static func relativeDay(_ when: Date?, now: Date, calendar: Calendar = gregorianLocal) -> String {
        guard let when else { return "" }
        let a = calendar.startOfDay(for: when)
        let b = calendar.startOfDay(for: now)
        let days = calendar.dateComponents([.day], from: a, to: b).day ?? -1
        if days < 0 { return "" }
        if days == 0 { return "היום" }
        if days == 1 { return "אתמול" }
        if days < 7 { return "לפני \(days) ימים" }
        let c = calendar.dateComponents([.month, .day], from: when)
        return gregorianShort(month: c.month ?? 1, day: c.day ?? 1)
    }

    static var gregorianLocal: Calendar {
        var c = Calendar(identifier: .gregorian)
        c.locale = Locale(identifier: "en_US_POSIX")
        return c
    }
}
