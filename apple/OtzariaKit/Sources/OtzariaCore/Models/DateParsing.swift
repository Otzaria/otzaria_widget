import Foundation

enum DateParsing {
    /// Dart's DateTime.toIso8601String (no zone for local times, up to 6 fractional digits) and zoned ISO-8601.
    /// Strings without a zone are read in the local time zone, like the C# reader.
    static func parse(_ text: String?) -> Date? {
        guard let text, !text.trimmingCharacters(in: .whitespaces).isEmpty else { return nil }
        let s = text.trimmingCharacters(in: .whitespaces)
        if hasZone(s) {
            for formatter in zonedFormatters {
                if let d = formatter.date(from: s) { return d }
            }
            return nil
        }
        for formatter in localFormatters {
            if let d = formatter.date(from: s) { return d }
        }
        return nil
    }

    private static func hasZone(_ s: String) -> Bool {
        if s.hasSuffix("Z") || s.hasSuffix("z") { return true }
        guard let t = s.firstIndex(where: { $0 == "T" || $0 == " " }) else { return false }
        let tail = s[t...]
        return tail.contains("+") || tail.dropFirst().contains("-")
    }

    private static let zonedFormatters: [ISO8601DateFormatter] = {
        ([[.withInternetDateTime, .withFractionalSeconds], [.withInternetDateTime]] as [ISO8601DateFormatter.Options]).map { options in
            let f = ISO8601DateFormatter()
            f.formatOptions = options
            return f
        }
    }()

    private static let localFormatters: [DateFormatter] = {
        [
            "yyyy-MM-dd'T'HH:mm:ss.SSSSSS", "yyyy-MM-dd'T'HH:mm:ss.SSS", "yyyy-MM-dd'T'HH:mm:ss",
            "yyyy-MM-dd HH:mm:ss.SSSSSS", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd",
        ].map { format in
            let f = DateFormatter()
            f.locale = Locale(identifier: "en_US_POSIX")
            f.calendar = Calendar(identifier: .gregorian)
            f.timeZone = .current
            f.dateFormat = format
            return f
        }
    }()
}
