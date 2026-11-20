import XCTest
import SQLite3
@testable import OtzariaCore

final class DataServiceTests: XCTestCase {
    typealias F = UserStateFixture

    /// A fake Otzaria installation under a temp home: Application Support/otzaria with hive, databases and a library.
    private final class Install {
        let home = TempDir()
        var root: String { home.path + "/Library/Application Support/otzaria" }
        var env: TempHomeEnv { TempHomeEnv(homeDirectory: home.path) }
        let userState: UserStateFixture

        init(withLibrary: Bool = true) {
            userState = UserStateFixture()
            let fm = FileManager.default
            try! fm.createDirectory(atPath: root + "/databases", withIntermediateDirectories: true)
            userState.sqlite.close()
            try! fm.copyItem(atPath: userState.path, toPath: root + "/databases/user_state.db")
            var w = HiveTestWriter()
            w.put("key-selected-city", HiveTestWriter.str("חיפה"))
            w.put("key-calendar-twelve-hour-clock", HiveTestWriter.bool(true))
            w.put("key-library-path", HiveTestWriter.str(root + "/books"))
            w.put("sz:tracked_books", HiveTestWriter.str("[1]"))
            w.put("sz:progress_by_id", HiveTestWriter.str("{\"1\":{\"0\":{\"learn\":true}}}"))
            home.write("Library/Application Support/otzaria/app_preferences.hive", bytes: w.bytes)
            if withLibrary {
                let lib = SQLiteFixture(name: "seforim.db")
                lib.exec("""
                    CREATE TABLE category (id INTEGER PRIMARY KEY, title TEXT, level INTEGER);
                    CREATE TABLE category_closure (ancestorId INTEGER, descendantId INTEGER);
                    CREATE TABLE book (id INTEGER PRIMARY KEY, categoryId INTEGER, title TEXT, totalLines INTEGER);
                    CREATE TABLE tocEntry (id INTEGER PRIMARY KEY, bookId INTEGER, parentId INTEGER);
                    INSERT INTO category VALUES (1,'תנ"ך',0);
                    INSERT INTO category_closure VALUES (1,1);
                    INSERT INTO book VALUES (1,1,'בראשית',4);
                    """)
                lib.close()
                try! fm.createDirectory(atPath: root + "/books", withIntermediateDirectories: true)
                try! fm.copyItem(atPath: lib.path, toPath: root + "/books/seforim.db")
            }
        }

        func writeUserState(_ build: (UserStateFixture) -> Void) {
            let fixture = UserStateFixture()
            build(fixture)
            fixture.sqlite.close()
            try? FileManager.default.removeItem(atPath: root + "/databases/user_state.db")
            try! FileManager.default.copyItem(atPath: fixture.path, toPath: root + "/databases/user_state.db")
        }
    }

    private func service(_ install: Install, settings: @escaping @Sendable () -> WidgetSettings = { WidgetSettings() },
                         clock: @escaping @Sendable () -> Date = { Date() }) -> OtzariaDataService {
        OtzariaDataService(settingsProvider: settings, pathResolver: OtzariaPathResolver(environment: install.env), clock: clock)
    }

    func testFullSnapshot() {
        let install = Install()
        install.writeUserState { f in
            f.setList("history", "history", [F.entry(F.book(1, "בראשית"), index: 2), F.search("מילה")])
            f.setList("bookmarks", "key-bookmarks", [F.entry(F.book(1, "בראשית"), index: 1)])
            f.setSession(slot: 1, tabs: [["type": "TextBookTab", "book": F.book(1, "בראשית"), "initalIndex": 3]], current: 0)
        }
        let s = service(install).snapshot()
        XCTAssertEqual(s.recentBooks.count, 1)
        XCTAssertEqual(s.recentSearches.map(\.query), ["מילה"])
        XCTAssertEqual(s.bookmarks.count, 1)
        XCTAssertEqual(s.openTabs.first?.isCurrent, true)
        XCTAssertEqual(s.preferences.selectedCity, "חיפה")
        XCTAssertTrue(s.preferences.twelveHourClock)
        XCTAssertEqual(s.progress.map(\.title), ["בראשית"])
        XCTAssertEqual(s.progress.first?.totalItems, 4)
        XCTAssertEqual(s.status.first { $0.name == SourceNames.userState }?.health, .ok)
        XCTAssertEqual(s.status.first { $0.name == SourceNames.library }?.health, .ok)
        XCTAssertEqual(s.paths.dataRoot, install.root)
    }

