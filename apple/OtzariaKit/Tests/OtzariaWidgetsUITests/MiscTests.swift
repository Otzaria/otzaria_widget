import AppIntents
import XCTest
import OtzariaCore
@testable import OtzariaWidgetsUI

final class MiscTests: XCTestCase {
    func testKindsMatchWindowsIds() {
        XCTAssertEqual(WidgetKinds.all, ["RecentBooks", "Bookmarks", "ShamorZachor", "Zmanim", "HebrewCalendar", "Dashboard"])
        XCTAssertEqual(WidgetKinds.displayName(for: WidgetKinds.shamorZachor), "שמור וזכור")
        XCTAssertEqual(WidgetKinds.displayName(for: WidgetKinds.dashboard), "אוצריא · לוח אישי")
    }

    func testPublicWidgetTypesInstantiate() {
        _ = RecentBooksWidget().body
        _ = BookmarksWidget().body
        _ = ShamorZachorWidget().body
        _ = ZmanimWidget().body
        _ = HebrewCalendarWidget().body
        _ = DashboardWidget().body
    }

    func testFormatHelpers() {
        XCTAssertEqual(HebrewFormat.percent(0.125), "13%")
        XCTAssertEqual(HebrewFormat.percent(2), "100%")
        XCTAssertEqual(HebrewFormat.percent(.nan), "0%")
        XCTAssertEqual(HebrewFormat.stripTitle("ברכות, ב.", "ברכות"), "ב.")
        XCTAssertEqual(HebrewFormat.stripTitle("ברכות", "ברכות"), "ברכות")
        XCTAssertEqual(HebrewFormat.stripTitle("סימן א", "ברכות"), "סימן א")
        XCTAssertEqual(HebrewFormat.clip("אבג דהו זחט", 8), "אבג דהו…")
        XCTAssertEqual(HebrewFormat.clip("קצר", 8), "קצר")
        XCTAssertEqual(HebrewFormat.stripBidi("\u{2066}12:30\u{2069}"), "12:30")
        XCTAssertEqual(HebrewFormat.gregorianLong(year: 2026, month: 11, day: 3), "3 בנובמבר 2026")
        XCTAssertEqual(HebrewFormat.gregorianShort(month: 11, day: 3), "3 נוב׳")
    }

    func testRelativeDay() {
        let now = T.now
        XCTAssertEqual(HebrewFormat.relativeDay(now, now: now), "היום")
        XCTAssertEqual(HebrewFormat.relativeDay(now.addingTimeInterval(-86_400), now: now), "אתמול")
        XCTAssertEqual(HebrewFormat.relativeDay(now.addingTimeInterval(-3 * 86_400), now: now), "לפני 3 ימים")
        XCTAssertFalse(HebrewFormat.relativeDay(now.addingTimeInterval(-30 * 86_400), now: now).contains("לפני"))
        XCTAssertEqual(HebrewFormat.relativeDay(now.addingTimeInterval(86_400), now: now), "")
        XCTAssertEqual(HebrewFormat.relativeDay(nil, now: now), "")
    }

    func testAccentParsing() {
        XCTAssertNotNil(Accent.parse(hex: "#112233"))
        XCTAssertNil(Accent.parse(hex: "zzz"))
        XCTAssertNil(Accent.parse(hex: "#12"))
    }

