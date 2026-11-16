import Foundation

/// Effective preferences the widgets need: Otzaria's own values with the widget's overrides already applied.
public struct WidgetPreferences: Codable, Sendable, Equatable {
    public var selectedCity: String
    /// Enabled zmanim ids; empty means "use the defaults".
    public var enabledZmanim: [String]
    public var twelveHourClock: Bool
    /// sunset | tzais | rabbeinuTam | midnight (when the Hebrew day switches).
    public var dayTransition: String
    /// hebrew | gregorian | combined
    public var calendarType: String
    public var showYerushalmiDaf: Bool
    public var darkMode: Bool
    public var lightSeedColorArgb: UInt32?
    public var darkSeedColorArgb: UInt32?
    public var linkMode: LinkOpenMode
    public var theme: ThemeMode
    /// "#RRGGBB" accent override, nil = derive from Otzaria's seed colour.
    public var accentColor: String?

    public init(
        selectedCity: String = "ירושלים", enabledZmanim: [String] = [], twelveHourClock: Bool = false,
        dayTransition: String = "sunset", calendarType: String = "combined", showYerushalmiDaf: Bool = false,
        darkMode: Bool = false, lightSeedColorArgb: UInt32? = nil, darkSeedColorArgb: UInt32? = nil,
        linkMode: LinkOpenMode = .openUrl, theme: ThemeMode = .followSystem, accentColor: String? = nil
    ) {
        self.selectedCity = selectedCity
        self.enabledZmanim = enabledZmanim
        self.twelveHourClock = twelveHourClock
        self.dayTransition = dayTransition
        self.calendarType = calendarType
        self.showYerushalmiDaf = showYerushalmiDaf
        self.darkMode = darkMode
        self.lightSeedColorArgb = lightSeedColorArgb
        self.darkSeedColorArgb = darkSeedColorArgb
        self.linkMode = linkMode
        self.theme = theme
        self.accentColor = accentColor
    }

    private enum CodingKeys: String, CodingKey {
        case selectedCity, enabledZmanim, twelveHourClock, dayTransition, calendarType, showYerushalmiDaf
        case darkMode, lightSeedColorArgb, darkSeedColorArgb, linkMode, theme, accentColor
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let d = WidgetPreferences()
        selectedCity = try c.decodeIfPresent(String.self, forKey: .selectedCity) ?? d.selectedCity
        enabledZmanim = try c.decodeIfPresent([String].self, forKey: .enabledZmanim) ?? []
        twelveHourClock = try c.decodeIfPresent(Bool.self, forKey: .twelveHourClock) ?? d.twelveHourClock
        dayTransition = try c.decodeIfPresent(String.self, forKey: .dayTransition) ?? d.dayTransition
        calendarType = try c.decodeIfPresent(String.self, forKey: .calendarType) ?? d.calendarType
        showYerushalmiDaf = try c.decodeIfPresent(Bool.self, forKey: .showYerushalmiDaf) ?? false
        darkMode = try c.decodeIfPresent(Bool.self, forKey: .darkMode) ?? false
        lightSeedColorArgb = try c.decodeIfPresent(UInt32.self, forKey: .lightSeedColorArgb)
        darkSeedColorArgb = try c.decodeIfPresent(UInt32.self, forKey: .darkSeedColorArgb)
        linkMode = (try? c.decodeIfPresent(LinkOpenMode.self, forKey: .linkMode)) ?? .openUrl
        theme = (try? c.decodeIfPresent(ThemeMode.self, forKey: .theme)) ?? .followSystem
        accentColor = try c.decodeIfPresent(String.self, forKey: .accentColor)
    }
}

/// App Group JSON contract (`widget-snapshot.json`) between the Otzaria data reader and the widgets: camelCase keys,
/// ISO-8601 UTC dates, enums as names; only `schemaVersion` and `generatedAt` are required, missing lists read as empty.
public struct WidgetSnapshot: Codable, Sendable, Equatable {
    public static let currentSchemaVersion = 1
    public static let fileName = "widget-snapshot.json"

