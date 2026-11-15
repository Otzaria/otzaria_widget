import XCTest
@testable import OtzariaCore

final class UserStateTests: XCTestCase {
    typealias F = UserStateFixture
    private let reader = UserStateReader()

    func testRecentBooksNewestFirstDedupedByIdentity() throws {
        let f = F()
        f.setList("history", "history", [
            F.entry(F.book(1, "א"), index: 10, ref: "א, ב"),
            F.search("שלום"),
            F.entry(F.book(2, "ב"), index: 5),
            F.entry(F.book(1, "א"), index: 3),                         // older duplicate of book 1
            F.entry(F.book(1, "א", type: "PdfBook", path: "/p/a.pdf"), index: 7), // PDF identity differs
            F.entry(F.book(3, "ג"), index: 0, targetKind: "commentators"),
        ])
        let r = try reader.readRecentBooks(path: f.path, max: 10)
        XCTAssertEqual(r.map(\.book.title), ["א", "ב", "א", "ג"])
        XCTAssertEqual(r[0].index, 10)
        XCTAssertEqual(r[0].ref, "א, ב")
        XCTAssertEqual(r[0].deepLink, "otzaria://open/book/1?index=10")
        XCTAssertEqual(r[2].deepLink, "otzaria://open/pdf/1?index=7")
        XCTAssertEqual(r[3].targetKind, "commentators")
        XCTAssertEqual(try reader.readRecentBooks(path: f.path, max: 2).count, 2)
        XCTAssertTrue(try reader.readRecentBooks(path: f.path, max: 0).isEmpty)
    }

    func testRecentSearches() throws {
        let f = F()
        f.setList("history", "history", [
            F.search("שלום עולם", mode: "Exact", ref: "(ק)שלום"),
            F.entry(F.book(1, "א"), index: 0),
            F.search("שלום עולם", mode: "exact"),   // duplicate
            F.search("שלום עולם", mode: "fuzzy"),   // same query, other mode
            F.search("   "),
        ])
        let r = try reader.readRecentSearches(path: f.path, max: 10)
        XCTAssertEqual(r.map(\.mode), ["exact", "fuzzy"])
        XCTAssertEqual(r[0].displayText, "(ק)שלום")
        XCTAssertEqual(r[0].deepLink, "otzaria://open/search?q=%D7%A9%D7%9C%D7%95%D7%9D%20%D7%A2%D7%95%D7%9C%D7%9D&mode=exact")
    }

    func testBookmarksReversedAndGroups() throws {
        let f = F()
        f.setList("bookmarks", "key-bookmarks", [
            F.entry(F.book(1, "ראשון"), index: 1, label: "תווית", createdAt: "2026-01-02T03:04:05.123456"),
            F.entry(F.book(2, "שני"), index: 2),
        ])
        f.setList("bookmarks", "key-bookmark-groups", [
            ["id": "g1", "name": "קבוצה א", "items": [F.entry(F.book(1, "א"), index: 1), F.search("x")], "createdAt": "2026-01-01T00:00:00Z"],
            ["id": "g2", "items": [[String: Any]()]],
            ["name": "no id"],
        ])
        let b = try reader.readBookmarks(path: f.path)
        XCTAssertEqual(b.map(\.book.title), ["שני", "ראשון"])
        XCTAssertEqual(b[1].label, "תווית")
        XCTAssertNotNil(b[1].createdAt)
        let g = try reader.readBookmarkGroups(path: f.path)
        XCTAssertEqual(g.map(\.id), ["g2", "g1"])
        XCTAssertEqual(g[1].name, "קבוצה א")
        XCTAssertEqual(g[1].items.count, 1)
        XCTAssertEqual(g[0].name, "")
        XCTAssertNotNil(g[1].createdAt)
    }

