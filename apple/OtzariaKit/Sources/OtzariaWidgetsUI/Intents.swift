import AppIntents
import Foundation
import OtzariaCore
import WidgetKit

/// A widget intent that maps to the pure customization values of its widget.
protocol OptionsIntent: WidgetConfigurationIntent {
    associatedtype Options: Sendable
    var options: Options { get }
    static var defaultOptions: Options { get }
    /// Extra timeline moments after `ctx.now` (calendar widgets); data widgets refresh on a fixed interval instead.
    static func refreshDates(_ ctx: WidgetContext, _ options: Options) -> [Date]
}

extension OptionsIntent {
    static func refreshDates(_ ctx: WidgetContext, _ options: Options) -> [Date] { [] }
}

// MARK: Entities

struct BookmarkGroupEntity: AppEntity {
    static let typeDisplayRepresentation: TypeDisplayRepresentation = "קבוצת סימניות"
    static let defaultQuery = BookmarkGroupQuery()

    var id: String
    var name: String

    var displayRepresentation: DisplayRepresentation { DisplayRepresentation(title: "\(name)") }

    static let all = BookmarkGroupEntity(id: BookmarksOptions.allGroups, name: "כל הסימניות")
    static let none = BookmarkGroupEntity(id: BookmarksOptions.noGroup, name: "ללא קבוצה")
}

struct BookmarkGroupQuery: EntityQuery {
    func suggestedEntities() async throws -> [BookmarkGroupEntity] {
        let groups = (WidgetEnvironment.loadSnapshot()?.bookmarkGroups ?? [])
            .filter { nonBlank($0.name) != nil }
            .map { BookmarkGroupEntity(id: $0.id, name: $0.name) }
        return [.all, .none] + groups
    }

    func entities(for identifiers: [String]) async throws -> [BookmarkGroupEntity] {
        let all = try await suggestedEntities()
        return identifiers.compactMap { id in all.first { $0.id == id } }
    }

    func defaultResult() async -> BookmarkGroupEntity? { .all }
}

struct CityEntity: AppEntity {
    static let typeDisplayRepresentation: TypeDisplayRepresentation = "עיר"
    static let defaultQuery = CityQuery()

    var id: String

    var displayRepresentation: DisplayRepresentation { DisplayRepresentation(title: "\(id)") }
}

struct CityQuery: EntityStringQuery {
    func suggestedEntities() async throws -> [CityEntity] {
        WidgetContext.calendarService.cities.map { CityEntity(id: $0.name) }
    }

    func entities(for identifiers: [String]) async throws -> [CityEntity] {
        identifiers.compactMap { WidgetContext.calendarService.findCity($0) }.map { CityEntity(id: $0.name) }
    }

    func entities(matching string: String) async throws -> [CityEntity] {
        let q = string.trimmingCharacters(in: .whitespacesAndNewlines)
        return WidgetContext.calendarService.cities.filter { q.isEmpty || $0.name.contains(q) }.map { CityEntity(id: $0.name) }
    }
}

// MARK: Enums

enum ShamorZachorFilterChoice: String, AppEnum {
    case inProgress, completed, all

    static let typeDisplayRepresentation: TypeDisplayRepresentation = "הצג"
    static let caseDisplayRepresentations: [ShamorZachorFilterChoice: DisplayRepresentation] = [
        .inProgress: "ספרים בתהליך", .completed: "ספרים שהושלמו", .all: "הכל",
    ]

    var filter: ShamorZachorFilter {
        switch self {
        case .inProgress: return .inProgress
        case .completed: return .completed
        case .all: return .all
        }
    }
}

enum ZmanimSetChoice: String, AppEnum {
    case follow, core, otzariaDefault

    static let typeDisplayRepresentation: TypeDisplayRepresentation = "קבוצת זמנים"
    static let caseDisplayRepresentations: [ZmanimSetChoice: DisplayRepresentation] = [
        .follow: "כמו באוצריא", .core: "עיקריים", .otzariaDefault: "ברירת המחדל של אוצריא",
    ]
}

/// Tri-state like the Windows card: until chosen, the widget follows the global setting.
enum YerushalmiChoice: String, AppEnum {
    case follow, show, hide

    static let typeDisplayRepresentation: TypeDisplayRepresentation = "דף יומי ירושלמי"
    static let caseDisplayRepresentations: [YerushalmiChoice: DisplayRepresentation] = [
        .follow: "לפי ההגדרות הכלליות", .show: "הצג", .hide: "הסתר",
    ]

    var value: Bool? {
        switch self {
        case .follow: return nil
        case .show: return true
        case .hide: return false
        }
    }
}

// MARK: Intents

struct RecentBooksIntent: OptionsIntent {
    static let title: LocalizedStringResource = "ספרים אחרונים"
    static let description = IntentDescription("התאמה אישית של ווידג'ט הספרים האחרונים")

    @Parameter(title: "מספר פריטים (0 = אוטומטי)", default: 0, inclusiveRange: (0, 8)) var count: Int
    @Parameter(title: "הצג את המיקום בספר", default: true) var showLocation: Bool
    @Parameter(title: "כלול ספרי PDF", default: true) var includePdfs: Bool
    @Parameter(title: "הצג חיפושים אחרונים", default: false) var showSearches: Bool

