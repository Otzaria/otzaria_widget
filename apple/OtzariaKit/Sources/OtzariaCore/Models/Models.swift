import Foundation

// Port of OtzariaWidget.Core/Contracts/Models.cs (storage-and-links.md section 2.3).

/// Book reference as stored in Otzaria's Bookmark/history JSON.
public struct BookRef: Codable, Sendable, Equatable {
    public var id: Int?
    public var title: String
    /// TextBook | PdfBook | DocxBook | EpubBook | DocumentBook | ExternalLibraryBook
    public var type: String
    /// Wire source key: "o" official, "u" personal, "d:<slug>" attached DB.
    public var source: String
    public var author: String?
    public var categoryPath: String?
    public var fileType: String?
    public var path: String?
    public var externalLibraryId: String?
    public var versionTitle: String?

    public var isPdf: Bool { type == "PdfBook" }

    public init(
        id: Int? = nil, title: String, type: String, source: String = "o",
        author: String? = nil, categoryPath: String? = nil, fileType: String? = nil,
        path: String? = nil, externalLibraryId: String? = nil, versionTitle: String? = nil
    ) {
        self.id = id
        self.title = title
        self.type = type
        self.source = source
        self.author = author
        self.categoryPath = categoryPath
        self.fileType = fileType
        self.path = path
        self.externalLibraryId = externalLibraryId
        self.versionTitle = versionTitle
    }

    private enum CodingKeys: String, CodingKey {
        case id, title, type, source, author, categoryPath, fileType, path, externalLibraryId, versionTitle
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decodeIfPresent(Int.self, forKey: .id)
        title = try c.decode(String.self, forKey: .title)
        type = try c.decodeIfPresent(String.self, forKey: .type) ?? "TextBook"
        source = try c.decodeIfPresent(String.self, forKey: .source) ?? "o"
        author = try c.decodeIfPresent(String.self, forKey: .author)
        categoryPath = try c.decodeIfPresent(String.self, forKey: .categoryPath)
        fileType = try c.decodeIfPresent(String.self, forKey: .fileType)
        path = try c.decodeIfPresent(String.self, forKey: .path)
        externalLibraryId = try c.decodeIfPresent(String.self, forKey: .externalLibraryId)
        versionTitle = try c.decodeIfPresent(String.self, forKey: .versionTitle)
    }
}

/// A reading location (history entry or bookmark).
public struct ReadingEntry: Codable, Sendable, Equatable {
    public var book: BookRef
    /// Display-ready Hebrew location text ("ref"); may be empty.
    public var ref: String
    /// Text: 0-based line index. PDF: 1-based page.
    public var index: Int
    /// "book" | "commentators"
    public var targetKind: String
    public var commentators: [String]
    /// Bookmarks: user label (nil = show `ref`).
    public var label: String?
    public var createdAt: Date?
    public var workspaceName: String?
    /// otzaria:// link that opens this entry, or nil if not linkable.
    public var deepLink: String?

    public init(
        book: BookRef, ref: String = "", index: Int = 0, targetKind: String = "book",
        commentators: [String] = [], label: String? = nil, createdAt: Date? = nil,
        workspaceName: String? = nil, deepLink: String? = nil
    ) {
        self.book = book
        self.ref = ref
        self.index = index
        self.targetKind = targetKind
        self.commentators = commentators
        self.label = label
        self.createdAt = createdAt
        self.workspaceName = workspaceName
        self.deepLink = deepLink
    }

    private enum CodingKeys: String, CodingKey {
        case book, ref, index, targetKind, commentators, label, createdAt, workspaceName, deepLink
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        book = try c.decode(BookRef.self, forKey: .book)
        ref = try c.decodeIfPresent(String.self, forKey: .ref) ?? ""
        index = try c.decodeIfPresent(Int.self, forKey: .index) ?? 0
        targetKind = try c.decodeIfPresent(String.self, forKey: .targetKind) ?? "book"
        commentators = try c.decodeIfPresent([String].self, forKey: .commentators) ?? []
        label = try c.decodeIfPresent(String.self, forKey: .label)
        createdAt = try c.decodeIfPresent(Date.self, forKey: .createdAt)
        workspaceName = try c.decodeIfPresent(String.self, forKey: .workspaceName)
        deepLink = try c.decodeIfPresent(String.self, forKey: .deepLink)
    }
}

public struct RecentSearch: Codable, Sendable, Equatable {
    public var query: String
    /// advanced | exact | fuzzy | smart | nil
    public var mode: String?
    public var displayText: String?
    public var deepLink: String?

    public init(query: String, mode: String? = nil, displayText: String? = nil, deepLink: String? = nil) {
        self.query = query
        self.mode = mode
        self.displayText = displayText
        self.deepLink = deepLink
    }
}

