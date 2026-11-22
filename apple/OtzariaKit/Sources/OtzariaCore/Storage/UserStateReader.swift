import Foundation

/// Everything read from user_state.db through one short-lived connection.
public struct UserStateData: Sendable, Equatable {
    public var recentBooks: [ReadingEntry] = []
    public var recentSearches: [RecentSearch] = []
    public var bookmarks: [ReadingEntry] = []
    public var bookmarkGroups: [BookmarkGroup] = []
    public var workspaces: [WorkspaceInfo] = []
    public var openTabs: [OpenTabInfo] = []

    public init() {}
}

/// Reads Otzaria's user_state.db strictly read-only (storage-and-links.md section 2). I/O failures propagate as
/// errors; schema problems yield empty results (use `probe` to tell them apart). Connections are short-lived.
public struct UserStateReader: Sendable {
    public static let sourceName = SourceNames.userState
    private static let supportedUserVersion: Int64 = 1

    private static let knownBookTypes: Set<String> = [
        "TextBook", "PdfBook", "DocxBook", "EpubBook", "DocumentBook", "ExternalLibraryBook",
    ]
    private static let knownTabTypes: Set<String> = [
        "TextBookTab", "PdfBookTab", "CombinedTab", "ToolTab", "SearchingTabWindow", "SearchingTab",
        "SemanticSearchTab", "CommentatorsTab", "PdfCommentatorsTab",
    ]

    public init() {}

    // MARK: Public API

    public func probe(path: String) -> SourceStatus {
        guard !path.trimmingCharacters(in: .whitespaces).isEmpty, FileManager.default.fileExists(atPath: path) else {
            return SourceStatus(name: Self.sourceName, health: .notFound, message: "לא נמצא קובץ user_state.db")
        }
        do {
            return try withConnection(path) { conn in
                if let problem = try Self.checkSchema(conn) {
                    return SourceStatus(name: Self.sourceName, health: .unknownSchema, message: problem)
                }
                return SourceStatus(name: Self.sourceName, health: .ok, message: nil)
            }
        } catch {
            return SourceStatus(name: Self.sourceName, health: .unreadable,
                                message: "לא ניתן לקרוא את user_state.db: " + Self.describe(error))
        }
    }

    /// `lists.updated_at` and `window_sessions.updated_at` change on every Otzaria commit; file events on the -wal are unreliable.
    /// Returns nil when the database cannot be read right now.
    public func changeToken(path: String) -> String? {
        guard !path.isEmpty, FileManager.default.fileExists(atPath: path) else { return nil }
        return try? withConnection(path) { conn in
            guard try Self.checkSchema(conn) == nil else { return nil }
            var token = ""
            if try Self.hasTable(conn, "lists") {
                let rows = try conn.query(
                    "SELECT box, key, updated_at FROM lists WHERE box IN ('history','bookmarks','workspaces') ORDER BY box, key"
                ) { ($0.text(0) ?? "", $0.text(1) ?? "", $0.int(2) ?? 0) }
                for r in rows { token += "\(r.0)/\(r.1)=\(r.2);" }
            }
            if try Self.hasTable(conn, "window_sessions") {
                let rows = try conn.query(
                    "SELECT slot, updated_at, current_index FROM window_sessions WHERE slot > 0 ORDER BY slot"
                ) { ($0.int(0) ?? 0, $0.int(1) ?? 0, $0.int(2) ?? 0) }
                for r in rows { token += "s\(r.0)=\(r.1):\(r.2);" }
            }
            return token
        }
    }

    /// Reads every list and the main window session in one connection.
    public func readAll(path: String, maxBooks: Int, maxSearches: Int) throws -> UserStateData {
        try withConnection(path) { conn in
            var data = UserStateData()
            guard try Self.checkSchema(conn) == nil else { return data }
            let history = try Self.parseList(Self.readList(conn, "history", "history"))
            data.recentBooks = Self.recentBooks(from: history, max: maxBooks)
            data.recentSearches = Self.recentSearches(from: history, max: maxSearches)
            data.bookmarks = try Self.bookmarks(conn)
            data.bookmarkGroups = try Self.bookmarkGroups(conn)
            data.workspaces = try Self.workspaces(conn)
            data.openTabs = try Self.openTabs(conn)
            return data
        }
    }