    public var schemaVersion: Int
    public var generatedAt: Date
    /// Overall state of the data (worst of user_state.db, preferences, library), for the empty and error states.
    public var health: DataHealth
    /// Hebrew explanation shown by the widgets when `health` is not `ok`.
    public var healthMessage: String?
    public var sources: [SourceStatus]
    public var recentBooks: [ReadingEntry]
    /// Active tab of Otzaria's main window when it holds a book ("continue reading").
    public var currentTab: OpenTabInfo?
    public var bookmarks: [ReadingEntry]
    public var bookmarkGroups: [BookmarkGroup]
    /// Already empty when the user chose to hide searches.
    public var recentSearches: [RecentSearch]
    public var shamorZachor: [BookProgress]
    public var preferences: WidgetPreferences

    public init(
        schemaVersion: Int = WidgetSnapshot.currentSchemaVersion, generatedAt: Date, health: DataHealth = .ok,
        healthMessage: String? = nil, sources: [SourceStatus] = [], recentBooks: [ReadingEntry] = [],
        currentTab: OpenTabInfo? = nil, bookmarks: [ReadingEntry] = [], bookmarkGroups: [BookmarkGroup] = [],
        recentSearches: [RecentSearch] = [], shamorZachor: [BookProgress] = [], preferences: WidgetPreferences = .init()
    ) {
        self.schemaVersion = schemaVersion
        self.generatedAt = generatedAt
        self.health = health
        self.healthMessage = healthMessage
        self.sources = sources
        self.recentBooks = recentBooks
        self.currentTab = currentTab
        self.bookmarks = bookmarks
        self.bookmarkGroups = bookmarkGroups
        self.recentSearches = recentSearches
        self.shamorZachor = shamorZachor
        self.preferences = preferences
    }

    private enum CodingKeys: String, CodingKey {
        case schemaVersion, generatedAt, health, healthMessage, sources, recentBooks, currentTab, bookmarks
        case bookmarkGroups, recentSearches, shamorZachor, preferences
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        schemaVersion = try c.decode(Int.self, forKey: .schemaVersion)
        generatedAt = try c.decode(Date.self, forKey: .generatedAt)
        health = (try? c.decodeIfPresent(DataHealth.self, forKey: .health)) ?? .ok
        healthMessage = try c.decodeIfPresent(String.self, forKey: .healthMessage)
        sources = try c.decodeIfPresent([SourceStatus].self, forKey: .sources) ?? []
        recentBooks = try c.decodeIfPresent([ReadingEntry].self, forKey: .recentBooks) ?? []
        currentTab = try c.decodeIfPresent(OpenTabInfo.self, forKey: .currentTab)
        bookmarks = try c.decodeIfPresent([ReadingEntry].self, forKey: .bookmarks) ?? []
        bookmarkGroups = try c.decodeIfPresent([BookmarkGroup].self, forKey: .bookmarkGroups) ?? []
        recentSearches = try c.decodeIfPresent([RecentSearch].self, forKey: .recentSearches) ?? []
        shamorZachor = try c.decodeIfPresent([BookProgress].self, forKey: .shamorZachor) ?? []
        preferences = try c.decodeIfPresent(WidgetPreferences.self, forKey: .preferences) ?? WidgetPreferences()
    }

    // MARK: Building from Otzaria's data

    public static let maxBookmarks = 100
    public static let maxBookmarkGroups = 30

