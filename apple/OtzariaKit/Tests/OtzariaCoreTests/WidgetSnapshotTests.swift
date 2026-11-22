import XCTest
@testable import OtzariaCore

final class WidgetSnapshotTests: XCTestCase {
    private let now = Date(timeIntervalSince1970: 1_800_000_000)

    private func sampleData() -> OtzariaSnapshot {
        let book = BookRef(id: 5, title: "ספר", type: "TextBook")
        let entry = ReadingEntry(book: book, ref: "פרק א", index: 3, createdAt: now, deepLink: "otzaria://open/book/5?index=3")
        var prefs = OtzariaPreferences()
        prefs.selectedCity = "חיפה"
        prefs.enabledZmanim = ["alos"]
        prefs.twelveHourClock = true
        prefs.dayTransition = "tzais"
        prefs.darkMode = true
        prefs.lightSeedColorArgb = 0xFF11_2233
        return OtzariaSnapshot(
            readAt: now, paths: OtzariaPaths(), recentBooks: [entry], recentSearches: [RecentSearch(query: "מילה", mode: "exact")],
            bookmarks: [entry], bookmarkGroups: [BookmarkGroup(id: "g", name: "קבוצה", items: [entry])],
            openTabs: [OpenTabInfo(title: "א", tabType: "ToolTab", isCurrent: false),
                       OpenTabInfo(title: "ספר", tabType: "TextBookTab", reading: entry, isCurrent: true, tabIndex: 1,
                                   deepLink: entry.deepLink)],
            progress: [BookProgress(bookId: 5, title: "ספר", totalItems: 10, statusText: "התחלה מצוינת!")],
            preferences: prefs,
            status: [SourceStatus(name: SourceNames.userState, health: .ok)])
    }

    func testMakeAppliesPreferencesAndSettings() {
        let w = WidgetSnapshot.make(from: sampleData(), settings: WidgetSettings(), now: now)
        XCTAssertEqual(w.schemaVersion, 1)
        XCTAssertEqual(w.generatedAt, now)
        XCTAssertEqual(w.health, .ok)
        XCTAssertNil(w.healthMessage)
        XCTAssertEqual(w.currentTab?.title, "ספר")
        XCTAssertEqual(w.recentSearches.map(\.query), ["מילה"])
        XCTAssertEqual(w.preferences.selectedCity, "חיפה")
        XCTAssertEqual(w.preferences.enabledZmanim, ["alos"])
        XCTAssertTrue(w.preferences.twelveHourClock)
        XCTAssertEqual(w.preferences.dayTransition, "tzais")
        XCTAssertTrue(w.preferences.darkMode)
        XCTAssertEqual(w.preferences.lightSeedColorArgb, 0xFF11_2233)

        var s = WidgetSettings()
        s.cityOverride = "ירושלים"
        s.zmanimOverride = ["shkia", "tzais"]
        s.twelveHourClockOverride = false
        s.dayTransitionOverride = "midnight"
        s.hideSearches = true
        s.showYerushalmiDaf = true
        s.linkMode = .providerLaunch
        let o = WidgetSnapshot.make(from: sampleData(), settings: s, now: now)
        XCTAssertEqual(o.preferences.selectedCity, "ירושלים")
        XCTAssertEqual(o.preferences.enabledZmanim, ["shkia", "tzais"])
        XCTAssertFalse(o.preferences.twelveHourClock)
        XCTAssertEqual(o.preferences.dayTransition, "midnight")
        XCTAssertTrue(o.preferences.showYerushalmiDaf)
        XCTAssertEqual(o.preferences.linkMode, .providerLaunch)
        XCTAssertTrue(o.recentSearches.isEmpty)
    }

    func testBlankOverridesAreIgnored() {
        var s = WidgetSettings()
        s.cityOverride = "  "
        s.dayTransitionOverride = ""
        let w = WidgetSnapshot.make(from: sampleData(), settings: s, now: now)
        XCTAssertEqual(w.preferences.selectedCity, "חיפה")
        XCTAssertEqual(w.preferences.dayTransition, "tzais")
    }

