import Foundation

public enum LinkOpenMode: String, Codable, Sendable, Equatable {
    /// The widget uses a plain otzaria:// URL and lets the system open it.
    case openUrl
    /// The host app launches Otzaria itself (NSWorkspace) with the URL.
    case providerLaunch
}

public enum ThemeMode: String, Codable, Sendable, Equatable {
    case followSystem, light, dark
}

/// The widget's own settings (never written into Otzaria's files); Apple equivalent of settings.json.
/// Stored as `widget-settings.json` next to `widget-snapshot.json`. Every key is optional when decoding.
public struct WidgetSettings: Codable, Sendable, Equatable {
    public static let fileName = "widget-settings.json"

    public var schemaVersion: Int = 1

    // Data locations (nil = auto-detect)
    public var dataRootOverride: String?
    public var databasesDirOverride: String?
    public var libraryDbOverride: String?
    /// Path of Otzaria.app (or its executable).
    public var otzariaAppOverride: String?

    // Behaviour
    public var linkMode: LinkOpenMode = .openUrl
    public var theme: ThemeMode = .followSystem
    /// "#RRGGBB"; nil = use Otzaria's seed color / default.
    public var accentColor: String?

    // Calendar / zmanim (nil = follow Otzaria's own settings)
    public var cityOverride: String?
    public var zmanimOverride: [String]?
    public var twelveHourClockOverride: Bool?
    public var dayTransitionOverride: String?
    public var showYerushalmiDaf: Bool = false

    // Privacy: hide searches from all widgets.
    public var hideSearches: Bool = false

    public init() {}

    private enum CodingKeys: String, CodingKey {
        case schemaVersion, dataRootOverride, databasesDirOverride, libraryDbOverride, otzariaAppOverride
        case linkMode, theme, accentColor, cityOverride, zmanimOverride, twelveHourClockOverride
        case dayTransitionOverride, showYerushalmiDaf, hideSearches
    }

    /// Each key is recovered on its own (like JsonSettingsStore.RecoverPerProperty): one wrongly-typed value keeps
    /// its default instead of resetting every other setting (e.g. the hideSearches privacy switch).
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        func value<T: Decodable>(_ type: T.Type, _ key: CodingKeys) -> T? { (try? c.decodeIfPresent(type, forKey: key)) ?? nil }
        schemaVersion = value(Int.self, .schemaVersion) ?? 1
        dataRootOverride = value(String.self, .dataRootOverride)
        databasesDirOverride = value(String.self, .databasesDirOverride)
        libraryDbOverride = value(String.self, .libraryDbOverride)
        otzariaAppOverride = value(String.self, .otzariaAppOverride)
        linkMode = value(LinkOpenMode.self, .linkMode) ?? .openUrl
        theme = value(ThemeMode.self, .theme) ?? .followSystem
        accentColor = value(String.self, .accentColor)
        cityOverride = value(String.self, .cityOverride)
        zmanimOverride = value([String].self, .zmanimOverride)
        twelveHourClockOverride = value(Bool.self, .twelveHourClockOverride)
        dayTransitionOverride = value(String.self, .dayTransitionOverride)
        showYerushalmiDaf = value(Bool.self, .showYerushalmiDaf) ?? false
        hideSearches = value(Bool.self, .hideSearches) ?? false
    }
}

/// Reads and writes `widget-settings.json` atomically in a shared directory (the App Group container).
public struct WidgetSettingsStore: Sendable {
    public let directory: URL

    public init(directory: URL) { self.directory = directory }

    public var fileURL: URL { directory.appendingPathComponent(WidgetSettings.fileName) }

    /// Missing or corrupt file yields the defaults (a bad value only resets its own key); never throws.
    public func load() -> WidgetSettings {
        guard let data = try? Data(contentsOf: fileURL),
              let settings = try? JSONDecoder().decode(WidgetSettings.self, from: data)
        else { return WidgetSettings() }
        return settings
    }

    public func save(_ settings: WidgetSettings) throws {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        try encoder.encode(settings).write(to: fileURL, options: .atomic)
    }
}
