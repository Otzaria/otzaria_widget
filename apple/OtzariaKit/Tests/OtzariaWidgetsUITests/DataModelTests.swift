import XCTest
import OtzariaCore
@testable import OtzariaWidgetsUI

final class DataModelTests: XCTestCase {
    func testSizeCapsAndAutoCount() {
        XCTAssertEqual(WidgetSize.small.maxRows, 2)
        XCTAssertEqual(WidgetSize.medium.maxRows, 4)
        XCTAssertEqual(WidgetSize.large.maxRows, 8)
        XCTAssertEqual(WidgetSize.small.rows(count: 0, defaultRows: 1), 1)
        XCTAssertEqual(WidgetSize.medium.rows(count: 0, defaultRows: 3), 3)
        XCTAssertEqual(WidgetSize.medium.rows(count: 8, defaultRows: 3), 4)
        XCTAssertEqual(WidgetSize.large.rows(count: 8, defaultRows: 7), 8)
        XCTAssertEqual(WidgetSize.small.rows(count: 8, defaultRows: 1), 2)
    }

    func testRecentBooksHeroIsOpenTab() {
        let m = RecentBooksModel.make(T.ctx(.large), RecentBooksOptions())
        XCTAssertEqual(m.state, .ok)
        XCTAssertEqual(m.heroLabel, "המשך קריאה")
        XCTAssertEqual(m.hero?.title, "ברכות")
        XCTAssertEqual(m.hero?.meta, "")
        // the hero book is not repeated in the list; large default = hero + 6
        XCTAssertEqual(m.rows.count, 6)
        XCTAssertFalse(m.rows.contains { $0.title == "ברכות" })
    }

    func testRecentBooksPerSizeAndCaps() {
        let o = RecentBooksOptions()
        XCTAssertEqual(RecentBooksModel.make(T.ctx(.small), o).rows.count, 0)
        XCTAssertEqual(RecentBooksModel.make(T.ctx(.medium), o).rows.count, 2)
        XCTAssertEqual(RecentBooksModel.make(T.ctx(.medium), RecentBooksOptions(count: 8)).rows.count, 3)
        XCTAssertEqual(RecentBooksModel.make(T.ctx(.large), RecentBooksOptions(count: 3)).rows.count, 2)
    }

    func testRecentBooksFallsBackToNewestAndFiltersPdf() {
        var snap = Fixtures.snapshot(now: T.now)
        snap.currentTab = nil
        snap.recentBooks = [T.entry(1, "קובץ", pdf: true), T.entry(2, "ספר")]
        let m = RecentBooksModel.make(T.ctx(.medium, snapshot: snap), RecentBooksOptions(includePdfs: false))
        XCTAssertEqual(m.heroLabel, "קריאה אחרונה")
        XCTAssertEqual(m.hero?.title, "ספר")
        let withPdf = RecentBooksModel.make(T.ctx(.medium, snapshot: snap), RecentBooksOptions())
        XCTAssertEqual(withPdf.hero?.title, "קובץ")
        XCTAssertEqual(withPdf.hero?.meta.hasPrefix("PDF"), true)
    }

    func testRecentBooksLocationToggleAndSearches() {
        let hidden = RecentBooksModel.make(T.ctx(.large), RecentBooksOptions(showLocation: false))
        XCTAssertEqual(hidden.hero?.sub, "")
        XCTAssertTrue(hidden.rows.allSatisfy { $0.sub.isEmpty })
        XCTAssertEqual(RecentBooksModel.make(T.ctx(.medium), RecentBooksOptions(showSearches: true)).searches.count, 1)
        XCTAssertEqual(RecentBooksModel.make(T.ctx(.large), RecentBooksOptions(showSearches: true)).searches.count, 2)
        XCTAssertEqual(RecentBooksModel.make(T.ctx(.small), RecentBooksOptions(showSearches: true)).searches.count, 0)
        var settings = WidgetSettings()
        settings.hideSearches = true
        XCTAssertEqual(RecentBooksModel.make(T.ctx(.large, settings: settings), RecentBooksOptions(showSearches: true)).searches.count, 0)
    }

    func testRecentBooksStatesAndLinks() {
        XCTAssertEqual(RecentBooksModel.make(T.ctx(.medium, snapshot: nil), RecentBooksOptions()).state, .missing(DataText.noSnapshot))
        var empty = Fixtures.snapshot(now: T.now)
        empty.currentTab = nil
        empty.recentBooks = []
        XCTAssertEqual(RecentBooksModel.make(T.ctx(.medium, snapshot: empty), RecentBooksOptions()).state, .empty("לא נמצאו ספרים אחרונים"))
        var bad = Fixtures.snapshot(now: T.now)
        bad.sources = [SourceStatus(name: SourceNames.userState, health: .unreadable, message: "קובץ נעול")]
        XCTAssertEqual(RecentBooksModel.make(T.ctx(.medium, snapshot: bad), RecentBooksOptions()).state, .missing("קובץ נעול"))
        let m = RecentBooksModel.make(T.ctx(.large), RecentBooksOptions())
        XCTAssertEqual(m.rows[0].url.scheme, "otzaria")
        XCTAssertEqual(m.url.absoluteString, "otzaria://open/history")
    }