    func testWorkspaces() throws {
        let f = F()
        f.setList("workspaces", "key-workspaces", [
            ["id": "w1", "name": "שולחן עבודה 1", "isPinned": true, "tabs": [
                ["type": "TextBookTab", "title": "ספר", "book": F.book(1, "ספר")],
                ["type": "PdfCommentatorsTab"], ["type": "Weird"], ["title": "no type"], "junk",
                ["type": "ToolTab", "toolId": "x"],
            ]],
            ["name": "בלי מזהה", "tabs": []],
            ["id": "w3"],
        ])
        f.setSession(slot: 1, tabs: [], current: 0, activeWorkspace: "w1")
        let w = try reader.readWorkspaces(path: f.path)
        XCTAssertEqual(w.map(\.name), ["שולחן עבודה 1", "בלי מזהה"])
        XCTAssertEqual(w[0].tabCount, 2)
        XCTAssertEqual(w[0].tabTitles, ["ספר", "ToolTab"])
        XCTAssertTrue(w[0].isPinned)
        XCTAssertTrue(w[0].isActive)
        XCTAssertEqual(w[1].id, "#1")
        XCTAssertFalse(w[1].isActive)
    }

    func testOpenTabsReadingsAndCurrent() throws {
        let f = F()
        let text: [String: Any] = ["type": "TextBookTab", "title": "ספר א", "book": F.book(10, "ספר א"), "initalIndex": 40,
                                   "readingAnchor": ["line": 55, "intraLineFraction": 0.1], "commentators": ["רש\"י"]]
        let pdf: [String: Any] = ["type": "PdfBookTab", "book": F.book(11, "תלמוד", type: "PdfBook"), "pageNumber": 0]
        let combined: [String: Any] = ["type": "CombinedTab", "rightTab": ["type": "ToolTab"], "leftTab": text]
        let comm: [String: Any] = ["type": "CommentatorsTab", "sourceTab": text, "initialIndex": 9, "selectedCommentators": ["תוס'"]]
        let search: [String: Any] = ["type": "SearchingTabWindow", "searchText": "מילה"]
        f.setSession(slot: 1, tabs: [text, pdf, combined, comm, search, ["nope": 1]], current: 3)
        f.setSession(slot: 2, tabs: [text], current: 0)
        let tabs = try reader.readOpenTabs(path: f.path)
        XCTAssertEqual(tabs.count, 5)
        XCTAssertEqual(tabs.map(\.tabIndex), [0, 1, 2, 3, 4])
        XCTAssertEqual(tabs.map(\.isCurrent), [false, false, false, true, false])
        XCTAssertEqual(tabs[0].reading?.index, 55, "a valid reading anchor wins over initalIndex")
        XCTAssertEqual(tabs[0].reading?.commentators, ["רש\"י"])
        XCTAssertEqual(tabs[0].deepLink, "otzaria://open/book/10?index=55")
        XCTAssertEqual(tabs[1].reading?.index, 1)
        XCTAssertEqual(tabs[1].deepLink, "otzaria://open/pdf/11?index=1")
        XCTAssertEqual(tabs[2].title, "ToolTab")
        XCTAssertEqual(tabs[2].reading?.book.id, 10)
        XCTAssertEqual(tabs[3].reading?.targetKind, "commentators")
        XCTAssertEqual(tabs[3].reading?.index, 9)
        XCTAssertEqual(tabs[3].reading?.commentators, ["תוס'"])
        XCTAssertEqual(tabs[4].deepLink, "otzaria://open/search?q=%D7%9E%D7%99%D7%9C%D7%94")
        XCTAssertNil(tabs[4].reading)
    }

    func testCurrentIndexClampedAndFallsBackToRestorableTab() throws {
        let f = F()
        f.setSession(slot: 1, tabs: [["type": "ToolTab"], ["x": 1], ["type": "ToolTab"]], current: 1)
        XCTAssertEqual(try reader.readOpenTabs(path: f.path).map(\.isCurrent), [true, false])
        f.setSession(slot: 1, tabs: [["type": "ToolTab"], ["type": "ToolTab"]], current: 99)
        XCTAssertEqual(try reader.readOpenTabs(path: f.path).map(\.isCurrent), [false, true])
    }