    func testHealthSelection() {
        var data = sampleData()
        data.status = [SourceStatus(name: SourceNames.userState, health: .notFound, message: "לא נמצא קובץ user_state.db"),
                       SourceStatus(name: SourceNames.preferences, health: .unreadable, message: "x")]
        var w = WidgetSnapshot.make(from: data, settings: WidgetSettings(), now: now)
        XCTAssertEqual(w.health, .notFound)
        XCTAssertEqual(w.healthMessage, "לא נמצא קובץ user_state.db")
        data.status = [SourceStatus(name: SourceNames.userState, health: .ok),
                       SourceStatus(name: SourceNames.preferences, health: .notFound, message: "לא נמצא קובץ app_preferences.hive"),
                       SourceStatus(name: SourceNames.otzariaApp, health: .notFound, message: "ignored")]
        w = WidgetSnapshot.make(from: data, settings: WidgetSettings(), now: now)
        XCTAssertEqual(w.health, .notFound)
        XCTAssertEqual(w.healthMessage, "לא נמצא קובץ app_preferences.hive")
        data.status = [SourceStatus(name: SourceNames.otzariaApp, health: .notFound, message: "ignored")]
        XCTAssertEqual(WidgetSnapshot.make(from: data, settings: WidgetSettings(), now: now).health, .ok)
    }

    func testCapsListSizes() {
        var data = sampleData()
        let e = data.bookmarks[0]
        data.bookmarks = Array(repeating: e, count: 500)
        data.bookmarkGroups = Array(repeating: data.bookmarkGroups[0], count: 90)
        let w = WidgetSnapshot.make(from: data, settings: WidgetSettings(), now: now)
        XCTAssertEqual(w.bookmarks.count, WidgetSnapshot.maxBookmarks)
        XCTAssertEqual(w.bookmarkGroups.count, WidgetSnapshot.maxBookmarkGroups)
    }

    func testJSONRoundTripAndKeys() throws {
        let w = WidgetSnapshot.make(from: sampleData(), settings: WidgetSettings(), now: now)
        let data = try WidgetSnapshotStore.makeEncoder().encode(w)
        XCTAssertEqual(try WidgetSnapshotStore.makeDecoder().decode(WidgetSnapshot.self, from: data), w)
        let object = try XCTUnwrap(JSONSerialization.jsonObject(with: data) as? [String: Any])
        for key in ["schemaVersion", "generatedAt", "health", "sources", "recentBooks", "currentTab", "bookmarks",
                    "bookmarkGroups", "recentSearches", "shamorZachor", "preferences"] {
            XCTAssertNotNil(object[key], key)
        }
        XCTAssertEqual(object["generatedAt"] as? String, "2027-01-15T08:00:00Z")
        XCTAssertEqual(object["health"] as? String, "ok")
        let prefs = try XCTUnwrap(object["preferences"] as? [String: Any])
        XCTAssertEqual(prefs["linkMode"] as? String, "openUrl")
        XCTAssertEqual(prefs["selectedCity"] as? String, "חיפה")
    }

    func testMinimalExternalWriterJSONDecodes() throws {
        let json = """
        {"schemaVersion":1,"generatedAt":"2026-05-06T07:08:09.123Z",
         "recentBooks":[{"book":{"title":"ספר","id":3},"ref":"פרק","index":4,"deepLink":"otzaria://open/book/3?index=4","future":1}],
         "unknownTopLevel":true}
        """
        let w = try WidgetSnapshotStore.makeDecoder().decode(WidgetSnapshot.self, from: Data(json.utf8))
        XCTAssertEqual(w.health, .ok)
        XCTAssertEqual(w.recentBooks.first?.book.type, "TextBook")
        XCTAssertEqual(w.recentBooks.first?.book.source, "o")
        XCTAssertEqual(w.recentBooks.first?.index, 4)
        XCTAssertTrue(w.bookmarks.isEmpty)
        XCTAssertTrue(w.shamorZachor.isEmpty)
        XCTAssertEqual(w.preferences, WidgetPreferences())
        XCTAssertNil(w.currentTab)
    }

    func testStoreWritesAtomicallyAndReadsBack() throws {
        let dir = TempDir()
        let store = WidgetSnapshotStore(directory: dir.url.appendingPathComponent("nested/group"))
        XCTAssertNil(store.read())
        let w = WidgetSnapshot.make(from: sampleData(), settings: WidgetSettings(), now: now)
        try store.write(w)
        XCTAssertEqual(store.read(), w)
        try store.write(WidgetSnapshot(generatedAt: now, health: .notFound, healthMessage: "אין נתונים"))
        XCTAssertEqual(store.read()?.healthMessage, "אין נתונים")
        let files = try FileManager.default.contentsOfDirectory(atPath: store.directory.path)
        XCTAssertEqual(files, ["widget-snapshot.json"], "no temp files left behind")
    }

    func testStoreTreatsCorruptFileAsMissing() throws {
        let dir = TempDir()
        dir.write("widget-snapshot.json", "{not json")
        XCTAssertNil(WidgetSnapshotStore(directory: dir.url).read())
    }