public struct BookmarkGroup: Codable, Sendable, Equatable {
    public var id: String
    public var name: String
    public var items: [ReadingEntry]
    public var createdAt: Date?

    public init(id: String, name: String = "", items: [ReadingEntry] = [], createdAt: Date? = nil) {
        self.id = id
        self.name = name
        self.items = items
        self.createdAt = createdAt
    }

    private enum CodingKeys: String, CodingKey { case id, name, items, createdAt }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decode(String.self, forKey: .id)
        name = try c.decodeIfPresent(String.self, forKey: .name) ?? ""
        items = try c.decodeIfPresent([ReadingEntry].self, forKey: .items) ?? []
        createdAt = try c.decodeIfPresent(Date.self, forKey: .createdAt)
    }
}

public struct WorkspaceInfo: Codable, Sendable, Equatable {
    public var id: String
    public var name: String
    public var tabCount: Int
    public var tabTitles: [String]
    public var isPinned: Bool
    public var isActive: Bool

    public init(id: String, name: String, tabCount: Int = 0, tabTitles: [String] = [],
                isPinned: Bool = false, isActive: Bool = false) {
        self.id = id
        self.name = name
        self.tabCount = tabCount
        self.tabTitles = tabTitles
        self.isPinned = isPinned
        self.isActive = isActive
    }
}

/// An open tab in a saved window session (window_sessions).
public struct OpenTabInfo: Codable, Sendable, Equatable {
    public var title: String
    /// TextBookTab | PdfBookTab | SearchingTabWindow | CombinedTab | CommentatorsTab | ToolTab | ...
    public var tabType: String
    public var reading: ReadingEntry?
    public var isCurrent: Bool
    public var tabIndex: Int
    public var deepLink: String?

    public init(title: String, tabType: String, reading: ReadingEntry? = nil, isCurrent: Bool = false,
                tabIndex: Int = 0, deepLink: String? = nil) {
        self.title = title
        self.tabType = tabType
        self.reading = reading
        self.isCurrent = isCurrent
        self.tabIndex = tabIndex
        self.deepLink = deepLink
    }
}

// MARK: - Shamor Vezachor

public struct ProgressColumn: Codable, Sendable, Equatable {
    public var id: String
    public var label: String
    public var done: Int
    public var ratio: Double
    public var completedAt: Date?

    public init(id: String, label: String, done: Int, ratio: Double, completedAt: Date? = nil) {
        self.id = id
        self.label = label
        self.done = done
        self.ratio = ratio
        self.completedAt = completedAt
    }
}

public struct BookProgress: Codable, Sendable, Equatable {
    public var bookId: Int
    public var title: String
    public var categoryPath: String?
    /// Number of learnable items (TOC leaves or lines), the denominator.
    public var totalItems: Int
    public var columns: [ProgressColumn]
    /// Columns Otzaria would show (rule: i == 0 || ring[i] > 0 || ring[i-1] >= 1).
    public var visibleColumns: [ProgressColumn]
    /// First column ratio clamped to [0, 1].
    public var headline: Double
    public var isCompleted: Bool
    public var isInProgress: Bool
    public var isTracked: Bool
    public var completedAt: Date?
    /// Hebrew status text (e.g. "חצי הדרך מאחוריך!").
    public var statusText: String
    /// "{done} מתוך {total}" for the current cycle; nil when total is unknown or every visible cycle is complete.
    public var currentCycleText: String?
    public var deepLink: String?

    public init(
        bookId: Int, title: String, categoryPath: String? = nil, totalItems: Int = 0,
        columns: [ProgressColumn] = [], visibleColumns: [ProgressColumn] = [], headline: Double = 0,
        isCompleted: Bool = false, isInProgress: Bool = false, isTracked: Bool = false,
        completedAt: Date? = nil, statusText: String = "", currentCycleText: String? = nil,
        deepLink: String? = nil
    ) {
        self.bookId = bookId
        self.title = title
        self.categoryPath = categoryPath
        self.totalItems = totalItems
        self.columns = columns
        self.visibleColumns = visibleColumns
        self.headline = headline
        self.isCompleted = isCompleted
        self.isInProgress = isInProgress
        self.isTracked = isTracked
        self.completedAt = completedAt
        self.statusText = statusText
        self.currentCycleText = currentCycleText
        self.deepLink = deepLink
    }
}

// MARK: - Otzaria preferences (whitelisted subset of app_preferences.hive)

