import XCTest
@testable import OtzariaCore

/// Reads the real Otzaria data of the current user (read-only) and prints counts only. Skipped when absent.
final class RealDataSmokeTests: XCTestCase {
    func testSnapshotFromRealOtzariaData() throws {
        let root = NSHomeDirectory() + "/Library/Application Support/otzaria"
        try XCTSkipUnless(FileManager.default.fileExists(atPath: root + "/app_preferences.hive"), "no real Otzaria data on this machine")

        let service = OtzariaDataService()
        let s = service.reload()
        XCTAssertEqual(s.paths.dataRoot, root)
        XCTAssertNotNil(s.paths.preferencesHive)
        let statuses = s.status.map { "\($0.name)=\($0.health.rawValue)" }.joined(separator: " ")
        print("real-data: sources: \(statuses)")
        print("real-data: paths: userState=\(s.paths.userStateDb != nil) library=\(s.paths.libraryDb != nil) app=\(s.paths.otzariaApp != nil)")
        print("real-data: recent=\(s.recentBooks.count) searches=\(s.recentSearches.count) bookmarks=\(s.bookmarks.count) "
              + "groups=\(s.bookmarkGroups.count) workspaces=\(s.workspaces.count) tabs=\(s.openTabs.count) progress=\(s.progress.count)")

        XCTAssertEqual(s.status.first { $0.name == SourceNames.preferences }?.health, .ok)
        // A found but unreadable user_state.db is a failure, not something to print and pass.
        if s.paths.userStateDb != nil {
            XCTAssertEqual(s.status.first { $0.name == SourceNames.userState }?.health, .ok)
        }
        XCTAssertFalse(s.preferences.selectedCity.isEmpty)
        for entry in s.recentBooks + s.bookmarks {
            XCTAssertFalse(entry.book.title.isEmpty)
            if let link = entry.deepLink { XCTAssertTrue(OtzariaLinks.isSafeOtzariaUri(link)) }
        }
        XCTAssertLessThanOrEqual(s.recentBooks.count, OtzariaDataService.maxRecentBooks)
        XCTAssertEqual(Set(s.recentBooks.map { $0.book.title + "\($0.book.id ?? -1)\($0.book.type)" }).count, s.recentBooks.count)
        for p in s.progress {
            XCTAssertTrue((0...1).contains(p.headline))
            XCTAssertFalse(p.statusText.isEmpty)
        }

        let widget = service.widgetSnapshot()
        let encoded = try WidgetSnapshotStore.makeEncoder().encode(widget)
        let decoded = try WidgetSnapshotStore.makeDecoder().decode(WidgetSnapshot.self, from: encoded)
        XCTAssertEqual(decoded.recentBooks.count, widget.recentBooks.count)
        print("real-data: widget snapshot json bytes=\(encoded.count) health=\(widget.health.rawValue) currentTab=\(widget.currentTab != nil)")
    }
}