    func testAppGroupHelpers() {
        XCTAssertEqual(WidgetSnapshotStore.iOSAppGroupID, "group.org.otzaria.widget")
        XCTAssertEqual(WidgetSnapshotStore.macOSAppGroupID(teamID: "ABCDE12345"), "ABCDE12345.org.otzaria.widget")
        // Without the entitlement there is no container (never crashes).
        _ = WidgetSnapshotStore.appGroupDirectory(groupID: "group.invalid.test")
        // An ad-hoc / unsigned build has no team: its group is not shared even when macOS hands out a container.
        XCTAssertTrue(WidgetSnapshotStore.isTeamSignedGroup("ABCDE12345.org.otzaria.widget", signingTeamID: "ABCDE12345"))
        XCTAssertFalse(WidgetSnapshotStore.isTeamSignedGroup("org.otzaria.widget", signingTeamID: nil))
        XCTAssertFalse(WidgetSnapshotStore.isTeamSignedGroup("ABCDE12345.org.otzaria.widget", signingTeamID: nil))
        XCTAssertFalse(WidgetSnapshotStore.isTeamSignedGroup("ZZZZZ99999.org.otzaria.widget", signingTeamID: "ABCDE12345"))
        XCTAssertFalse(WidgetSnapshotStore.isTeamSignedGroup("org.otzaria.widget", signingTeamID: ""))
        #if os(macOS)
        _ = WidgetSnapshotStore.currentSigningTeamID() // never crashes, whatever signed the test runner
        #endif
    }

    func testSettingsStoreRoundTripAndDefaults() throws {
        let dir = TempDir()
        let store = WidgetSettingsStore(directory: dir.url)
        XCTAssertEqual(store.load(), WidgetSettings())
        var s = WidgetSettings()
        s.dataRootOverride = "/x"
        s.linkMode = .providerLaunch
        s.theme = .dark
        s.hideSearches = true
        s.cityOverride = "חיפה"
        s.zmanimOverride = ["a"]
        s.twelveHourClockOverride = true
        try store.save(s)
        XCTAssertEqual(store.load(), s)
        dir.write("widget-settings.json", "{\"linkMode\":\"bogus\",\"theme\":7,\"hideSearches\":true}")
        let lenient = store.load()
        XCTAssertEqual(lenient.linkMode, .openUrl)
        XCTAssertEqual(lenient.theme, .followSystem)
        XCTAssertTrue(lenient.hideSearches)
        dir.write("widget-settings.json", "garbage")
        XCTAssertEqual(store.load(), WidgetSettings())
    }

    func testSettingsStoreRecoversEachKeyOnItsOwn() {
        let dir = TempDir()
        let store = WidgetSettingsStore(directory: dir.url)
        // Wrongly-typed values (and a JSON null for a non-optional key) must not reset the valid ones.
        dir.write("widget-settings.json", """
            {"schemaVersion":"1","dataRootOverride":5,"showYerushalmiDaf":"true","zmanimOverride":"x",
             "twelveHourClockOverride":null,"theme":null,"hideSearches":true,"cityOverride":"חיפה","linkMode":"providerLaunch"}
            """)
        let s = store.load()
        XCTAssertTrue(s.hideSearches)
        XCTAssertEqual(s.cityOverride, "חיפה")
        XCTAssertEqual(s.linkMode, .providerLaunch)
        XCTAssertEqual(s.schemaVersion, 1)
        XCTAssertNil(s.dataRootOverride)
        XCTAssertNil(s.zmanimOverride)
        XCTAssertNil(s.twelveHourClockOverride)
        XCTAssertFalse(s.showYerushalmiDaf)
        XCTAssertEqual(s.theme, .followSystem)
        dir.write("widget-settings.json", "[true]")
        XCTAssertEqual(store.load(), WidgetSettings())
    }

    func testDateParsing() {
        XCTAssertNotNil(DateParsing.parse("2026-01-02T03:04:05.123456"))
        XCTAssertNotNil(DateParsing.parse("2026-01-02T03:04:05"))
        XCTAssertNotNil(DateParsing.parse("2026-01-02T03:04:05.5Z"))
        XCTAssertNotNil(DateParsing.parse("2026-01-02T03:04:05+02:00"))
        XCTAssertEqual(DateParsing.parse("2026-01-02T03:04:05Z"), Date(timeIntervalSince1970: 1_767_323_045))
        XCTAssertNil(DateParsing.parse("garbage"))
        XCTAssertNil(DateParsing.parse(nil))
        XCTAssertNil(DateParsing.parse("  "))
        let local = DateParsing.parse("2026-01-02T03:04:05")!
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = .current
        XCTAssertEqual(cal.component(.hour, from: local), 3)
    }
}