    func testBookmarksGroupsAndCounts() {
        let all = BookmarksModel.make(T.ctx(.large), BookmarksOptions())
        XCTAssertEqual(all.rows.count, 5)
        XCTAssertEqual(BookmarksModel.make(T.ctx(.medium), BookmarksOptions()).rows.count, 3)
        XCTAssertEqual(BookmarksModel.make(T.ctx(.medium), BookmarksOptions(count: 8)).rows.count, 4)
        let g = BookmarksModel.make(T.ctx(.large), BookmarksOptions(group: "g1"))
        XCTAssertEqual(g.groupName, "הלכה")
        XCTAssertEqual(g.rows.count, 2)
        let none = BookmarksModel.make(T.ctx(.large), BookmarksOptions(group: BookmarksOptions.noGroup))
        XCTAssertEqual(none.groupName, "ללא קבוצה")
        XCTAssertEqual(none.rows.count, 3)
        // an unknown group id behaves like "all"
        XCTAssertEqual(BookmarksModel.make(T.ctx(.large), BookmarksOptions(group: "zzz")).rows.count, 5)
    }

    func testBookmarkRowTexts() {
        let m = BookmarksModel.make(T.ctx(.large), BookmarksOptions())
        XCTAssertEqual(m.rows[0].title, "הלכות שבת")
        XCTAssertTrue(m.rows[0].sub.hasPrefix("משנה ברורה"))
        XCTAssertEqual(m.rows[1].title, "ספר החינוך")
        var snap = Fixtures.snapshot(now: T.now)
        snap.bookmarkGroups = [BookmarkGroup(id: "e", name: "ריקה")]
        XCTAssertEqual(BookmarksModel.make(T.ctx(.large, snapshot: snap), BookmarksOptions(group: "e")).state, .empty("אין סימניות בקבוצה זו"))
        snap.bookmarks = []
        XCTAssertEqual(BookmarksModel.make(T.ctx(.large, snapshot: snap), BookmarksOptions()).state, .empty("לא נמצאו סימניות"))
    }

    func testShamorZachorFilters() {
        func titles(_ f: ShamorZachorFilter) -> [String] {
            ShamorZachorModel.make(T.ctx(.large), ShamorZachorOptions(count: 8, filter: f)).rows.map(\.title)
        }
        XCTAssertEqual(titles(.inProgress), ["ברכות", "שבת", "עירובין", "פסחים"])
        XCTAssertEqual(titles(.completed), ["יומא"])
        XCTAssertEqual(titles(.all).count, 5)
        let none = ShamorZachorModel.make(T.ctx(.large, snapshot: {
            var s = Fixtures.snapshot(now: T.now); s.shamorZachor = []; return s
        }()), ShamorZachorOptions())
        XCTAssertEqual(none.state, .empty("אין ספרים בתהליך"))
    }

    func testShamorZachorRowsAndColumns() {
        XCTAssertEqual(ShamorZachorModel.make(T.ctx(.small), ShamorZachorOptions()).rows.count, 1)
        XCTAssertEqual(ShamorZachorModel.make(T.ctx(.medium), ShamorZachorOptions()).rows.count, 3)
        XCTAssertEqual(ShamorZachorModel.make(T.ctx(.large), ShamorZachorOptions()).rows.count, 4)
        let cols = ShamorZachorModel.make(T.ctx(.large), ShamorZachorOptions(allColumns: true))
        XCTAssertEqual(cols.rows.count, 2)
        XCTAssertEqual(cols.rows[0].columns.count, 2)
        XCTAssertEqual(ShamorZachorModel.make(T.ctx(.medium), ShamorZachorOptions(allColumns: true)).rows[0].columns.count, 0)
        let m = ShamorZachorModel.make(T.ctx(.large), ShamorZachorOptions())
        XCTAssertEqual(m.rows[0].meta, "52%")
        XCTAssertEqual(m.headerEnd, "4 בתהליך")
        // Windows shows the count in the medium header too (and nothing when there is no list).
        XCTAssertEqual(ShamorZachorModel.make(T.ctx(.medium), ShamorZachorOptions()).headerEnd, "4 בתהליך")
        XCTAssertEqual(ShamorZachorModel.make(T.ctx(.medium), ShamorZachorOptions(filter: .completed)).headerEnd, "1 הושלמו")
        XCTAssertEqual(ShamorZachorModel.make(T.ctx(.medium, snapshot: nil), ShamorZachorOptions()).headerEnd, "")
        XCTAssertEqual(ShamorZachorModel.make(T.ctx(.medium), ShamorZachorOptions()).rows[0].sub, "")
        let done = ShamorZachorModel.make(T.ctx(.large), ShamorZachorOptions(filter: .completed)).rows[0]
        XCTAssertEqual(done.meta, "הושלם ✓")
        XCTAssertTrue(done.sub.hasPrefix("הושלם ב-"))
        XCTAssertTrue(done.complete)
    }

    func testShamorZachorLibraryNote() {
        var snap = Fixtures.snapshot(now: T.now)
        snap.sources = [SourceStatus(name: SourceNames.library, health: .notFound)]
        XCTAssertEqual(ShamorZachorModel.make(T.ctx(.large, snapshot: snap), ShamorZachorOptions()).note, DataText.libraryPartial)
    }

    func testMissingReasonRules() {
        XCTAssertEqual(DataText.missingReason(nil, sources: []), DataText.noSnapshot)
        var s = Fixtures.snapshot(now: T.now)
        XCTAssertNil(DataText.missingReason(s, sources: [SourceNames.userState]))
        s.health = .unreadable
        s.healthMessage = "הודעה"
        XCTAssertEqual(DataText.missingReason(s, sources: [SourceNames.userState]), "הודעה")
        s.sources = [SourceStatus(name: SourceNames.preferences, health: .notFound)]
        XCTAssertNil(DataText.missingReason(s, sources: [SourceNames.userState]))
        XCTAssertEqual(DataText.missingReason(s, sources: [SourceNames.preferences]), DataText.missing)
    }
}
