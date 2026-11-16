import Foundation
import OtzariaCore

struct ReadingRow: Equatable {
    var title: String
    var sub: String
    var meta: String
    /// Location only, short (one-line bookmark rows).
    var short: String = ""
    var url: URL
}

struct SearchRow: Equatable {
    var text: String
    var sub: String
    var url: URL
}

enum ReadingRows {
    static func key(_ e: ReadingEntry) -> String { "\(e.book.source)|\(e.book.id.map(String.init) ?? e.book.title)|\(e.book.type)" }

    static func row(_ e: ReadingEntry, now: Date, showLocation: Bool, meta: String? = nil) -> ReadingRow {
        let sub = showLocation ? HebrewFormat.stripTitle(e.ref, e.book.title) : ""
        let label = e.label.flatMap(nonBlank)
        return ReadingRow(
            title: HebrewFormat.clip(label ?? e.book.title, 80), sub: HebrewFormat.clip(sub, 70),
            meta: meta ?? defaultMeta(e, now: now), url: WidgetLinks.url(e.deepLink))
    }

    static func defaultMeta(_ e: ReadingEntry, now: Date) -> String {
        var parts: [String] = []
        if e.book.isPdf { parts.append("PDF") }
        let day = HebrewFormat.relativeDay(e.createdAt, now: now)
        if !day.isEmpty { parts.append(day) }
        return parts.joined(separator: " · ")
    }

    static func searchRow(_ s: RecentSearch) -> SearchRow {
        let text = s.displayText.flatMap(nonBlank) ?? s.query
        let sub: String
        switch s.mode {
        case "exact": sub = "חיפוש מדויק"
        case "fuzzy": sub = "חיפוש מקורב"
        case "smart": sub = "חיפוש חכם"
        default: sub = ""
        }
        let link = s.deepLink ?? OtzariaLinks.buildSearchLink(query: s.query, mode: nil)
        return SearchRow(text: HebrewFormat.clip(text, 60), sub: sub, url: WidgetLinks.url(link))
    }

    /// The tab open right now, if it can be shown.
    static func currentTab(_ snapshot: WidgetSnapshot, includePdfs: Bool) -> (reading: ReadingEntry, link: String?)? {
        guard let tab = snapshot.currentTab, let reading = tab.reading, includePdfs || !reading.book.isPdf else { return nil }
        return (reading, tab.deepLink)
    }
}

// MARK: Recent books

struct RecentBooksModel: Equatable {
    var state: ContentState
    var heroLabel = ""
    var hero: ReadingRow?
    var rows: [ReadingRow] = []
    var searches: [SearchRow] = []
    /// Number of list slots asked for (hero included), to choose the row density.
    var slots = 0
    var url = WidgetLinks.url(WidgetLinks.history)

    static func make(_ ctx: WidgetContext, _ o: RecentBooksOptions) -> RecentBooksModel {
        if let reason = DataText.missingReason(ctx.snapshot, sources: [SourceNames.userState]) {
            return RecentBooksModel(state: .missing(reason))
        }
        let snap = ctx.snapshot!
        let recents = snap.recentBooks.filter { o.includePdfs || !$0.book.isPdf }
        let tab = ReadingRows.currentTab(snap, includePdfs: o.includePdfs)
        guard let heroEntry = tab?.reading ?? recents.first else {
            return RecentBooksModel(state: .empty("לא נמצאו ספרים אחרונים"))
        }
        var hero = ReadingRows.row(heroEntry, now: ctx.now, showLocation: o.showLocation)
        if let tab {
            hero.meta = heroEntry.book.isPdf ? "PDF" : ""
            if let link = tab.link, !link.isEmpty { hero.url = WidgetLinks.url(link) }
        }
        var model = RecentBooksModel(state: .ok, heroLabel: tab != nil ? "המשך קריאה" : "קריאה אחרונה", hero: hero)

        let slots = ctx.size.rows(count: o.count, defaultRows: ctx.size == .small ? 1 : ctx.size == .medium ? 3 : 7)
        model.slots = ctx.size == .small ? 1 : slots
        if ctx.size != .small, slots > 1 {
            let heroKey = ReadingRows.key(heroEntry)
            model.rows = recents.filter { ReadingRows.key($0) != heroKey }.prefix(24).prefix(slots - 1)
                .map { ReadingRows.row($0, now: ctx.now, showLocation: o.showLocation) }
        }
        if o.showSearches, !ctx.hideSearches, ctx.size != .small {
            model.searches = snap.recentSearches.prefix(ctx.size == .large ? 2 : 1).map(ReadingRows.searchRow)
        }
        return model
    }
}

// MARK: Bookmarks

struct BookmarksModel: Equatable {
    var state: ContentState
    var groupName = ""
    var rows: [ReadingRow] = []
    var url = WidgetLinks.url(WidgetLinks.bookmarks)