    func testMissingEverythingGivesHebrewNotFoundStatuses() {
        let home = TempDir()
        let svc = OtzariaDataService(pathResolver: OtzariaPathResolver(environment: TempHomeEnv(homeDirectory: home.path)))
        let s = svc.snapshot()
        XCTAssertTrue(s.recentBooks.isEmpty)
        XCTAssertEqual(s.status.first { $0.name == SourceNames.userState }, SourceStatus(name: "user_state.db", health: .notFound, message: "לא נמצא קובץ user_state.db"))
        XCTAssertEqual(s.status.first { $0.name == SourceNames.preferences }?.message, "לא נמצא קובץ app_preferences.hive")
        XCTAssertEqual(s.status.first { $0.name == SourceNames.library }?.message, "לא נמצא קובץ seforim.db — לא ניתן לחשב התקדמות בשמור וזכור")
    }

    func testHideSearchesSetting() {
        let install = Install()
        install.writeUserState { $0.setList("history", "history", [F.search("סוד")]) }
        var mutable = WidgetSettings()
        mutable.hideSearches = true
        let s = mutable
        XCTAssertTrue(service(install, settings: { s }).snapshot().recentSearches.isEmpty)
        let widget = service(install, settings: { s }).widgetSnapshot()
        XCTAssertTrue(widget.recentSearches.isEmpty)
    }

    func testCachingReloadAndChangePolling() {
        let install = Install()
        install.writeUserState { $0.setList("history", "history", [F.entry(F.book(1, "א"), index: 1)]) }
        let svc = service(install)
        let first = svc.snapshot()
        XCTAssertEqual(svc.snapshot().readAt, first.readAt, "cached")
        XCTAssertNil(svc.refreshIfChanged(), "nothing changed")
        install.writeUserState { $0.setList("history", "history", [F.entry(F.book(1, "א"), index: 1), F.entry(F.book(2, "ב"), index: 1)]) }
        // content differs but updated_at is constant in the fixture: the file size changes the token only via data; force it
        let changed = svc.reload()
        XCTAssertEqual(changed.recentBooks.count, 2)
        svc.invalidate()
        XCTAssertEqual(svc.snapshot().recentBooks.count, 2)
    }

    func testRefreshIfChangedDetectsUpdatedAt() {
        let install = Install()
        install.writeUserState { $0.setList("history", "history", []) }
        let svc = service(install)
        _ = svc.snapshot()
        XCTAssertNil(svc.refreshIfChanged())
        let path = install.root + "/databases/user_state.db"
        var db: OpaquePointer?
        XCTAssertEqual(sqlite3_open(path, &db), SQLITE_OK)
        sqlite3_exec(db, "UPDATE lists SET updated_at = 12345", nil, nil, nil)
        sqlite3_close(db)
        XCTAssertNotNil(svc.refreshIfChanged())
        XCTAssertNil(svc.refreshIfChanged())
    }

    func testStaleCacheIsRebuilt() {
        let install = Install()
        let box = ClockBox()
        let svc = service(install, clock: { box.now })
        let a = svc.snapshot()
        box.now = box.now.addingTimeInterval(OtzariaDataService.cacheMaxAge + 1)
        XCTAssertNotEqual(svc.snapshot().readAt, a.readAt)
    }

    func testBrokenUserStateIsReportedNotThrown() {
        let install = Install()
        try! "garbage garbage garbage garbage".write(toFile: install.root + "/databases/user_state.db", atomically: true, encoding: .utf8)
        let s = service(install).snapshot()
        XCTAssertEqual(s.status.first { $0.name == SourceNames.userState }?.health, .unreadable)
        XCTAssertTrue(s.recentBooks.isEmpty)
        XCTAssertEqual(s.preferences.selectedCity, "חיפה")
        let w = WidgetSnapshot.make(from: s, settings: WidgetSettings())
        XCTAssertEqual(w.health, .unreadable)
        XCTAssertNotNil(w.healthMessage)
    }

    func testDoesNotModifyOtzariaFiles() throws {
        let install = Install()
        install.writeUserState { $0.setList("history", "history", [F.entry(F.book(1, "א"), index: 1)]) }
        func state() throws -> [String: Data] {
            var out: [String: Data] = [:]
            let enumerator = FileManager.default.enumerator(atPath: install.root)!
            for case let rel as String in enumerator {
                var isDir: ObjCBool = false
                let full = install.root + "/" + rel
                FileManager.default.fileExists(atPath: full, isDirectory: &isDir)
                if !isDir.boolValue { out[rel] = try Data(contentsOf: URL(fileURLWithPath: full)) }
            }
            return out
        }
        let before = try state()
        _ = service(install).reload()
        XCTAssertEqual(try state(), before)
    }
}

final class ClockBox: @unchecked Sendable {
    var now = Date()
}
