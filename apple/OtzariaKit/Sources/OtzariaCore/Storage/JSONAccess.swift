import Foundation

/// Defensive accessors over `JSONSerialization` output, mirroring the C# reader's lenient helpers.
enum JSONAccess {
    static func parse(_ text: String) -> Any? {
        let data = Data(text.utf8)
        if let v = try? JSONSerialization.jsonObject(with: data, options: [.fragmentsAllowed]) { return v }
        // The C# reader allows trailing commas.
        return try? JSONSerialization.jsonObject(with: data, options: [.fragmentsAllowed, .json5Allowed])
    }

    static func isBool(_ v: Any?) -> Bool {
        guard let n = v as? NSNumber else { return false }
        return CFGetTypeID(n) == CFBooleanGetTypeID()
    }

    static func str(_ obj: Any?, _ key: String) -> String? {
        (obj as? [String: Any])?[key] as? String
    }

    /// True only for a JSON `true`.
    static func bool(_ obj: Any?, _ key: String) -> Bool {
        guard let v = (obj as? [String: Any])?[key], isBool(v), let n = v as? NSNumber else { return false }
        return n.boolValue
    }

    /// Integer from a number (integral doubles accepted) or a numeric string.
    static func int(_ obj: Any?, _ key: String) -> Int? {
        guard let v = (obj as? [String: Any])?[key] else { return nil }
        return intValue(v)
    }

    static func intValue(_ v: Any) -> Int? {
        if let s = v as? String {
            guard let i = Int(s.trimmingCharacters(in: .whitespaces)), i >= Int(Int32.min), i <= Int(Int32.max) else { return nil }
            return i
        }
        guard let n = v as? NSNumber, !isBool(n) else { return nil }
        let d = n.doubleValue
        guard d.isFinite, d >= Double(Int32.min), d <= Double(Int32.max), abs(d - d.rounded()) < 1e-9 else { return nil }
        return Int(d.rounded())
    }

    static func strList(_ obj: Any?, _ key: String) -> [String] {
        guard let arr = (obj as? [String: Any])?[key] as? [Any] else { return [] }
        return arr.compactMap { ($0 as? String).flatMap { $0.isEmpty ? nil : $0 } }
    }

    static func dict(_ obj: Any?, _ key: String) -> [String: Any]? {
        (obj as? [String: Any])?[key] as? [String: Any]
    }
}
