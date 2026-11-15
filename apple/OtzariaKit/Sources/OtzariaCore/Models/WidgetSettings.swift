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

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        schemaVersion = try c.decodeIfPresent(Int.self, forKey: .schemaVersion) ?? 1
        dataRootOverride = try c.decodeIfPresent(String.self, forKey: .dataRootOverride)
        databasesDirOverride = try c.decodeIfPresent(String.self, forKey: .databasesDirOverride)
        libraryDbOverride = try c.decodeIfPresent(String.self, forKey: .libraryDbOverride)
        otzariaAppOverride = try c.decodeIfPresent(String.self, forKey: .otzariaAppOverride)
        linkMode = (try? c.decodeIfPresent(LinkOpenMode.self, forKey: .linkMode)) ?? .openUrl
        theme = (try? c.decodeIfPresent(ThemeMode.self, forKey: .theme)) ?? .followSystem
        accentColor = try c.decodeIfPresent(String.self, forKey: .accentColor)
        cityOverride = try c.decodeIfPresent(String.self, forKey: .cityOverride)
        zmanimOverride = try c.decodeIfPresent([String].self, forKey: .zmanimOverride)
        twelveHourClockOverride = try c.decodeIfPresent(Bool.self, forKey: .twelveHourClockOverride)
        dayTransitionOverride = try c.decodeIfPresent(String.self, forKey: .dayTransitionOverride)
        showYerushalmiDaf = try c.decodeIfPresent(Bool.self, forKey: .showYerushalmiDaf) ?? false
        hideSearches = try c.decodeIfPresent(Bool.self, forKey: .hideSearches) ?? false
    }
}

/// Reads and writes `widget-settings.json` atomically in a shared directory (the App Group container).
public struct WidgetSettingsStore: Sendable {
    public let directory: URL

    public init(directory: URL) { self.directory = directory }

    public var fileURL: URL { directory.appendingPathComponent(WidgetSettings.fileName) }

    /// Missing or corrupt file yields the defaults; never throws.
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