    func testDefensiveParsingSkipsBrokenEntries() throws {
        let f = F()
        f.setList("history", "history", [
            "junk", 5, ["book": "notobject"], ["ref": "x"],
            ["book": ["id": 1, "type": "WeirdBook", "title": "x"], "index": 0],
            ["book": ["id": 1, "type": "TextBook"], "index": 0],
            ["book": ["id": "7", "title": "מחרוזת", "type": "TextBook"], "index": "12"],
            ["book": ["id": 8, "title": "נקודה", "type": "TextBook"], "index": 3.0, "targetKind": "other"],
            ["book": ["id": 9, "title": "ללא index", "type": "TextBook"], "readingAnchor": ["line": 4]],
            ["book": ["id": 12, "title": "ספר משתמש", "type": "TextBook", "isUserBook": true]],
            ["book": ["id": 13, "title": "מקור לא מוכר", "type": "TextBook", "source": "zzz"]],
        ])
        let r = try reader.readRecentBooks(path: f.path, max: 50)
        XCTAssertEqual(r.map(\.book.title), ["מחרוזת", "נקודה", "ללא index", "ספר משתמש", "מקור לא מוכר"])
        XCTAssertEqual(r[0].index, 12)
        XCTAssertEqual(r[0].book.id, 7)
        XCTAssertEqual(r[1].index, 3)
        XCTAssertEqual(r[1].targetKind, "book")
        XCTAssertEqual(r[2].index, 4)
        XCTAssertEqual(r[3].book.source, "u")
        XCTAssertEqual(r[3].deepLink, "otzaria://open/book/12?source=user&index=0")
        XCTAssertEqual(r[4].book.source, "o")
    }

    func testExternalAndFileIdentities() throws {
        let f = F()
        f.setList("history", "history", [
            ["book": ["otzarId": 55, "bookName": "אוצר", "type": "ExternalLibraryBook"], "index": 0],
            ["book": ["otzarId": 55, "title": "אוצר", "type": "ExternalLibraryBook"], "index": 1],
            ["book": ["title": "קובץ", "type": "PdfBook", "path": "/a.pdf"], "index": 1],
            ["book": ["title": "קובץ", "type": "PdfBook", "path": "/b.pdf"], "index": 1],
            ["book": ["title": "קובץ", "type": "PdfBook", "path": "/a.pdf"], "index": 9],
            ["book": ["id": 4, "title": "ז", "type": "TextBook", "source": "d:lib", "versionTitle": "מהדורה"], "index": 0],
            ["book": ["id": 4, "title": "ז", "type": "TextBook", "source": "d:lib"], "index": 0],
        ])
        let r = try reader.readRecentBooks(path: f.path, max: 50)
        XCTAssertEqual(r.count, 5)
        XCTAssertNil(r[0].book.id)
        XCTAssertEqual(r[0].book.externalLibraryId, "55")
        XCTAssertNil(r[0].deepLink)
        XCTAssertEqual(r.map(\.book.versionTitle), [nil, nil, nil, "מהדורה", nil])
    }

    func testSchemaProbe() {
        XCTAssertEqual(reader.probe(path: "/nonexistent/user_state.db").health, .notFound)
        let ok = F(), v2 = F(userVersion: 2), noSchema = F(createSchema: false)
        XCTAssertEqual(reader.probe(path: ok.path), SourceStatus(name: "user_state.db", health: .ok))
        let future = reader.probe(path: v2.path)
        XCTAssertEqual(future.health, .unknownSchema)
        XCTAssertTrue(future.message?.contains("(2)") == true)
        XCTAssertEqual(reader.probe(path: noSchema.path).health, .unknownSchema)
        let dir = TempDir()
        XCTAssertEqual(reader.probe(path: dir.write("user_state.db", "not a database at all, just text, long enough to fail")).health, .unreadable)
    }

    func testUnknownSchemaAndMissingRowsYieldEmpty() throws {
        let future = F(userVersion: 5)
        future.setList("history", "history", [F.entry(F.book(1, "א"), index: 0)])
        XCTAssertTrue(try reader.readRecentBooks(path: future.path, max: 5).isEmpty)
        let empty = F()
        let all = try reader.readAll(path: empty.path, maxBooks: 5, maxSearches: 5)
        XCTAssertEqual(all, UserStateData())
    }