    /// Converts a freshly read `OtzariaSnapshot`, applying the widget settings (overrides, hidden searches).
    public static func make(from data: OtzariaSnapshot, settings: WidgetSettings, now: Date = Date()) -> WidgetSnapshot {
        let (health, message) = overallHealth(data.status)
        let prefs = data.preferences
        let preferences = WidgetPreferences(
            selectedCity: settings.cityOverride.flatMap(nonBlank) ?? prefs.selectedCity,
            enabledZmanim: settings.zmanimOverride ?? prefs.enabledZmanim,
            twelveHourClock: settings.twelveHourClockOverride ?? prefs.twelveHourClock,
            dayTransition: settings.dayTransitionOverride.flatMap(nonBlank) ?? prefs.dayTransition,
            calendarType: prefs.calendarType,
            showYerushalmiDaf: settings.showYerushalmiDaf,
            darkMode: prefs.darkMode,
            lightSeedColorArgb: prefs.lightSeedColorArgb,
            darkSeedColorArgb: prefs.darkSeedColorArgb,
            linkMode: settings.linkMode,
            theme: settings.theme,
            accentColor: settings.accentColor)
        return WidgetSnapshot(
            generatedAt: now, health: health, healthMessage: message, sources: data.status,
            recentBooks: data.recentBooks,
            currentTab: data.openTabs.first { $0.isCurrent && $0.reading != nil },
            bookmarks: Array(data.bookmarks.prefix(maxBookmarks)),
            bookmarkGroups: Array(data.bookmarkGroups.prefix(maxBookmarkGroups)),
            recentSearches: settings.hideSearches ? [] : data.recentSearches,
            shamorZachor: data.progress, preferences: preferences)
    }

    /// user_state.db decides (it carries the reading data); otherwise the first problem among the other sources is the message.
    static func overallHealth(_ status: [SourceStatus]) -> (DataHealth, String?) {
        if let user = status.first(where: { $0.name == SourceNames.userState }), user.health != .ok {
            return (user.health, user.message)
        }
        let relevant = [SourceNames.preferences, SourceNames.library]
        if let problem = status.first(where: { relevant.contains($0.name) && $0.health != .ok }) {
            return (problem.health, problem.message)
        }
        return (.ok, nil)
    }

    private static func nonBlank(_ s: String) -> String? {
        s.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? nil : s
    }
}

/// Reads and writes `widget-snapshot.json` atomically in a directory (the App Group container, passed in by the app).
public struct WidgetSnapshotStore: Sendable {
    /// iOS App Group. The App Group must be enabled for the app and every extension.
    public static let iOSAppGroupID = "group.org.otzaria.widget"

    /// macOS group containers are team-prefixed: `<TEAMID>.org.otzaria.widget` (for example "ABCDE12345.org.otzaria.widget").
    public static func macOSAppGroupID(teamID: String) -> String { "\(teamID).org.otzaria.widget" }

    public let directory: URL

    public init(directory: URL) { self.directory = directory }

    public var fileURL: URL { directory.appendingPathComponent(WidgetSnapshot.fileName) }

    /// The shared container of `groupID`, or nil when the entitlement is missing.
    public static func appGroupDirectory(groupID: String) -> URL? {
        FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: groupID)
    }

    /// Nil when the file is missing or unreadable (the widgets then show their empty state).
    public func read() -> WidgetSnapshot? {
        guard let data = try? Data(contentsOf: fileURL) else { return nil }
        return try? Self.makeDecoder().decode(WidgetSnapshot.self, from: data)
    }

    /// Writes atomically (temp file + rename), so a reader never sees a partial file.
    public func write(_ snapshot: WidgetSnapshot) throws {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        try Self.makeEncoder().encode(snapshot).write(to: fileURL, options: .atomic)
    }

    public static func makeEncoder() -> JSONEncoder {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        encoder.dateEncodingStrategy = .custom { date, enc in
            var container = enc.singleValueContainer()
            try container.encode(isoFormatter.string(from: date))
        }
        return encoder
    }

    public static func makeDecoder() -> JSONDecoder {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .custom { dec in
            let text = try dec.singleValueContainer().decode(String.self)
            guard let date = DateParsing.parse(text) else {
                throw DecodingError.dataCorrupted(.init(codingPath: dec.codingPath, debugDescription: "bad date \(text)"))
            }
            return date
        }
        return decoder
    }

    private static let isoFormatter: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime]
        f.timeZone = TimeZone(identifier: "UTC")
        return f
    }()
}
