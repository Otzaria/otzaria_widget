import Foundation

// Pure customization values (the Windows WidgetCustomState); the AppIntents map onto these.

struct RecentBooksOptions: Equatable, Sendable {
    var count = 0
    var showLocation = true
    var includePdfs = true
    var showSearches = false
}

struct BookmarksOptions: Equatable, Sendable {
    static let allGroups = "all"
    static let noGroup = "none"
    var count = 0
    var group = BookmarksOptions.allGroups
}

enum ShamorZachorFilter: String, Equatable, Sendable { case inProgress, completed, all }

struct ShamorZachorOptions: Equatable, Sendable {
    var count = 0
    var filter = ShamorZachorFilter.inProgress
    var allColumns = false
}

struct ZmanimOptions: Equatable, Sendable {
    /// nil = follow the global / Otzaria city.
    var city: String?
    /// Empty = follow Otzaria's list.
    var customIds: [String] = []
}

struct CalendarOptions: Equatable, Sendable {
    var showDaf = true
    /// nil = follow the global setting.
    var showYerushalmi: Bool?
    var showOmer = true
    var showCandle = true
}

enum DashboardSection: String, CaseIterable, Equatable, Sendable {
    case date, zman, reading, progress, bookmarks
}

struct DashboardOptions: Equatable, Sendable {
    var sections: [DashboardSection] = DashboardSection.allCases
    var includePdfs = true

    /// Positions 0 (hidden) to 5; ties keep the declared order.
    static func sections(fromPositions positions: [DashboardSection: Int]) -> [DashboardSection] {
        DashboardSection.allCases.enumerated()
            .compactMap { i, s in (positions[s] ?? 0) > 0 ? (s, positions[s]!, i) : nil }
            .sorted { ($0.1, $0.2) < ($1.1, $1.2) }
            .map(\.0)
    }
}

/// Core zmanim preset of the Windows widget (ids unknown to the registry are skipped by the engine).
enum ZmanimPreset {
    static let core = ["alos72Degrees", "sunrise", "sofZmanShmaGRA", "sofZmanTfilaGRA", "chatzos",
                       "minchaGedolaGreater30", "sunset", "tzeitTikochinsky"]
}
