import Foundation

/// Reads every Otzaria source (each isolated, with a Hebrew `SourceStatus`) into one cached `OtzariaSnapshot`. Thread-safe.
/// The host polls `refreshIfChanged()`: file events on the -wal of a database Otzaria holds open are unreliable.
public final class OtzariaDataService: @unchecked Sendable {
    public static let maxRecentBooks = 30
    public static let maxRecentSearches = 20
    /// A cached snapshot older than this is rebuilt on the next `snapshot()`.
    public static let cacheMaxAge: TimeInterval = 300
    /// A snapshot built while Otzaria's files were missing is re-read after this short time.
    public static let incompleteCacheMaxAge: TimeInterval = 5

    private let settingsProvider: @Sendable () -> WidgetSettings
    private let pathResolver: OtzariaPathResolver
    private let userStateReader: UserStateReader
    private let preferencesReader: PreferencesReader
    private let progressService: ProgressService
    private let catalogFactory: @Sendable (String) -> LibraryCatalog
    private let clock: @Sendable () -> Date

    private let lock = NSLock()
    private var cached: (snapshot: OtzariaSnapshot, incomplete: Bool)?
    private var lastToken: String?
    private var catalogKey: String?
    private var catalog: LibraryCatalog?

    public init(
        settingsProvider: @escaping @Sendable () -> WidgetSettings = { WidgetSettings() },
        pathResolver: OtzariaPathResolver = OtzariaPathResolver(),
        userStateReader: UserStateReader = UserStateReader(),
        preferencesReader: PreferencesReader = PreferencesReader(),
        progressService: ProgressService = ProgressService(),
        catalogFactory: @escaping @Sendable (String) -> LibraryCatalog = { LibraryCatalog(databasePath: $0) },
        clock: @escaping @Sendable () -> Date = { Date() }
    ) {
        self.settingsProvider = settingsProvider
        self.pathResolver = pathResolver
        self.userStateReader = userStateReader
        self.preferencesReader = preferencesReader
        self.progressService = progressService
        self.catalogFactory = catalogFactory
        self.clock = clock
    }

    /// The cached snapshot, rebuilt when missing or stale.
    public func snapshot() -> OtzariaSnapshot {
        lock.lock()
        defer { lock.unlock() }
        if let c = cached, !isStale(c.snapshot, incomplete: c.incomplete) { return c.snapshot }
        return buildLocked()
    }

    /// Forces a re-read and returns the fresh snapshot.
    @discardableResult
    public func reload() -> OtzariaSnapshot {
        lock.lock()
        defer { lock.unlock() }
        return buildLocked()
    }

    /// Drops the cache; the next `snapshot()` re-reads.
    public func invalidate() {
        lock.withLock { cached = nil }
    }

    /// A cheap fingerprint of everything read from Otzaria's files (nil when it cannot be determined).
    public func changeToken() -> String? {
        let paths = lock.withLock { cached?.snapshot.paths } ?? pathResolver.resolve(settings: settingsProvider())
        return computeToken(paths)
    }

    /// Polling hook: re-reads and returns the new snapshot only when Otzaria's files changed since the last build.
    public func refreshIfChanged() -> OtzariaSnapshot? {
        lock.lock()
        defer { lock.unlock() }
        guard let c = cached else { return buildLocked() }
        guard let token = computeToken(c.snapshot.paths), token != lastToken else { return nil }
        return buildLocked()
    }

    /// One snapshot converted to the widget contract (see `WidgetSnapshot`).
    public func widgetSnapshot() -> WidgetSnapshot {
        WidgetSnapshot.make(from: snapshot(), settings: settingsProvider(), now: clock())
    }

    // MARK: Build

    private func isStale(_ snap: OtzariaSnapshot, incomplete: Bool) -> Bool {
        let age = clock().timeIntervalSince(snap.readAt)
        // A clock that went backwards also counts as stale.
        return age < 0 || age > (incomplete ? Self.incompleteCacheMaxAge : Self.cacheMaxAge)
    }