    func testCorruptPayloadsYieldEmpty() throws {
        let f = F()
        f.setListRaw("history", "history", "{broken")
        f.setListRaw("bookmarks", "key-bookmarks", "{\"not\":\"array\"}")
        f.setListRaw("bookmarks", "key-bookmark-groups", "")
        let all = try reader.readAll(path: f.path, maxBooks: 5, maxSearches: 5)
        XCTAssertEqual(all, UserStateData())
    }

    func testTrailingCommasAreAccepted() throws {
        let f = F()
        f.setListRaw("history", "history", "[{\"book\":{\"id\":1,\"title\":\"א\",\"type\":\"TextBook\"},\"index\":2,},]")
        XCTAssertEqual(try reader.readRecentBooks(path: f.path, max: 5).first?.index, 2)
    }

    func testReadAllMatchesIndividualReads() throws {
        let f = F()
        f.setList("history", "history", [F.entry(F.book(1, "א"), index: 1), F.search("ש")])
        f.setList("bookmarks", "key-bookmarks", [F.entry(F.book(2, "ב"), index: 2)])
        f.setSession(slot: 1, tabs: [["type": "TextBookTab", "book": F.book(1, "א"), "initalIndex": 3]], current: 0)
        let all = try reader.readAll(path: f.path, maxBooks: 30, maxSearches: 20)
        XCTAssertEqual(all.recentBooks, try reader.readRecentBooks(path: f.path, max: 30))
        XCTAssertEqual(all.recentSearches, try reader.readRecentSearches(path: f.path, max: 20))
        XCTAssertEqual(all.bookmarks, try reader.readBookmarks(path: f.path))
        XCTAssertEqual(all.openTabs, try reader.readOpenTabs(path: f.path))
    }

    func testReadsWalDatabaseWhileWriterHoldsItOpen() throws {
        let f = F(wal: true)
        f.setList("history", "history", [F.entry(F.book(1, "א"), index: 1)])
        // The writer connection stays open (WAL file present) while we read.
        XCTAssertTrue(FileManager.default.fileExists(atPath: f.path + "-wal"))
        XCTAssertEqual(try reader.readRecentBooks(path: f.path, max: 5).count, 1)
        f.setList("history", "history", [F.entry(F.book(1, "א"), index: 1), F.entry(F.book(2, "ב"), index: 1)])
        XCTAssertEqual(try reader.readRecentBooks(path: f.path, max: 5).count, 2)
    }

    func testReaderNeverWrites() throws {
        let f = F()
        f.setList("history", "history", [F.entry(F.book(1, "א"), index: 1)])
        f.sqlite.close()
        let before = try Data(contentsOf: URL(fileURLWithPath: f.path))
        _ = try reader.readAll(path: f.path, maxBooks: 5, maxSearches: 5)
        _ = reader.probe(path: f.path)
        XCTAssertEqual(try Data(contentsOf: URL(fileURLWithPath: f.path)), before)
    }

    func testChangeTokenChangesWithContent() throws {
        let f = F()
        f.setList("history", "history", [])
        let t1 = reader.changeToken(path: f.path)
        XCTAssertNotNil(t1)
        XCTAssertEqual(t1, reader.changeToken(path: f.path))
        f.sqlite.exec("UPDATE lists SET updated_at = 99")
        XCTAssertNotEqual(t1, reader.changeToken(path: f.path))
        XCTAssertNil(reader.changeToken(path: "/nonexistent.db"))
    }

    func testCopyFallbackReadsPrivateCopy() throws {
        let f = F(wal: true)
        f.setList("history", "history", [F.entry(F.book(1, "א"), index: 1)])
        let n = try UserStateReader.runOnCopy(f.path) { conn in
            try conn.query("SELECT payload_json FROM lists WHERE box = 'history'") { $0.text(0) }.count
        }
        XCTAssertEqual(n, 1)
    }

    func testPathWithSpacesAndHebrew() throws {
        let dir = TempDir()
        let sub = dir.file("תיקייה עם רווח #1 ?x")
        try FileManager.default.createDirectory(atPath: sub, withIntermediateDirectories: true)
        let fixture = F()
        fixture.sqlite.close()
        let target = sub + "/user_state.db"
        try FileManager.default.copyItem(atPath: fixture.path, toPath: target)
        XCTAssertEqual(reader.probe(path: target).health, .ok)
    }
}