    static func make(_ ctx: WidgetContext, _ o: BookmarksOptions) -> BookmarksModel {
        if let reason = DataText.missingReason(ctx.snapshot, sources: [SourceNames.userState]) {
            return BookmarksModel(state: .missing(reason))
        }
        let snap = ctx.snapshot!
        func itemKey(_ e: ReadingEntry) -> String { "\(ReadingRows.key(e))|\(e.index)" }
        var source = snap.bookmarks
        var groupName = ""
        if o.group == BookmarksOptions.noGroup {
            let grouped = Set(snap.bookmarkGroups.flatMap(\.items).map(itemKey))
            source = source.filter { !grouped.contains(itemKey($0)) }
            groupName = "ללא קבוצה"
        } else if o.group != BookmarksOptions.allGroups, let g = snap.bookmarkGroups.first(where: { $0.id == o.group }) {
            source = g.items
            groupName = g.name
        }
        let list = Array(source.prefix(40))
        if list.isEmpty {
            return BookmarksModel(state: .empty(groupName.isEmpty ? "לא נמצאו סימניות" : "אין סימניות בקבוצה זו"), groupName: groupName)
        }
        let n = ctx.size.rows(count: o.count, defaultRows: ctx.size == .large ? 5 : 3)
        let rows = list.prefix(n).map { b -> ReadingRow in
            let label = b.label.flatMap(nonBlank)
            let loc = HebrewFormat.stripTitle(b.ref, b.book.title)
            let sub = label != nil ? [b.book.title, loc].filter { nonBlank($0) != nil }.joined(separator: " · ") : loc
            return ReadingRow(title: HebrewFormat.clip(label ?? b.book.title, 80), sub: HebrewFormat.clip(sub, 80),
                              meta: b.book.isPdf ? "PDF" : "", short: HebrewFormat.clip(loc, 22), url: WidgetLinks.url(b.deepLink))
        }
        return BookmarksModel(state: .ok, groupName: groupName, rows: Array(rows))
    }
}

// MARK: Shamor Vezachor

struct ProgressBarRow: Equatable {
    var label: String
    var ratio: Double
    var pct: String
}

struct ProgressRow: Equatable {
    var title: String
    var meta: String
    var sub: String
    var cycle: String
    var ratio: Double
    var complete: Bool
    var columns: [ProgressBarRow]
    var url: URL
}

struct ShamorZachorModel: Equatable {
    var state: ContentState
    var subtitle = ""
    var headerEnd = ""
    var note = ""
    var rows: [ProgressRow] = []
    var url = WidgetLinks.url(WidgetLinks.shamorZachor)

    static func matches(_ b: BookProgress, _ filter: ShamorZachorFilter) -> Bool {
        switch filter {
        case .completed: return b.isCompleted
        case .all: return true
        case .inProgress: return (b.isTracked || b.isInProgress) && !b.isCompleted
        }
    }

    static func filterLabel(_ f: ShamorZachorFilter) -> String {
        switch f {
        case .completed: return "הושלמו"
        case .all: return "כל הספרים"
        case .inProgress: return "בתהליך"
        }
    }

    static func make(_ ctx: WidgetContext, _ o: ShamorZachorOptions) -> ShamorZachorModel {
        let subtitle = filterLabel(o.filter)
        if let reason = DataText.missingReason(ctx.snapshot, sources: [SourceNames.preferences, SourceNames.progress]) {
            return ShamorZachorModel(state: .missing(reason), subtitle: subtitle)
        }
        let snap = ctx.snapshot!
        let list = snap.shamorZachor.filter { matches($0, o.filter) }
        if list.isEmpty {
            let text: String
            switch o.filter {
            case .completed: text = "אין ספרים שהושלמו"
            case .all: text = "לא נמצאו ספרים בשמור וזכור"
            case .inProgress: text = "אין ספרים בתהליך"
            }
            return ShamorZachorModel(state: .empty(text), subtitle: subtitle)
        }
        let suffix: String
        switch o.filter {
        case .completed: suffix = " הושלמו"
        case .all: suffix = " ספרים"
        case .inProgress: suffix = " בתהליך"
        }
        var model = ShamorZachorModel(state: .ok, subtitle: subtitle, headerEnd: "\(list.count)\(suffix)")
        if snap.sources.contains(where: { $0.name == SourceNames.library && $0.health != .ok }) { model.note = DataText.libraryPartial }

        let allCols = o.allColumns && ctx.size == .large
        let def = ctx.size == .small ? 1 : ctx.size == .medium ? 3 : (allCols ? 2 : 4)
        var n = ctx.size.rows(count: o.count, defaultRows: def)
        if allCols { n = min(n, 4) }
        model.rows = list.prefix(40).prefix(n).map { row($0, allColumns: allCols, size: ctx.size) }
        return model
    }

    static func row(_ b: BookProgress, allColumns: Bool, size: WidgetSize) -> ProgressRow {
        var sub = b.statusText
        if b.isCompleted {
            if let d = b.completedAt {
                let c = HebrewFormat.gregorianLocal.dateComponents([.month, .day], from: d)
                sub = "הושלם ב-" + HebrewFormat.gregorianShort(month: c.month ?? 1, day: c.day ?? 1)
            } else { sub = "הושלם" }
        }
        let columns = allColumns ? b.visibleColumns.map {
            ProgressBarRow(label: $0.label, ratio: $0.ratio, pct: HebrewFormat.percent($0.ratio))
        } : []
        return ProgressRow(
            title: HebrewFormat.clip(b.title, 60), meta: b.isCompleted ? "הושלם ✓" : HebrewFormat.percent(b.headline),
            sub: size == .large ? sub : "", cycle: b.isCompleted ? "" : (b.currentCycleText ?? ""),
            ratio: b.headline, complete: b.isCompleted, columns: columns,
            url: WidgetLinks.url(b.deepLink, fallback: WidgetLinks.shamorZachor))
    }
}