    private func buildLocked() -> OtzariaSnapshot {
        let settings = settingsProvider()
        let paths = pathResolver.resolve(settings: settings)
        // Taken BEFORE reading: a change that lands while we read is detected by the next poll.
        lastToken = computeToken(paths)
        var status: [SourceStatus] = []

        // ---- user_state.db ----
        var userData = UserStateData()
        if let path = paths.userStateDb {
            let probe = userStateReader.probe(path: path)
            if probe.health != .ok {
                status.append(probe)
            } else {
                do {
                    userData = try userStateReader.readAll(path: path, maxBooks: Self.maxRecentBooks,
                                                           maxSearches: Self.maxRecentSearches)
                    status.append(SourceStatus(name: SourceNames.userState, health: .ok))
                } catch {
                    status.append(SourceStatus(
                        name: SourceNames.userState, health: .unreadable,
                        message: "לא ניתן היה לקרוא את user_state.db (ייתכן שהקובץ נעול)"))
                }
            }
        } else {
            status.append(SourceStatus(name: SourceNames.userState, health: .notFound, message: "לא נמצא קובץ user_state.db"))
        }
        if settings.hideSearches { userData.recentSearches = [] }

        // ---- preferences ----
        var prefs = OtzariaPreferences()
        if let hive = paths.preferencesHive {
            let result = HiveReader().readDetailed(path: hive, keyFilter: PreferencesReader.isWhitelisted)
            if result.totalBytes == 0 && result.error != nil {
                status.append(SourceStatus(name: SourceNames.preferences, health: .unreadable,
                                           message: "לא ניתן לקרוא את הגדרות אוצריא (app_preferences.hive)"))
            } else {
                prefs = PreferencesReader.map(result.values)
                status.append(SourceStatus(name: SourceNames.preferences, health: .ok))
            }
        } else {
            status.append(SourceStatus(name: SourceNames.preferences, health: .notFound, message: "לא נמצא קובץ app_preferences.hive"))
        }

        // ---- library + progress ----
        var library: LibraryCatalog?
        if let db = paths.libraryDb {
            let candidate = catalogFor(db)
            if candidate.canOpen() {
                library = candidate
                status.append(SourceStatus(name: SourceNames.library, health: .ok))
            } else {
                status.append(SourceStatus(name: SourceNames.library, health: .unreadable,
                                           message: "לא ניתן לפתוח את ספריית אוצריא (seforim.db)"))
            }
        } else {
            status.append(SourceStatus(name: SourceNames.library, health: .notFound,
                                       message: "לא נמצא קובץ seforim.db — לא ניתן לחשב התקדמות בשמור וזכור"))
        }
        var progress = progressService.compute(preferences: prefs, catalog: library)
        for i in progress.indices where progress[i].deepLink == nil {
            progress[i].deepLink = OtzariaLinks.buildBookOpenLink(bookId: progress[i].bookId)
        }
        status.append(SourceStatus(name: SourceNames.progress, health: .ok))

        // ---- Otzaria.app ----
        if paths.otzariaApp == nil {
            status.append(SourceStatus(name: SourceNames.otzariaApp, health: .notFound,
                                       message: "לא נמצאה אפליקציית אוצריא — פתיחת קישורים תעבוד רק אם הפרוטוקול otzaria:// רשום"))
        } else {
            status.append(SourceStatus(name: SourceNames.otzariaApp, health: .ok))
        }

        let incomplete = paths.userStateDb == nil || paths.preferencesHive == nil
        let snap = OtzariaSnapshot(
            readAt: clock(), paths: paths, recentBooks: userData.recentBooks, recentSearches: userData.recentSearches,
            bookmarks: userData.bookmarks, bookmarkGroups: userData.bookmarkGroups, workspaces: userData.workspaces,
            openTabs: userData.openTabs, progress: progress, preferences: prefs, status: status)
        cached = (snap, incomplete)
        return snap
    }

    /// One catalog per library file version (path + size + mtime), so a replaced library is re-read.
    private func catalogFor(_ dbPath: String) -> LibraryCatalog {
        var key = dbPath
        if let a = try? FileManager.default.attributesOfItem(atPath: dbPath) {
            key = "\(dbPath)|\((a[.size] as? Int) ?? 0)|\((a[.modificationDate] as? Date)?.timeIntervalSince1970 ?? 0)"
        }
        if let catalog, key == catalogKey { return catalog }
        let fresh = catalogFactory(dbPath)
        catalog = fresh
        catalogKey = key
        return fresh
    }

    private func computeToken(_ paths: OtzariaPaths) -> String? {
        var parts: [String] = []
        if let db = paths.userStateDb {
            // Fallback when the reader cannot tell: sizes of the db and its wal.
            parts.append(userStateReader.changeToken(path: db) ?? (fileSignature(db) + "|" + fileSignature(db + "-wal")))
        }
        if let hive = paths.preferencesHive { parts.append(fileSignature(hive)) }
        return parts.joined(separator: "#")
    }

    private func fileSignature(_ path: String) -> String {
        guard let a = try? FileManager.default.attributesOfItem(atPath: path) else { return "-" }
        return "\((a[.size] as? Int) ?? 0):\((a[.modificationDate] as? Date)?.timeIntervalSince1970 ?? 0)"
    }
}
