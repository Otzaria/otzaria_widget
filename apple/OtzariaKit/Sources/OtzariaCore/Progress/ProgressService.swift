import Foundation

/// Shamor Vezachor computation (hive-and-progress.md section 2.4). Pure: works on the raw sz:* JSON strings from
/// `OtzariaPreferences.shamorZachorRaw` plus an optional catalog for titles and denominators. Malformed JSON or entries are
/// skipped. Ordering: in-progress books first (real progress before merely tracked, most recently tracked first), then other
/// books, then completed books (most recently completed first).
public struct ProgressService: Sendable {
    public static let trackedKey = "sz:tracked_books"
    public static let progressKey = "sz:progress_by_id"
    public static let columnsKey = "sz:book_columns"
    public static let completionDatesKey = "sz:completion_dates_by_id"
    public static let columnCompletionDatesKey = "sz:column_completion_dates"

    private static let defaultColumns: [(id: String, label: String)] = [
        ("learn", "לימוד"), ("review1", "חזרה 1"), ("review2", "חזרה 2"), ("review3", "חזרה 3"),
    ]

    public init() {}

    public func compute(preferences: OtzariaPreferences, catalog: LibraryCatalogProviding?) -> [BookProgress] {
        let raw = preferences.shamorZachorRaw
        if raw.isEmpty { return [] }

        let trackedList = Self.parseTracked(Self.rawValue(raw, Self.trackedKey))
        let tracked = Set(trackedList)
        let progress = Self.parseProgress(Self.rawValue(raw, Self.progressKey))
        let columnsOverride = Self.parseColumnsOverride(Self.rawValue(raw, Self.columnsKey))
        let completionDates = Self.parseDateMap(Self.rawValue(raw, Self.completionDatesKey))
        let columnDates = Self.parseNestedDateMap(Self.rawValue(raw, Self.columnCompletionDatesKey))

        var ids = trackedList
        var seen = Set(trackedList)
        for id in progress.keys.sorted() where seen.insert(id).inserted { ids.append(id) }

        struct Item { var book: BookProgress; var hasProgress: Bool; var trackedPos: Int }
        var result: [Item] = []
        for id in ids {
            guard let book = Self.buildBook(id, tracked: tracked, progress: progress, columnsOverride: columnsOverride,
                                            completionDates: completionDates, columnDates: columnDates, catalog: catalog)
            else { continue }
            let pos = trackedList.lastIndex(of: id) ?? -1
            let hasProgress = progress[id]?.values.contains { !$0.isEmpty } ?? false
            result.append(Item(book: book, hasProgress: hasProgress, trackedPos: pos))
        }

        func group(_ b: BookProgress) -> Int { b.isCompleted ? 2 : b.isInProgress ? 0 : 1 }
        // Stable multi-key sort identical to the C# OrderBy/ThenBy chain.
        return result.enumerated().sorted { l, r in
            let a = l.element, b = r.element
            if group(a.book) != group(b.book) { return group(a.book) < group(b.book) }
            let ap = a.book.isInProgress && a.hasProgress, bp = b.book.isInProgress && b.hasProgress
            if ap != bp { return ap }
            if a.trackedPos != b.trackedPos { return a.trackedPos > b.trackedPos }
            let ac = a.book.completedAt ?? .distantPast, bc = b.book.completedAt ?? .distantPast
            if ac != bc { return ac > bc }
            if a.book.bookId != b.book.bookId { return a.book.bookId < b.book.bookId }
            return l.offset < r.offset
        }.map(\.element.book)
    }

