import Foundation

/// Read-only access to the library catalog (seforim.db, or user_books.db which has the same schema family).
public protocol LibraryCatalogProviding: Sendable {
    /// Number of learnable items for Shamor Vezachor (TOC leaves, else totalLines, doubled for Talmud).
    func learnableItemCount(bookId: Int) -> Int?
    func bookInfo(bookId: Int) -> (title: String, categoryPath: String?)?
    func findBookId(title: String) -> Int?
}

/// Read-only access to seforim.db (4+ GB; never copied). Short-lived connections, indexed lookups only, results cached
/// in memory. Never throws: failures return nil (and are not cached).
public final class LibraryCatalog: LibraryCatalogProviding, @unchecked Sendable {
    public let databasePath: String
    private let lock = NSLock()
    private var counts: [Int: Int] = [:]
    private var infos: [Int: (title: String, categoryPath: String?)] = [:]
    private var titleIds: [String: Int] = [:]

    public init(databasePath: String) { self.databasePath = databasePath }

    /// Whether the database can be opened at all.
    public func canOpen() -> Bool { (try? open()) != nil }

    public func learnableItemCount(bookId: Int) -> Int? {
        if let c = cached({ counts[bookId] }) { return c }
        guard let conn = try? open(), let n = try? Self.countLearnable(conn, bookId: bookId) else { return nil }
        lock.withLock { counts[bookId] = n }
        return n
    }

    private static func countLearnable(_ conn: SQLiteReadConnection, bookId: Int) throws -> Int? {
        let rows = try conn.query("SELECT id, parentId FROM tocEntry WHERE bookId = ?", [.int(Int64(bookId))]) {
            ($0.int(0) ?? 0, $0.int(1))
        }
        // Otzaria only builds sections from entries whose parentId is NULL; without such a root it falls back to totalLines.
        var children: [Int64: [Int64]] = [:]
        var roots: [Int64] = []
        for (id, parent) in rows {
            if let parent { children[parent, default: []].append(id) } else { roots.append(id) }
        }

        if roots.isEmpty {
            guard let totalLines = try conn.query("SELECT totalLines FROM book WHERE id = ?", [.int(Int64(bookId))],
                                                  { $0.int(0) }).first, let lines = totalLines else { return nil }
            var n = max(Int(lines), 1)
            if try isDafContent(conn, bookId: bookId) { n *= 2 }
            return n
        }

        var leaves = 0
        var stack = roots
        var seen = Set<Int64>()
        while let id = stack.popLast() {
            if !seen.insert(id).inserted { continue } // cycle guard
            if let kids = children[id], !kids.isEmpty { stack.append(contentsOf: kids) } else { leaves += 1 }
        }
        return leaves
    }

    /// Mirrors `_buildRecursiveCategory`: a בבלי/ירושלמי title switches the content type to "דף" (two items per page),
    /// a תנ"ך title switches it back; otherwise the parent's type is inherited.
    private static func isDafContent(_ conn: SQLiteReadConnection, bookId: Int) throws -> Bool {
        let titles = try conn.query(
            "SELECT c.title FROM category_closure cc JOIN category c ON c.id = cc.ancestorId "
                + "WHERE cc.descendantId = (SELECT categoryId FROM book WHERE id = ?) ORDER BY c.level, c.id",
            [.int(Int64(bookId))]) { $0.text(0) ?? "" }
        var daf = false
        for t in titles {
            if t.contains("בבלי") || t.contains("ירושלמי") { daf = true } else if t.contains("תנ\"ך") { daf = false }
        }
        return daf
    }

    public func bookInfo(bookId: Int) -> (title: String, categoryPath: String?)? {
        if let c = cached({ infos[bookId] }) { return c }
        guard let conn = try? open() else { return nil }
        do {
            guard let head = try conn.query("SELECT title, categoryId FROM book WHERE id = ?", [.int(Int64(bookId))],
                                            { ($0.text(0), $0.int(1) ?? 0) }).first, let title = head.0 else { return nil }
            let parts = try conn.query(
                "SELECT c.title FROM category_closure cc JOIN category c ON c.id = cc.ancestorId "
                    + "WHERE cc.descendantId = ? ORDER BY c.level, c.id", [.int(head.1)]) { $0.text(0) }.compactMap { $0 }
            let info = (title: title, categoryPath: parts.isEmpty ? nil : parts.joined(separator: ", "))
            lock.withLock { infos[bookId] = info }
            return info
        } catch {
            return nil
        }
    }

    public func findBookId(title: String) -> Int? {
        if title.isEmpty { return nil }
        if let c = cached({ titleIds[title] }) { return c }
        guard let conn = try? open(),
              let id = try? conn.query("SELECT id FROM book WHERE title = ? ORDER BY id LIMIT 1", [.text(title)],
                                       { $0.int(0) }).first, let id else { return nil }
        lock.withLock { titleIds[title] = Int(id) }
        return Int(id)
    }

    private func cached<T>(_ read: () -> T?) -> T? { lock.withLock(read) }

    private func open() throws -> SQLiteReadConnection {
        try SQLiteReadConnection(path: databasePath, busyTimeoutMs: 2000)
    }
}
