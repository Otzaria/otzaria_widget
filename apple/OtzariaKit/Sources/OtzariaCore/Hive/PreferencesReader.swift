import Foundation

/// Loads the privacy-whitelisted subset of Otzaria's app_preferences.hive. Anything outside the whitelist is
/// never materialized. Never throws: a missing or corrupt file, or odd value types, fall back to the defaults.
public struct PreferencesReader: Sendable {
    public static let shamorZachorPrefix = "sz:"

    private static let whitelistedKeys: Set<String> = [
        "key-selected-city",
        "key-calendar-enabled-zmanim",
        "key-calendar-type",
        "key-calendar-day-transition",
        "key-calendar-twelve-hour-clock",
        "key-dark-mode",
        "key-swatch-color",
        "key-dark-swatch-color",
        "key-library-path",
        "key-library-folder-name",
        "key-databases-path",
    ]

    private static let calendarTypes = ["hebrew", "gregorian", "combined"]
    private static let dayTransitions = ["sunset", "tzais", "rabbeinuTam", "midnight"]

    private let hive: HiveReader

    public init(hive: HiveReader = HiveReader()) { self.hive = hive }

    /// The privacy whitelist, used as the Hive key filter.
    public static func isWhitelisted(_ key: String) -> Bool {
        key.hasPrefix(shamorZachorPrefix) || whitelistedKeys.contains(key)
    }

    public func read(path: String) -> OtzariaPreferences {
        Self.map(hive.read(path: path, keyFilter: Self.isWhitelisted))
    }

    /// Maps an already-read key/value dictionary (only whitelisted keys are looked at).
    public static func map(_ raw: [String: HiveValue]) -> OtzariaPreferences {
        var defaults = OtzariaPreferences()
        var sz: [String: String] = [:]
        for (k, v) in raw where k.hasPrefix(shamorZachorPrefix) {
            if case .string(let s) = v { sz[k] = s }
        }
        defaults.selectedCity = nonEmpty(raw["key-selected-city"]) ?? defaults.selectedCity
        defaults.enabledZmanim = parseStringList(raw["key-calendar-enabled-zmanim"])
        defaults.calendarType = oneOf(raw["key-calendar-type"], calendarTypes) ?? defaults.calendarType
        defaults.dayTransition = oneOf(raw["key-calendar-day-transition"], dayTransitions) ?? defaults.dayTransition
        defaults.twelveHourClock = raw["key-calendar-twelve-hour-clock"]?.boolValue ?? defaults.twelveHourClock
        defaults.darkMode = raw["key-dark-mode"]?.boolValue ?? defaults.darkMode
        defaults.lightSeedColorArgb = seedColor(raw["key-swatch-color"])
        defaults.darkSeedColorArgb = seedColor(raw["key-dark-swatch-color"])
        defaults.libraryPath = nonEmpty(raw["key-library-path"])
        defaults.libraryFolderName = nonEmpty(raw["key-library-folder-name"])
        defaults.databasesPath = nonEmpty(raw["key-databases-path"])
        defaults.shamorZachorRaw = sz
        return defaults
    }

    private static func nonEmpty(_ v: HiveValue?) -> String? {
        guard case .string(let s)? = v, !s.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return nil }
        return s
    }

    // Otzaria reads settings with getValue<T>, which returns the default unless the stored value `is T`: no coercion here.
    private static func oneOf(_ v: HiveValue?, _ allowed: [String]) -> String? {
        guard case .string(let s)? = v else { return nil }
        return allowed.first { $0 == s }
    }

    /// Seed colours are ints (stored as Hive doubles); anything else, such as "#aarrggbb" strings, means "default".
    private static func seedColor(_ v: HiveValue?) -> UInt32? {
        guard case .int(let i)? = v else { return nil }
        return UInt32(truncatingIfNeeded: i)
    }

    /// Mirrors CalendarCubit._parseEnabledZmanim: only a JSON string holding an array counts; strings inside are kept.
    /// A valid "[]" is also returned as empty because the contract cannot tell the two apart.
    private static func parseStringList(_ v: HiveValue?) -> [String] {
        guard case .string(let json)? = v, !json.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
              let obj = try? JSONSerialization.jsonObject(with: Data(json.utf8), options: [.fragmentsAllowed]),
              let array = obj as? [Any]
        else { return [] }
        return array.compactMap { ($0 as? String).flatMap { $0.isEmpty ? nil : $0 } }
    }
}