    /// History reading entries, newest first, de-duplicated per book identity.
    public func readRecentBooks(path: String, max: Int) throws -> [ReadingEntry] {
        try withConnection(path) { conn in
            guard try Self.checkSchema(conn) == nil else { return [] }
            return Self.recentBooks(from: try Self.parseList(Self.readList(conn, "history", "history")), max: max)
        }
    }

    public func readRecentSearches(path: String, max: Int) throws -> [RecentSearch] {
        try withConnection(path) { conn in
            guard try Self.checkSchema(conn) == nil else { return [] }
            return Self.recentSearches(from: try Self.parseList(Self.readList(conn, "history", "history")), max: max)
        }
    }

    /// Bookmarks, newest first.
    public func readBookmarks(path: String) throws -> [ReadingEntry] {
        try withConnection(path) { conn in
            guard try Self.checkSchema(conn) == nil else { return [] }
            return try Self.bookmarks(conn)
        }
    }

    public func readBookmarkGroups(path: String) throws -> [BookmarkGroup] {
        try withConnection(path) { conn in
            guard try Self.checkSchema(conn) == nil else { return [] }
            return try Self.bookmarkGroups(conn)
        }
    }

    public func readWorkspaces(path: String) throws -> [WorkspaceInfo] {
        try withConnection(path) { conn in
            guard try Self.checkSchema(conn) == nil else { return [] }
            return try Self.workspaces(conn)
        }
    }

    /// Tabs of the main window (slot 1).
    public func readOpenTabs(path: String) throws -> [OpenTabInfo] {
        try withConnection(path) { conn in
            guard try Self.checkSchema(conn) == nil else { return [] }
            return try Self.openTabs(conn)
        }
    }

    // MARK: Lists

    private struct Parsed {
        var entry: ReadingEntry
        var isSearch: Bool
        var searchMode: String?
        var identity: String
    }

    private static func recentBooks(from items: [Parsed], max: Int) -> [ReadingEntry] {
        guard max > 0 else { return [] }
        var result: [ReadingEntry] = []
        var seen = Set<String>()
        for item in items where !item.isSearch {
            // Keep the newest (first) occurrence per book identity.
            guard seen.insert(item.identity).inserted else { continue }
            result.append(item.entry)
            if result.count >= max { break }
        }
        return result
    }

    private static func recentSearches(from items: [Parsed], max: Int) -> [RecentSearch] {
        guard max > 0 else { return [] }
        var result: [RecentSearch] = []
        var seen = Set<String>()
        for item in items where item.isSearch {
            let query = item.entry.book.title
            if query.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { continue }
            guard seen.insert(query + "\u{1}" + (item.searchMode ?? "")).inserted else { continue }
            let display = item.entry.ref.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? nil : item.entry.ref
            result.append(RecentSearch(query: query, mode: item.searchMode, displayText: display,
                                       deepLink: OtzariaLinks.buildSearchLink(query: query, mode: item.searchMode)))
            if result.count >= max { break }
        }
        return result
    }

    private static func bookmarks(_ conn: SQLiteReadConnection) throws -> [ReadingEntry] {
        // Stored oldest-first.
        try parseList(readList(conn, "bookmarks", "key-bookmarks")).filter { !$0.isSearch }.map(\.entry).reversed()
    }

    private static func bookmarkGroups(_ conn: SQLiteReadConnection) throws -> [BookmarkGroup] {
        guard let array = try readList(conn, "bookmarks", "key-bookmark-groups") as? [Any] else { return [] }
        var groups: [BookmarkGroup] = []
        for g in array {
            guard let obj = g as? [String: Any], let id = JSONAccess.str(obj, "id") else { continue }
            var items: [ReadingEntry] = []
            for it in (obj["items"] as? [Any]) ?? [] {
                if let p = tryParseBookmark(it), !p.isSearch { items.append(p.entry) }
            }
            groups.append(BookmarkGroup(id: id, name: JSONAccess.str(obj, "name") ?? "", items: items,
                                        createdAt: DateParsing.parse(JSONAccess.str(obj, "createdAt"))))
        }
        return groups.reversed() // newest first
    }

