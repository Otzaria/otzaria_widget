import SwiftUI
import WidgetKit

// Stub widgets; replaced by the real implementation.
private struct StubEntry: TimelineEntry { let date: Date }

private struct StubProvider: TimelineProvider {
    func placeholder(in context: Context) -> StubEntry { StubEntry(date: .now) }
    func getSnapshot(in context: Context, completion: @escaping (StubEntry) -> Void) { completion(StubEntry(date: .now)) }
    func getTimeline(in context: Context, completion: @escaping (Timeline<StubEntry>) -> Void) {
        completion(Timeline(entries: [StubEntry(date: .now)], policy: .never))
    }
}

private func stub(_ kind: String) -> some WidgetConfiguration {
    StaticConfiguration(kind: kind, provider: StubProvider()) { _ in
        Text(kind).containerBackground(.background, for: .widget)
    }
}

public struct RecentBooksWidget: Widget { public init() {}; public var body: some WidgetConfiguration { stub("RecentBooks") } }
public struct BookmarksWidget: Widget { public init() {}; public var body: some WidgetConfiguration { stub("Bookmarks") } }
public struct ShamorZachorWidget: Widget { public init() {}; public var body: some WidgetConfiguration { stub("ShamorZachor") } }
public struct ZmanimWidget: Widget { public init() {}; public var body: some WidgetConfiguration { stub("Zmanim") } }
public struct HebrewCalendarWidget: Widget { public init() {}; public var body: some WidgetConfiguration { stub("HebrewCalendar") } }
public struct DashboardWidget: Widget { public init() {}; public var body: some WidgetConfiguration { stub("Dashboard") } }
