import Foundation
import OtzariaCore

/// Sample data for placeholders and previews.
enum Fixtures {
    static func snapshot(now: Date) -> WidgetSnapshot {
        func entry(_ id: Int, _ title: String, _ ref: String, label: String? = nil, pdf: Bool = false, ago: TimeInterval = 0) -> ReadingEntry {
            let book = BookRef(id: id, title: title, type: pdf ? "PdfBook" : "TextBook")
            var e = ReadingEntry(book: book, ref: ref, index: 3, label: label, createdAt: now.addingTimeInterval(-ago))
            e.deepLink = OtzariaLinks.buildLink(for: e)
            return e
        }
        let recents = [
            entry(1, "ברכות", "ברכות, ב.", ago: 3_600),
            entry(2, "משנה ברורה", "משנה ברורה סימן רכה", ago: 90_000),
            entry(3, "שולחן ערוך אורח חיים", "אורח חיים, סימן א", pdf: true, ago: 200_000),
            entry(4, "רמב״ם הלכות שבת", "הלכות שבת, פרק ה", ago: 400_000),
            entry(5, "תניא", "תניא, פרק לב", ago: 700_000),
            entry(6, "מסילת ישרים", "מסילת ישרים, פרק א", ago: 900_000),
            entry(7, "כוזרי", "מאמר ראשון", ago: 1_200_000),
        ]
        let marks = [
            entry(2, "משנה ברורה", "משנה ברורה סימן רכה", label: "הלכות שבת"),
            entry(8, "ספר החינוך", "מצוה ג"),
            entry(9, "חובות הלבבות", "שער הבטחון", label: "לקרוא שוב"),
            entry(10, "פירוש רש״י", "בראשית א, א"),
            entry(11, "אור החיים", "ויקרא ב"),
        ]
        func column(_ label: String, _ ratio: Double) -> ProgressColumn { ProgressColumn(id: label, label: label, done: Int(ratio * 100), ratio: ratio) }
        func book(_ id: Int, _ title: String, _ ratio: Double, done: Bool = false) -> BookProgress {
            let cols = [column("חזרה א׳", ratio), column("חזרה ב׳", ratio / 3)]
            return BookProgress(
                bookId: id, title: title, totalItems: 200, columns: cols, visibleColumns: cols, headline: ratio, isCompleted: done,
                isInProgress: !done, isTracked: true, completedAt: done ? now.addingTimeInterval(-86_400 * 5) : nil,
                statusText: done ? "" : "חצי הדרך מאחוריך!", currentCycleText: done ? nil : "\(Int(ratio * 200)) מתוך 200",
                deepLink: OtzariaLinks.buildBookOpenLink(bookId: id))
        }
        let progress = [book(1, "ברכות", 0.52), book(12, "שבת", 0.18), book(13, "עירובין", 0.77), book(14, "פסחים", 0.05), book(15, "יומא", 1, done: true)]
        let tab = OpenTabInfo(title: "ברכות", tabType: "TextBookTab", reading: recents[0], isCurrent: true, deepLink: recents[0].deepLink)
        return WidgetSnapshot(
            generatedAt: now, recentBooks: recents, currentTab: tab, bookmarks: marks,
            bookmarkGroups: [BookmarkGroup(id: "g1", name: "הלכה", items: Array(marks.prefix(2)))],
            recentSearches: [RecentSearch(query: "אין עומדין", mode: "exact", displayText: "אין עומדין"),
                             RecentSearch(query: "שלוש סעודות")],
            shamorZachor: progress)
    }
}