    private static func workspaces(_ conn: SQLiteReadConnection) throws -> [WorkspaceInfo] {
        var activeId: String?
        if try hasTable(conn, "window_sessions") {
            activeId = try conn.query("SELECT active_workspace_id FROM window_sessions WHERE slot = 1") { $0.text(0) }
                .first.flatMap { $0 }.flatMap { $0.isEmpty ? nil : $0 }
        }
        guard let array = try readList(conn, "workspaces", "key-workspaces") as? [Any] else { return [] }
        var result: [WorkspaceInfo] = []
        for w in array {
            // Workspace.fromJson requires `name` but accepts a missing id (it generates one).
            guard let obj = w as? [String: Any], let name = JSONAccess.str(obj, "name") else { continue }
            let id = JSONAccess.str(obj, "id") ?? "#\(result.count)"
            var titles: [String] = []
            for t in (obj["tabs"] as? [Any]) ?? [] {
                // Broken/unknown tabs are skipped and PdfCommentatorsTab is never stored.
                guard let tab = t as? [String: Any], let type = JSONAccess.str(tab, "type"),
                      knownTabTypes.contains(type), type != "PdfCommentatorsTab" else { continue }
                titles.append(tabTitle(tab))
            }
            result.append(WorkspaceInfo(id: id, name: name, tabCount: titles.count, tabTitles: titles,
                                        isPinned: JSONAccess.bool(obj, "isPinned"),
                                        isActive: activeId != nil && activeId == id))
        }
        return result
    }