    var options: RecentBooksOptions {
        RecentBooksOptions(count: count, showLocation: showLocation, includePdfs: includePdfs, showSearches: showSearches)
    }
    static var defaultOptions: RecentBooksOptions { RecentBooksOptions() }
}

struct BookmarksIntent: OptionsIntent {
    static let title: LocalizedStringResource = "סימניות"
    static let description = IntentDescription("התאמה אישית של ווידג'ט הסימניות")

    @Parameter(title: "קבוצה") var group: BookmarkGroupEntity?
    @Parameter(title: "מספר פריטים (0 = אוטומטי)", default: 0, inclusiveRange: (0, 8)) var count: Int

    var options: BookmarksOptions { BookmarksOptions(count: count, group: group?.id ?? BookmarksOptions.allGroups) }
    static var defaultOptions: BookmarksOptions { BookmarksOptions() }
}

struct ShamorZachorIntent: OptionsIntent {
    static let title: LocalizedStringResource = "שמור וזכור"
    static let description = IntentDescription("התאמה אישית של ווידג'ט שמור וזכור")

    @Parameter(title: "הצג", default: .inProgress) var filter: ShamorZachorFilterChoice
    @Parameter(title: "מספר פריטים (0 = אוטומטי)", default: 0, inclusiveRange: (0, 8)) var count: Int
    @Parameter(title: "הצג את כל העמודות (חזרות) בווידג'ט הגדול", default: false) var allColumns: Bool

    var options: ShamorZachorOptions { ShamorZachorOptions(count: count, filter: filter.filter, allColumns: allColumns) }
    static var defaultOptions: ShamorZachorOptions { ShamorZachorOptions() }
}

struct ZmanimIntent: OptionsIntent {
    static let title: LocalizedStringResource = "זמני היום"
    static let description = IntentDescription("התאמה אישית של ווידג'ט זמני היום")

    @Parameter(title: "עיר (ריק = לפי הגדרות אוצריא)") var city: CityEntity?
    @Parameter(title: "קבוצת זמנים", default: .follow) var zmanimSet: ZmanimSetChoice

    var options: ZmanimOptions {
        let ids: [String]
        switch zmanimSet {
        case .follow: ids = []
        case .core: ids = ZmanimPreset.core
        case .otzariaDefault: ids = WidgetContext.calendarService.defaultEnabledZmanim
        }
        return ZmanimOptions(city: city?.id, customIds: ids)
    }
    static var defaultOptions: ZmanimOptions { ZmanimOptions() }

    static func refreshDates(_ ctx: WidgetContext, _ options: ZmanimOptions) -> [Date] { ZmanimModel.refreshDates(ctx, options) }
}

struct HebrewCalendarIntent: OptionsIntent {
    static let title: LocalizedStringResource = "לוח עברי"
    static let description = IntentDescription("התאמה אישית של הלוח העברי")

    @Parameter(title: "הצג דף יומי (בבלי)", default: true) var showDaf: Bool
    @Parameter(title: "הצג דף יומי ירושלמי", default: .follow) var yerushalmi: YerushalmiChoice
    @Parameter(title: "הצג ספירת העומר", default: true) var showOmer: Bool
    @Parameter(title: "הצג הדלקת נרות והבדלה", default: true) var showCandle: Bool

    var options: CalendarOptions {
        CalendarOptions(showDaf: showDaf, showYerushalmi: yerushalmi.value, showOmer: showOmer, showCandle: showCandle)
    }
    static var defaultOptions: CalendarOptions { CalendarOptions() }

    static func refreshDates(_ ctx: WidgetContext, _ options: CalendarOptions) -> [Date] { HebrewCalendarModel.refreshDates(ctx) }
}

struct DashboardIntent: OptionsIntent {
    static let title: LocalizedStringResource = "לוח אישי"
    static let description = IntentDescription("בחירת החלקים והסדר שלהם בלוח האישי")

    @Parameter(title: "התאריך היום — מיקום (0 = מוסתר)", default: 1, inclusiveRange: (0, 5)) var posDate: Int
    @Parameter(title: "הזמן הבא — מיקום (0 = מוסתר)", default: 2, inclusiveRange: (0, 5)) var posZman: Int
    @Parameter(title: "המשך קריאה — מיקום (0 = מוסתר)", default: 3, inclusiveRange: (0, 5)) var posReading: Int
    @Parameter(title: "שמור וזכור — מיקום (0 = מוסתר)", default: 4, inclusiveRange: (0, 5)) var posProgress: Int
    @Parameter(title: "סימניות — מיקום (0 = מוסתר)", default: 5, inclusiveRange: (0, 5)) var posBookmarks: Int

    var options: DashboardOptions {
        DashboardOptions(sections: DashboardOptions.sections(fromPositions: [
            .date: posDate, .zman: posZman, .reading: posReading, .progress: posProgress, .bookmarks: posBookmarks,
        ]))
    }
    static var defaultOptions: DashboardOptions { DashboardOptions() }

    static func refreshDates(_ ctx: WidgetContext, _ options: DashboardOptions) -> [Date] { DashboardModel.refreshDates(ctx, options) }
}