    func testIntentOptionMapping() {
        let recent = RecentBooksIntent()
        XCTAssertEqual(recent.options, RecentBooksOptions())
        recent.count = 5; recent.showSearches = true; recent.includePdfs = false
        XCTAssertEqual(recent.options, RecentBooksOptions(count: 5, showLocation: true, includePdfs: false, showSearches: true))

        let marks = BookmarksIntent()
        XCTAssertEqual(marks.options.group, BookmarksOptions.allGroups)
        marks.group = BookmarkGroupEntity(id: "g1", name: "הלכה")
        XCTAssertEqual(marks.options.group, "g1")

        let sz = ShamorZachorIntent()
        XCTAssertEqual(sz.options.filter, .inProgress)
        sz.filter = .completed; sz.allColumns = true
        XCTAssertEqual(sz.options, ShamorZachorOptions(count: 0, filter: .completed, allColumns: true))

        let zm = ZmanimIntent()
        XCTAssertEqual(zm.options, ZmanimOptions())
        zm.city = CityEntity(id: "חיפה"); zm.zmanimSet = .core
        XCTAssertEqual(zm.options.city, "חיפה")
        XCTAssertEqual(zm.options.customIds, ZmanimPreset.core)
        zm.zmanimSet = .otzariaDefault
        XCTAssertFalse(zm.options.customIds.isEmpty)

        let cal = HebrewCalendarIntent()
        XCTAssertNil(cal.options.showYerushalmi)
        cal.yerushalmi = .show
        XCTAssertEqual(cal.options.showYerushalmi, true)
        cal.yerushalmi = .hide
        XCTAssertEqual(cal.options.showYerushalmi, false)

        let dash = DashboardIntent()
        XCTAssertEqual(dash.options.sections, DashboardSection.allCases)
        dash.posDate = 0; dash.posBookmarks = 1
        XCTAssertEqual(dash.options.sections, [.bookmarks, .zman, .reading, .progress])
    }

    func testSnapshotLinksMustPassTheWhitelist() {
        XCTAssertEqual(WidgetLinks.url("otzaria://open/book/12?index=3").absoluteString, "otzaria://open/book/12?index=3")
        XCTAssertEqual(WidgetLinks.url(WidgetLinks.settings).absoluteString, WidgetLinks.settings)
        for bad in ["https://example.com", "file:///etc/passwd", "otzaria://plugin/install", "otzaria://open/../x", ""] {
            XCTAssertEqual(WidgetLinks.url(bad).absoluteString, WidgetLinks.library, bad)
        }
        XCTAssertEqual(WidgetLinks.url("https://example.com", fallback: WidgetLinks.shamorZachor).absoluteString, WidgetLinks.shamorZachor)
    }

    func testDynamicOptionEntities() async throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("wui-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: dir); WidgetEnvironment.directoryOverride = nil }
        WidgetEnvironment.directoryOverride = dir
        try WidgetSnapshotStore(directory: dir).write(Fixtures.snapshot(now: T.now))
        let groups = try await BookmarkGroupQuery().suggestedEntities()
        XCTAssertEqual(groups.map(\.id), ["all", "none", "g1"])
        let found = try await BookmarkGroupQuery().entities(for: ["g1", "zzz"])
        XCTAssertEqual(found.map(\.name), ["הלכה"])
        let cities = try await CityQuery().suggestedEntities()
        XCTAssertTrue(cities.contains { $0.id == "ירושלים" })
        let matched = try await CityQuery().entities(matching: "תל")
        XCTAssertTrue(matched.contains { $0.id == "תל אביב" })
    }

    func testEnvironmentLoadsSnapshotAndSettings() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("wui-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: dir); WidgetEnvironment.directoryOverride = nil }
        WidgetEnvironment.directoryOverride = nil
        XCTAssertNil(WidgetEnvironment.appGroupDirectory) // no Info.plist key under the test runner
        XCTAssertNil(WidgetEnvironment.loadSnapshot())
        WidgetEnvironment.directoryOverride = dir
        XCTAssertNil(WidgetEnvironment.loadSnapshot())
        XCTAssertEqual(WidgetEnvironment.loadSettings(), WidgetSettings())
        try WidgetSnapshotStore(directory: dir).write(Fixtures.snapshot(now: T.now))
        var settings = WidgetSettings()
        settings.cityOverride = "חיפה"
        try WidgetSettingsStore(directory: dir).save(settings)
        XCTAssertEqual(WidgetEnvironment.loadSnapshot()?.recentBooks.count, 7)
        XCTAssertEqual(WidgetEnvironment.loadSettings().cityOverride, "חיפה")
        XCTAssertEqual(WidgetEnvironment.appGroupDirectory, dir)
    }
}