    private static func buildBook(
        _ id: Int, tracked: Set<Int>, progress: [Int: [String: Set<String>]],
        columnsOverride: [Int: [(id: String, label: String)]], completionDates: [Int: Date],
        columnDates: [Int: [String: Date]], catalog: LibraryCatalogProviding?
    ) -> BookProgress? {
        var title = "ספר \(id)"
        var categoryPath: String?
        var n = 0
        if let catalog {
            guard let info = catalog.bookInfo(bookId: id) else { return nil } // unknown id (renumbered / different library)
            if !info.title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { title = info.title }
            categoryPath = info.categoryPath
            n = max(0, catalog.learnableItemCount(bookId: id) ?? 0)
        }

        let cols = (columnsOverride[id].flatMap { $0.isEmpty ? nil : $0 }) ?? defaultColumns
        let items = progress[id]
        let colDates = columnDates[id]

        // Valid items only (key is the canonical "0".."N-1"): what the book card rings iterate in Otzaria.
        var validItems: [Set<String>] = []
        if let items, n > 0 {
            for (key, set) in items where isValidIndexKey(key, n) { validItems.append(set) }
        }

        var columns: [ProgressColumn] = []
        var allRawComplete = n > 0 && !cols.isEmpty
        for (colId, label) in cols {
            let done = validItems.reduce(0) { $0 + ($1.contains(colId) ? 1 : 0) }
            // Otzaria's getColumnCompletedCount counts every entry (stale indexes too); the completed chip is based on it.
            let rawDone = items?.values.reduce(0) { $0 + ($1.contains(colId) ? 1 : 0) } ?? 0
            if rawDone < n { allRawComplete = false }
            let ratio = n == 0 ? 0 : min(max(Double(done) / Double(n), 0), 1)
            columns.append(ProgressColumn(id: colId, label: label, done: done, ratio: ratio, completedAt: colDates?[colId]))
        }

        var visible: [ProgressColumn] = []
        var visibleIdx: [Int] = []
        for i in columns.indices where i == 0 || columns[i].ratio > 0 || columns[i - 1].ratio >= 1.0 {
            visible.append(columns[i])
            visibleIdx.append(i)
        }

        let completed = allRawComplete
        let hasAnyItem = items?.values.contains { !$0.isEmpty } ?? false
        let inProgress = !completed && (tracked.contains(id) || hasAnyItem)

        return BookProgress(
            bookId: id, title: title, categoryPath: categoryPath, totalItems: n, columns: columns,
            visibleColumns: visible, headline: min(max(columns[0].ratio, 0), 1), isCompleted: completed,
            isInProgress: inProgress, isTracked: tracked.contains(id), completedAt: completionDates[id],
            statusText: statusText(n: n, columns: columns, visibleIdx: visibleIdx, completed: completed),
            currentCycleText: currentCycleText(n: n, columns: columns, visibleIdx: visibleIdx),
            deepLink: "otzaria://open/book/\(id)")
    }

    // MARK: Status text

    private static func currentIndex(_ columns: [ProgressColumn], _ visibleIdx: [Int]) -> Int {
        // Current cycle = last visible column that is not yet complete (as in Otzaria's book card).
        var current = -1
        for i in visibleIdx where columns[i].ratio < 1.0 { current = i }
        return current
    }

    static func statusText(n: Int, columns: [ProgressColumn], visibleIdx: [Int], completed: Bool) -> String {
        if n <= 0 { return "לימוד פעיל" }
        if completed { return cycleDone(columns.count) }
        let current = currentIndex(columns, visibleIdx)
        if current < 0 { return cycleDone(columns.count) }
        let p = columns[current].ratio
        if p <= 0 { return current > 0 ? cycleDone(current) : "עדיין לא התחלת!" }
        if p < 0.30 { return "התחלה מצוינת!" }
        if p < 0.50 { return "שליש הדרך כבר הושלם!" }
        if p < 0.60 { return "חצי הדרך מאחוריך!" }
        if p < 0.75 { return "רוב הדרך כבר מאחוריך!" }
        return "הסוף כבר באופק!"
    }

    /// The line Otzaria's book card shows under the rings: "{round(ring*N)} מתוך {N}" for the current cycle.
    static func currentCycleText(n: Int, columns: [ProgressColumn], visibleIdx: [Int]) -> String? {
        if n <= 0 { return nil }
        let current = currentIndex(columns, visibleIdx)
        if current < 0 { return nil }
        // Dart's double.round() rounds half away from zero.
        let shown = Int((columns[current].ratio * Double(n)).rounded(.toNearestOrAwayFromZero))
        return "\(shown) מתוך \(n)"
    }