    private static func openTabs(_ conn: SQLiteReadConnection) throws -> [OpenTabInfo] {
        guard try hasTable(conn, "window_sessions") else { return [] }
        let rows = try conn.query("SELECT tabs_json, current_index FROM window_sessions WHERE slot = 1") {
            ($0.text(0) ?? "[]", Int($0.int(1) ?? 0))
        }
        guard let row = rows.first, let tabs = JSONAccess.parse(row.0) as? [Any] else { return [] }
        let current = resolveCurrentIndex(tabs, saved: row.1)
        var result: [OpenTabInfo] = []
        for (index, t) in tabs.enumerated() {
            guard let tab = t as? [String: Any], let type = JSONAccess.str(tab, "type") else { continue }
            let reading = resolveTabReading(tab, depth: 0)
            var link = reading?.deepLink
            if reading == nil, type == "SearchingTabWindow" || type == "SearchingTab",
               let q = JSONAccess.str(tab, "searchText"), !q.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                link = OtzariaLinks.buildSearchLink(query: q, mode: nil)
            }
            result.append(OpenTabInfo(title: tabTitle(tab), tabType: type, reading: reading,
                                      isCurrent: index == current, tabIndex: index, deepLink: link))
        }
        return result
    }

    /// TabsRepository._resolvePersistedCurrentTabIndex: clamp, then fall back to the nearest restorable tab (previous first).
    private static func resolveCurrentIndex(_ tabs: [Any], saved: Int) -> Int {
        let count = tabs.count
        if count == 0 { return -1 }
        let restorable = tabs.map { ($0 as? [String: Any]).flatMap { JSONAccess.str($0, "type") } != nil }
        let cur = min(max(saved, 0), count - 1)
        if restorable[cur] { return cur }
        for k in stride(from: cur - 1, through: 0, by: -1) where restorable[k] { return k }
        for k in (cur + 1)..<max(count, cur + 1) where restorable[k] { return k }
        return -1
    }

    private static func readList(_ conn: SQLiteReadConnection, _ box: String, _ key: String) throws -> Any? {
        guard try hasTable(conn, "lists") else { return nil }
        let payload = try conn.query("SELECT payload_json FROM lists WHERE box = ? AND key = ?",
                                     [.text(box), .text(key)]) { $0.text(0) }.first.flatMap { $0 }
        guard let payload, !payload.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return nil }
        return JSONAccess.parse(payload)
    }

    /// Parses every element of a JSON array payload, skipping broken ones.
    private static func parseList(_ json: Any?) -> [Parsed] {
        guard let array = json as? [Any] else { return [] }
        return array.compactMap(tryParseBookmark)
    }

    // MARK: Entry parsing

    private static func tryParseBookmark(_ el: Any) -> Parsed? {
        guard let obj = el as? [String: Any], let bookObj = obj["book"] as? [String: Any] else { return nil }
        let isSearch = JSONAccess.bool(obj, "isSearch")
        guard let (book, identity) = parseBook(bookObj, allowAnyType: isSearch) else { return nil }

        let index = JSONAccess.int(obj, "index") ?? JSONAccess.int(JSONAccess.dict(obj, "readingAnchor"), "line") ?? 0
        let targetKind = JSONAccess.str(obj, "targetKind") == "commentators" ? "commentators" : "book"

        var entry = ReadingEntry(
            book: book, ref: JSONAccess.str(obj, "ref") ?? "", index: index, targetKind: targetKind,
            commentators: JSONAccess.strList(obj, "commentatorsToShow"), label: JSONAccess.str(obj, "label"),
            createdAt: DateParsing.parse(JSONAccess.str(obj, "createdAt")),
            workspaceName: JSONAccess.str(obj, "workspaceName"))
        if !isSearch { entry.deepLink = OtzariaLinks.buildLink(for: entry) }

        var mode: String?
        if isSearch, let m = JSONAccess.str(obj, "searchMode") {
            mode = m.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        }
        return Parsed(entry: entry, isSearch: isSearch, searchMode: mode, identity: identity)
    }

    /// Parses the polymorphic book object (section 2.3.1). Nil for unknown types (unless `allowAnyType`) or titleless books.
    private static func parseBook(_ b: [String: Any], allowAnyType: Bool) -> (BookRef, String)? {
        guard let type = JSONAccess.str(b, "type") ?? (allowAnyType ? "TextBook" : nil) else { return nil }
        if !allowAnyType && !knownBookTypes.contains(type) { return nil }
        guard let title = JSONAccess.str(b, "title") ?? JSONAccess.str(b, "bookName"),
              !title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return nil }

        let isExternal = type == "ExternalLibraryBook"
        // ExternalLibraryBook keeps its catalog id in `otzarId`; it only feeds the identity and is never a deep-linkable id.
        var id = JSONAccess.int(b, "id")
        let identityId = isExternal ? (id ?? JSONAccess.int(b, "otzarId")) : id
        if isExternal { id = nil }
        if let i = id, i <= 0 { id = nil }

        // BookSource.fromJson: only "o" / "u" / "d:<valid slug>" are recognised; anything else uses the legacy flag.
        var source = JSONAccess.str(b, "source")
        let validSource = source == "o" || source == "u"
            || (source?.hasPrefix("d:") == true && OtzariaLinks.isValidSlug(String(source!.dropFirst(2))))
        if !validSource { source = JSONAccess.bool(b, "isUserBook") ? "u" : "o" }
        let src = source ?? "o"

        var externalId = nonEmpty(JSONAccess.str(b, "externalLibraryId"))
        if externalId == nil, isExternal, let oz = b["otzarId"] {
            if let s = oz as? String { externalId = nonEmpty(s) }
            else if let n = oz as? NSNumber, !JSONAccess.isBool(n) { externalId = n.stringValue }
        }

        let path = nonEmpty(JSONAccess.str(b, "path"))
        let filePath = nonEmpty(JSONAccess.str(b, "filePath"))
        let fileType = JSONAccess.str(b, "fileType")
        let versionTitle = type == "TextBook" ? JSONAccess.str(b, "versionTitle") : nil
        let categoryId = JSONAccess.int(b, "categoryId")

        // bookIdentity (lib/bookmarks/models/bookmark.dart); `path` only counts for file book types.
        let isFileBook = ["PdfBook", "DocxBook", "EpubBook", "DocumentBook"].contains(type)
        var baseId: String
        if let identityId { baseId = "id:\(identityId)" + (type == "PdfBook" ? "|pdf" : "") }
        else if let externalId { baseId = "external:\(externalId)" }
        else if isFileBook, let path { baseId = "file:\(path)" }
        else if let filePath { baseId = "filePath:\(filePath)" }
        else if let categoryId { baseId = "category:\(categoryId)|title:\(title)|type:\(fileType ?? "")" }
        else { baseId = "title:\(title)" }
        if src != "o" { baseId += "|src:\(src)" }
        if let versionTitle { baseId += "|version:\(versionTitle)" }

        let ref = BookRef(id: id, title: title, type: type, source: src, author: JSONAccess.str(b, "author"),
                          categoryPath: JSONAccess.str(b, "categoryPath"), fileType: fileType, path: path,
                          externalLibraryId: externalId, versionTitle: versionTitle)
        return (ref, baseId)
    }

    private static func resolveTabReading(_ tab: [String: Any], depth: Int) -> ReadingEntry? {
        if depth > 4 { return nil }
        let type = JSONAccess.str(tab, "type")
        switch type {
        case "TextBookTab":
            guard let b = tab["book"] as? [String: Any], let (book, _) = parseBook(b, allowAnyType: false) else { return nil }
            // bookmark_from_tab.dart: `anchor?.line ?? tab.index` (a valid anchor wins over the stored tab index).
            let idx = anchorLine(tab) ?? JSONAccess.int(tab, "initalIndex") ?? JSONAccess.int(tab, "initialIndex") ?? 0
            return link(ReadingEntry(book: book, index: idx, commentators: JSONAccess.strList(tab, "commentators")))
        case "PdfBookTab":
            guard let b = tab["book"] as? [String: Any], let (book, _) = parseBook(b, allowAnyType: false) else { return nil }
            let page = max(JSONAccess.int(tab, "pageNumber") ?? 1, 1)
            return link(ReadingEntry(book: book, index: page, commentators: JSONAccess.strList(tab, "activeCommentators")))
        case "CombinedTab":
            for side in ["rightTab", "leftTab"] {
                if let inner = tab[side] as? [String: Any], let r = resolveTabReading(inner, depth: depth + 1) { return r }
            }
            return nil
        case "CommentatorsTab", "PdfCommentatorsTab":
            guard let src = tab["sourceTab"] as? [String: Any], var r = resolveTabReading(src, depth: depth + 1) else { return nil }
            let selected = JSONAccess.strList(tab, "selectedCommentators")
            // CommentatorsTab persists its own position as `initialIndex`, which differs from the book tab's.
            var pos = r.index
            if type == "CommentatorsTab", let ci = JSONAccess.int(tab, "initialIndex"), ci >= 0 { pos = ci }
            r.targetKind = "commentators"
            r.index = pos
            if !selected.isEmpty { r.commentators = selected }
            return link(r)
        default:
            return nil
        }
    }

    /// ReadingAnchor.tryFromJson: `line` must be an int >= 0.
    private static func anchorLine(_ obj: [String: Any]) -> Int? {
        guard let line = JSONAccess.int(JSONAccess.dict(obj, "readingAnchor"), "line"), line >= 0 else { return nil }
        return line
    }

    private static func link(_ e: ReadingEntry) -> ReadingEntry {
        var e = e
        e.deepLink = OtzariaLinks.buildLink(for: e)
        return e
    }

    private static func tabTitle(_ tab: [String: Any]) -> String {
        if let t = JSONAccess.str(tab, "title"), !isBlank(t) { return t }
        if let b = tab["book"] as? [String: Any],
           let t = JSONAccess.str(b, "title") ?? JSONAccess.str(b, "bookName"), !isBlank(t) { return t }
        if let t = JSONAccess.str(tab, "bookTitle") ?? JSONAccess.str(tab, "searchText"), !isBlank(t) { return t }
        for name in ["sourceTab", "rightTab", "leftTab"] {
            if let inner = tab[name] as? [String: Any] {
                let t = tabTitle(inner)
                if !t.isEmpty { return t }
            }
        }
        return JSONAccess.str(tab, "type") ?? ""
    }

    private static func isBlank(_ s: String) -> Bool { s.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
    private static func nonEmpty(_ s: String?) -> String? { s.flatMap { $0.isEmpty ? nil : $0 } }

    // MARK: SQLite access

    private static func checkSchema(_ conn: SQLiteReadConnection) throws -> String? {
        let version = try conn.scalarInt("PRAGMA user_version") ?? 0
        if version > supportedUserVersion {
            return "גרסת הסכמה לא מוכרת של user_state.db (\(version)); ייתכן שנדרש עדכון של הווידג'ט"
        }
        if try !hasTable(conn, "lists") && !hasTable(conn, "window_sessions") {
            return "מבנה user_state.db לא מוכר (חסרות הטבלאות lists/window_sessions)"
        }
        return nil
    }

    private static func hasTable(_ conn: SQLiteReadConnection, _ name: String) throws -> Bool {
        try conn.scalarInt("SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = ?", [.text(name)]) != nil
    }

    private func withConnection<T>(_ path: String, _ work: (SQLiteReadConnection) throws -> T) throws -> T {
        do {
            return try Self.withRetry { try Self.runOn(path, work) }
        } catch let e as SQLiteError where (e.primaryCode == 14 || e.primaryCode == 8) && FileManager.default.fileExists(atPath: path) {
            // -shm missing and cannot be created (Otzaria closed cleanly, or the folder is read-only): read a private copy.
            return try Self.runOnCopy(path, work)
        }
    }

    private static func runOn<T>(_ path: String, _ work: (SQLiteReadConnection) throws -> T) throws -> T {
        try work(try SQLiteReadConnection(path: path, busyTimeoutMs: 1500))
    }

    private static let copyDirPrefix = "OtzariaWidget-uscopy-"

    static func runOnCopy<T>(_ path: String, _ work: (SQLiteReadConnection) throws -> T) throws -> T {
        cleanStaleCopies()
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(copyDirPrefix + UUID().uuidString)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let target = dir.appendingPathComponent("user_state.db").path
        try copyConsistent(path, to: target)
        return try withRetry { try work(try SQLiteReadConnection(path: target, busyTimeoutMs: 1500, privateCopy: true)) }
    }

    /// Copies the db and its -wal (not the -shm: SQLite rebuilds it from the -wal), retrying while Otzaria writes.
    private static func copyConsistent(_ path: String, to target: String) throws {
        let fm = FileManager.default
        for attempt in 1... {
            let before = (signature(path), signature(path + "-wal"))
            try? fm.removeItem(atPath: target)
            try fm.copyItem(atPath: path, toPath: target)
            try? fm.removeItem(atPath: target + "-wal")
            if fm.fileExists(atPath: path + "-wal") { try? fm.copyItem(atPath: path + "-wal", toPath: target + "-wal") }
            let after = (signature(path), signature(path + "-wal"))
            if before == after || attempt >= 4 { return }
            usleep(UInt32(50_000 * attempt))
        }
    }

    private struct Signature: Equatable { var size: Int; var mtime: Double }

    private static func signature(_ file: String) -> Signature {
        guard let a = try? FileManager.default.attributesOfItem(atPath: file) else { return Signature(size: -1, mtime: 0) }
        return Signature(size: (a[.size] as? Int) ?? 0, mtime: (a[.modificationDate] as? Date)?.timeIntervalSince1970 ?? 0)
    }

    /// A crash between copy and delete would leave personal data in the temp folder: remove old leftovers.
    private static func cleanStaleCopies() {
        let fm = FileManager.default
        guard let items = try? fm.contentsOfDirectory(at: fm.temporaryDirectory, includingPropertiesForKeys: [.contentModificationDateKey])
        else { return }
        for url in items where url.lastPathComponent.hasPrefix(copyDirPrefix) {
            let m = (try? url.resourceValues(forKeys: [.contentModificationDateKey]))?.contentModificationDate
            if let m, Date().timeIntervalSince(m) > 600 { try? fm.removeItem(at: url) }
        }
    }

    private static func withRetry<T>(_ action: () throws -> T) throws -> T {
        let attempts = 4
        for i in 1... {
            do { return try action() }
            catch let e as SQLiteError where (e.primaryCode == 5 || e.primaryCode == 6) && i < attempts {
                usleep(UInt32(80_000 * i))
            }
        }
        fatalError("unreachable")
    }

    private static func describe(_ error: Error) -> String {
        if let e = error as? SQLiteError { return "SQLite \(e.code)" }
        return String(describing: type(of: error))
    }
}
