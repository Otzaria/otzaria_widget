import AppIntents
import SwiftUI
import WidgetKit

private let allFamilies: [WidgetFamily] = [.systemSmall, .systemMedium, .systemLarge]
private let listFamilies: [WidgetFamily] = [.systemMedium, .systemLarge]

public struct RecentBooksWidget: Widget {
    public init() {}
    public var body: some WidgetConfiguration {
        AppIntentConfiguration(kind: WidgetKinds.recentBooks, intent: RecentBooksIntent.self, provider: OtzariaProvider<RecentBooksIntent>()) {
            RecentBooksView(entry: $0).containerBackground(.background, for: .widget)
        }
        .configurationDisplayName(WidgetKinds.displayName(for: WidgetKinds.recentBooks))
        .description("המשך קריאה והספרים האחרונים שפתחת באוצריא")
        .supportedFamilies(allFamilies)
    }
}

public struct BookmarksWidget: Widget {
    public init() {}
    public var body: some WidgetConfiguration {
        AppIntentConfiguration(kind: WidgetKinds.bookmarks, intent: BookmarksIntent.self, provider: OtzariaProvider<BookmarksIntent>()) {
            BookmarksView(entry: $0).containerBackground(.background, for: .widget)
        }
        .configurationDisplayName(WidgetKinds.displayName(for: WidgetKinds.bookmarks))
        .description("הסימניות שלך באוצריא, לפי קבוצה")
        .supportedFamilies(listFamilies)
    }
}

public struct ShamorZachorWidget: Widget {
    public init() {}
    public var body: some WidgetConfiguration {
        AppIntentConfiguration(kind: WidgetKinds.shamorZachor, intent: ShamorZachorIntent.self, provider: OtzariaProvider<ShamorZachorIntent>()) {
            ShamorZachorView(entry: $0).containerBackground(.background, for: .widget)
        }
        .configurationDisplayName(WidgetKinds.displayName(for: WidgetKinds.shamorZachor))
        .description("התקדמות הלימוד בספרי שמור וזכור")
        .supportedFamilies(allFamilies)
    }
}

public struct ZmanimWidget: Widget {
    public init() {}
    public var body: some WidgetConfiguration {
        AppIntentConfiguration(kind: WidgetKinds.zmanim, intent: ZmanimIntent.self, provider: OtzariaProvider<ZmanimIntent>()) {
            ZmanimView(entry: $0).containerBackground(.background, for: .widget)
        }
        .configurationDisplayName(WidgetKinds.displayName(for: WidgetKinds.zmanim))
        .description("זמני היום, הזמן הבא וספירה לאחור")
        .supportedFamilies(allFamilies)
    }
}

public struct HebrewCalendarWidget: Widget {
    public init() {}
    public var body: some WidgetConfiguration {
        AppIntentConfiguration(kind: WidgetKinds.hebrewCalendar, intent: HebrewCalendarIntent.self, provider: OtzariaProvider<HebrewCalendarIntent>()) {
            HebrewCalendarView(entry: $0).containerBackground(.background, for: .widget)
        }
        .configurationDisplayName(WidgetKinds.displayName(for: WidgetKinds.hebrewCalendar))
        .description("התאריך העברי והלועזי, חגים, פרשה ודף יומי")
        .supportedFamilies(allFamilies)
    }
}

public struct DashboardWidget: Widget {
    public init() {}
    public var body: some WidgetConfiguration {
        AppIntentConfiguration(kind: WidgetKinds.dashboard, intent: DashboardIntent.self, provider: OtzariaProvider<DashboardIntent>()) {
            DashboardView(entry: $0).containerBackground(.background, for: .widget)
        }
        .configurationDisplayName(WidgetKinds.displayName(for: WidgetKinds.dashboard))
        .description("לוח משולב: תאריך, הזמן הבא, המשך קריאה, שמור וזכור וסימניות")
        .supportedFamilies(listFamilies)
    }
}
