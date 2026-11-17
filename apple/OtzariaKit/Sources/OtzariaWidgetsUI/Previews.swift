import SwiftUI
import WidgetKit
import OtzariaCore

private func sample<O: Sendable>(_ options: O, snapshot: WidgetSnapshot? = Fixtures.snapshot(now: .now)) -> OtzariaEntry<O> {
    OtzariaEntry(date: .now, options: options, snapshot: snapshot, settings: WidgetSettings())
}

#Preview("ספרים אחרונים", as: .systemMedium) { RecentBooksWidget() } timeline: { sample(RecentBooksOptions(showSearches: true)) }
#Preview("סימניות", as: .systemLarge) { BookmarksWidget() } timeline: { sample(BookmarksOptions()) }
#Preview("שמור וזכור", as: .systemSmall) { ShamorZachorWidget() } timeline: { sample(ShamorZachorOptions()) }
#Preview("זמני היום", as: .systemMedium) { ZmanimWidget() } timeline: { sample(ZmanimOptions(), snapshot: nil) }
#Preview("לוח עברי", as: .systemSmall) { HebrewCalendarWidget() } timeline: { sample(CalendarOptions(), snapshot: nil) }
#Preview("לוח אישי", as: .systemLarge) { DashboardWidget() } timeline: { sample(DashboardOptions()) }
#Preview("ספרים אחרונים · ללא נתונים", as: .systemMedium) { RecentBooksWidget() } timeline: { sample(RecentBooksOptions(), snapshot: nil) }
