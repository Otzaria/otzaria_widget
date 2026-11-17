import ImageIO
import SwiftUI
import UniformTypeIdentifiers
import WidgetKit
import XCTest
import OtzariaCore
@testable import OtzariaWidgetsUI

/// Renders every widget in every family and color scheme to PNG (for eyeballing) and checks an image comes out.
@MainActor
final class RenderTests: XCTestCase {
    /// `OTZARIA_WIDGET_RENDER_DIR` keeps the PNGs somewhere to look at; otherwise they go to the temp folder.
    static let outDir = ProcessInfo.processInfo.environment["OTZARIA_WIDGET_RENDER_DIR"].map { URL(fileURLWithPath: $0) }
        ?? FileManager.default.temporaryDirectory.appendingPathComponent("otzaria-widget-renders")

    static let sizes: [(WidgetFamily, String, CGSize)] = [
        (.systemSmall, "small", CGSize(width: 158, height: 158)),
        (.systemMedium, "medium", CGSize(width: 338, height: 158)),
        (.systemLarge, "large", CGSize(width: 338, height: 354)),
    ]

    private func render<V: View>(_ view: V, family: WidgetFamily, size: CGSize, scheme: ColorScheme, name: String) throws {
        let content = view
            .padding(16)
            .frame(width: size.width, height: size.height)
            .background(scheme == .dark ? Color(white: 0.13) : Color.white)
            .environment(\.familyOverride, family)
            .environment(\.plainLinks, true)
            .environment(\.colorScheme, scheme)
        let renderer = ImageRenderer(content: content)
        renderer.scale = 2
        let image: CGImage = try XCTUnwrap(renderer.cgImage, name)
        XCTAssertEqual(image.width, Int(size.width * 2), name)
        try FileManager.default.createDirectory(at: Self.outDir, withIntermediateDirectories: true)
        let url = Self.outDir.appendingPathComponent("\(name).png")
        let dest = try XCTUnwrap(CGImageDestinationCreateWithURL(url as CFURL, UTType.png.identifier as CFString, 1, nil))
        CGImageDestinationAddImage(dest, image, nil)
        XCTAssertTrue(CGImageDestinationFinalize(dest), name)
    }

    private func renderAll<O: Sendable, V: View>(_ kind: String, families: [WidgetFamily], options: O, snapshot: WidgetSnapshot?,
                                                 variant: String = "", settings: WidgetSettings = WidgetSettings(),
                                                 make: (OtzariaEntry<O>) -> V) throws {
        for (family, label, size) in Self.sizes where families.contains(family) {
            for scheme in [ColorScheme.light, .dark] {
                let entry = OtzariaEntry(date: T.now, options: options, snapshot: snapshot, settings: settings)
                let name = "\(kind)_\(label)_\(scheme == .dark ? "dark" : "light")\(variant)"
                try render(make(entry).otzariaLocale(), family: family, size: size, scheme: scheme, name: name)
            }
        }
    }

    private let all: [WidgetFamily] = [.systemSmall, .systemMedium, .systemLarge]
    private let list: [WidgetFamily] = [.systemMedium, .systemLarge]
    private var snap: WidgetSnapshot { Fixtures.snapshot(now: T.now) }

    func testRecentBooks() throws {
        try renderAll("RecentBooks", families: all, options: RecentBooksOptions(showSearches: true), snapshot: snap) { RecentBooksView(entry: $0) }
        try renderAll("RecentBooks", families: all, options: RecentBooksOptions(count: 8), snapshot: snap, variant: "_count8") { RecentBooksView(entry: $0) }
    }

    func testBookmarks() throws {
        try renderAll("Bookmarks", families: list, options: BookmarksOptions(), snapshot: snap) { BookmarksView(entry: $0) }
        try renderAll("Bookmarks", families: list, options: BookmarksOptions(count: 8), snapshot: snap, variant: "_count8") { BookmarksView(entry: $0) }
    }

    func testShamorZachor() throws {
        try renderAll("ShamorZachor", families: all, options: ShamorZachorOptions(), snapshot: snap) { ShamorZachorView(entry: $0) }
        try renderAll("ShamorZachor", families: all, options: ShamorZachorOptions(allColumns: true), snapshot: snap, variant: "_columns") { ShamorZachorView(entry: $0) }
    }

    func testZmanim() throws {
        try renderAll("Zmanim", families: all, options: ZmanimOptions(), snapshot: nil) { ZmanimView(entry: $0) }
    }

    func testHebrewCalendar() throws {
        try renderAll("HebrewCalendar", families: all, options: CalendarOptions(), snapshot: nil) { HebrewCalendarView(entry: $0) }
    }

    func testDashboard() throws {
        try renderAll("Dashboard", families: list, options: DashboardOptions(), snapshot: snap) { DashboardView(entry: $0) }
    }

    func testMissingAndEmptyStates() throws {
        try renderAll("RecentBooks", families: all, options: RecentBooksOptions(), snapshot: nil, variant: "_missing") { RecentBooksView(entry: $0) }
        try renderAll("Bookmarks", families: list, options: BookmarksOptions(), snapshot: nil, variant: "_missing") { BookmarksView(entry: $0) }
        try renderAll("ShamorZachor", families: all, options: ShamorZachorOptions(filter: .completed), snapshot: {
            var s = snap; s.shamorZachor = []; return s
        }(), variant: "_empty") { ShamorZachorView(entry: $0) }
        try renderAll("Dashboard", families: list, options: DashboardOptions(sections: [.reading, .progress]), snapshot: nil, variant: "_missing") { DashboardView(entry: $0) }
    }
}