    /// True when `key` is exactly the decimal string of an index in [0, n).
    static func isValidIndexKey(_ key: String, _ n: Int) -> Bool {
        let bytes = Array(key.utf8)
        if bytes.isEmpty || bytes.count > 10 { return false }
        if bytes.count > 1 && bytes[0] == UInt8(ascii: "0") { return false }
        var v = 0
        for c in bytes {
            if c < 48 || c > 57 { return false }
            v = v * 10 + Int(c - 48)
        }
        return v < n
    }

    private static func cycleDone(_ cycle: Int) -> String {
        switch cycle {
        case 1: return "סיימת מחזור ראשון בהצלחה!"
        case 2: return "סיימת מחזור שני בהצלחה!"
        case 3: return "סיימת מחזור שלישי בהצלחה!"
        case 4: return "סיימת מחזור רביעי בהצלחה!"
        default: return "הושלם בהצלחה!"
        }
    }

    // MARK: Parsing

    private static func rawValue(_ raw: [String: String], _ key: String) -> String? {
        guard let v = raw[key], !v.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return nil }
        return v
    }

    private static func parseId(_ s: String) -> Int? {
        guard let i = Int(s.trimmingCharacters(in: .whitespaces)), i >= Int(Int32.min), i <= Int(Int32.max) else { return nil }
        return i
    }

    private static func parseTracked(_ json: String?) -> [Int] {
        guard let json, let array = JSONAccess.parse(json) as? [Any] else { return [] }
        var seen = Set<Int>()
        var list: [Int] = []
        for e in array {
            if let id = JSONAccess.intValue(e), seen.insert(id).inserted { list.append(id) }
        }
        return list
    }

    /// bookId -> itemKey -> set of true column ids. Item keys stay strings exactly as Otzaria stores them.
    private static func parseProgress(_ json: String?) -> [Int: [String: Set<String>]] {
        var result: [Int: [String: Set<String>]] = [:]
        guard let json, let root = JSONAccess.parse(json) as? [String: Any] else { return result }
        for (bookKey, bookValue) in root {
            guard let id = parseId(bookKey), let itemsObj = bookValue as? [String: Any] else { continue }
            var items: [String: Set<String>] = [:]
            for (itemKey, itemValue) in itemsObj {
                guard let cols = itemValue as? [String: Any] else { continue }
                var set = Set<String>()
                for (col, flag) in cols where JSONAccess.isBool(flag) && (flag as? NSNumber)?.boolValue == true { set.insert(col) }
                items[itemKey] = set
            }
            result[id] = items
        }
        return result
    }

    private static func parseColumnsOverride(_ json: String?) -> [Int: [(id: String, label: String)]] {
        var result: [Int: [(id: String, label: String)]] = [:]
        guard let json, let root = JSONAccess.parse(json) as? [String: Any] else { return result }
        for (bookKey, value) in root {
            guard let id = parseId(bookKey), let array = value as? [Any] else { continue }
            var cols: [(id: String, label: String)] = []
            var ids = Set<String>()
            for c in array {
                guard let obj = c as? [String: Any], let colId = obj["id"] as? String, !colId.isEmpty,
                      ids.insert(colId).inserted else { continue }
                var label = obj["label"] as? String
                if label == nil || label!.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { label = "מחזור \(cols.count + 1)" }
                cols.append((colId, label!))
            }
            if !cols.isEmpty { result[id] = cols }
        }
        return result
    }

    private static func parseDateMap(_ json: String?) -> [Int: Date] {
        var result: [Int: Date] = [:]
        guard let json, let root = JSONAccess.parse(json) as? [String: Any] else { return result }
        for (k, v) in root {
            if let id = parseId(k), let d = DateParsing.parse(v as? String) { result[id] = d }
        }
        return result
    }

    private static func parseNestedDateMap(_ json: String?) -> [Int: [String: Date]] {
        var result: [Int: [String: Date]] = [:]
        guard let json, let root = JSONAccess.parse(json) as? [String: Any] else { return result }
        for (k, v) in root {
            guard let id = parseId(k), let obj = v as? [String: Any] else { continue }
            var map: [String: Date] = [:]
            for (colId, dv) in obj { if let d = DateParsing.parse(dv as? String) { map[colId] = d } }
            result[id] = map
        }
        return result
    }
}