public struct OtzariaPreferences: Codable, Sendable, Equatable {
    public var selectedCity: String
    /// JSON-decoded key-calendar-enabled-zmanim; empty means "use defaults".
    public var enabledZmanim: [String]
    /// hebrew | gregorian | combined
    public var calendarType: String
    /// sunset | tzais | rabbeinuTam | midnight
    public var dayTransition: String
    public var twelveHourClock: Bool
    public var darkMode: Bool
    public var lightSeedColorArgb: UInt32?
    public var darkSeedColorArgb: UInt32?
    public var libraryPath: String?
    public var libraryFolderName: String?
    public var databasesPath: String?
    /// Raw sz:* values (JSON strings), consumed by the progress service.
    public var shamorZachorRaw: [String: String]

    public init(
        selectedCity: String = "ירושלים", enabledZmanim: [String] = [], calendarType: String = "combined",
        dayTransition: String = "sunset", twelveHourClock: Bool = false, darkMode: Bool = false,
        lightSeedColorArgb: UInt32? = nil, darkSeedColorArgb: UInt32? = nil, libraryPath: String? = nil,
        libraryFolderName: String? = nil, databasesPath: String? = nil, shamorZachorRaw: [String: String] = [:]
    ) {
        self.selectedCity = selectedCity
        self.enabledZmanim = enabledZmanim
        self.calendarType = calendarType
        self.dayTransition = dayTransition
        self.twelveHourClock = twelveHourClock
        self.darkMode = darkMode
        self.lightSeedColorArgb = lightSeedColorArgb
        self.darkSeedColorArgb = darkSeedColorArgb
        self.libraryPath = libraryPath
        self.libraryFolderName = libraryFolderName
        self.databasesPath = databasesPath
        self.shamorZachorRaw = shamorZachorRaw
    }
}

// MARK: - Resolved locations

public struct OtzariaPaths: Codable, Sendable, Equatable {
    public var dataRoot: String?
    public var userStateDb: String?
    public var personalNotesDb: String?
    public var userBooksDb: String?
    public var preferencesHive: String?
    public var libraryDb: String?
    /// Otzaria.app bundle (or its executable), when found.
    public var otzariaApp: String?
    public var isPortable: Bool
    /// Hebrew notes on how each path was found (for diagnostics).
    public var diagnostics: [String]

    public init(
        dataRoot: String? = nil, userStateDb: String? = nil, personalNotesDb: String? = nil,
        userBooksDb: String? = nil, preferencesHive: String? = nil, libraryDb: String? = nil,
        otzariaApp: String? = nil, isPortable: Bool = false, diagnostics: [String] = []
    ) {
        self.dataRoot = dataRoot
        self.userStateDb = userStateDb
        self.personalNotesDb = personalNotesDb
        self.userBooksDb = userBooksDb
        self.preferencesHive = preferencesHive
        self.libraryDb = libraryDb
        self.otzariaApp = otzariaApp
        self.isPortable = isPortable
        self.diagnostics = diagnostics
    }
}

public enum DataHealth: String, Codable, Sendable, Equatable {
    case ok, notFound, unreadable, unknownSchema
}

public struct SourceStatus: Codable, Sendable, Equatable {
    public var name: String
    public var health: DataHealth
    public var message: String?

    public init(name: String, health: DataHealth, message: String? = nil) {
        self.name = name
        self.health = health
        self.message = message
    }
}

/// Canonical `SourceStatus.name` values produced by `OtzariaDataService`.
public enum SourceNames {
    public static let userState = "user_state.db"
    public static let preferences = "app_preferences.hive"
    public static let library = "seforim.db"
    public static let progress = "progress"
    public static let otzariaApp = "otzaria.app"
}

/// Everything the widgets need from Otzaria's files, read in one go.
public struct OtzariaSnapshot: Sendable, Equatable {
    public var readAt: Date
    public var paths: OtzariaPaths
    public var recentBooks: [ReadingEntry]
    public var recentSearches: [RecentSearch]
    public var bookmarks: [ReadingEntry]
    public var bookmarkGroups: [BookmarkGroup]
    public var workspaces: [WorkspaceInfo]
    public var openTabs: [OpenTabInfo]
    public var progress: [BookProgress]
    public var preferences: OtzariaPreferences
    public var status: [SourceStatus]

    public init(
        readAt: Date, paths: OtzariaPaths, recentBooks: [ReadingEntry] = [], recentSearches: [RecentSearch] = [],
        bookmarks: [ReadingEntry] = [], bookmarkGroups: [BookmarkGroup] = [], workspaces: [WorkspaceInfo] = [],
        openTabs: [OpenTabInfo] = [], progress: [BookProgress] = [], preferences: OtzariaPreferences = .init(),
        status: [SourceStatus] = []
    ) {
        self.readAt = readAt
        self.paths = paths
        self.recentBooks = recentBooks
        self.recentSearches = recentSearches
        self.bookmarks = bookmarks
        self.bookmarkGroups = bookmarkGroups
        self.workspaces = workspaces
        self.openTabs = openTabs
        self.progress = progress
        self.preferences = preferences
        self.status = status
    }
}
