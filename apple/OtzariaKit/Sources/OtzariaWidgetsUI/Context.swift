import Foundation
import OtzariaCalendar
import OtzariaCore
import WidgetKit

enum WidgetSize: Equatable {
    case small, medium, large

    init(_ family: WidgetFamily) {
        switch family {
        case .systemSmall: self = .small
        case .systemMedium: self = .medium
        default: self = .large
        }
    }

    /// Upper bound of list items: S 2, M 4, L 8.
    var maxRows: Int {
        switch self {
        case .small: return 2
        case .medium: return 4
        case .large: return 8
        }
    }

    /// 0 = automatic: use `defaultRows`, then clamp to the size maximum.
    func rows(count: Int, defaultRows: Int) -> Int {
        min(max(count > 0 ? count : defaultRows, 1), maxRows)
    }
}

enum ContentState: Equatable {
    case ok
    case empty(String)
    case missing(String)
}

enum DataText {
    static let missing = "לא נמצאו נתוני אוצריא — פתח את ההגדרות"
    static let iosNoSnapshot = "נתוני אוצריא יופיעו כאן לאחר שאוצריא תשתף אותם עם הווידג'ט. ווידג'טי הלוח העברי והזמנים פועלים גם בלעדיה."
    static let libraryPartial = "ספריית אוצריא לא זמינה — שמות הספרים עשויים להיות חלקיים"

    static var noSnapshot: String {
        #if os(iOS)
        return iosNoSnapshot
        #else
        return missing
        #endif
    }

    /// Hebrew message when the data the widget depends on is unavailable, nil when it is fine.
    static func missingReason(_ snapshot: WidgetSnapshot?, sources: [String]) -> String? {
        guard let snapshot else { return noSnapshot }
        if !snapshot.sources.isEmpty {
            if let bad = snapshot.sources.first(where: { $0.health != .ok && sources.contains($0.name) }) {
                return nonBlank(bad.message) ?? missing
            }
            return nil
        }
        return snapshot.health == .ok ? nil : (nonBlank(snapshot.healthMessage) ?? missing)
    }

    private static func nonBlank(_ s: String?) -> String? {
        guard let s, !s.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return nil }
        return s
    }
}

/// Everything a view model needs, so models stay pure functions.
struct WidgetContext {
    var snapshot: WidgetSnapshot?
    var settings: WidgetSettings
    var now: Date
    var size: WidgetSize

    static let calendarService = CalendarService()

    var twelveHour: Bool { settings.twelveHourClockOverride ?? snapshot?.preferences.twelveHourClock ?? false }

    var dayTransition: String {
        settings.dayTransitionOverride.flatMap(nonBlank) ?? snapshot?.preferences.dayTransition ?? CalendarService.dayTransitionSunset
    }

    var hideSearches: Bool { settings.hideSearches }

    /// Widget city, else the settings override, else Otzaria's, else Jerusalem.
    func city(widgetCity: String?) -> CityInfo {
        let cal = Self.calendarService
        let names = [widgetCity, settings.cityOverride, snapshot?.preferences.selectedCity, "ירושלים"]
        for name in names {
            if let name, let n = nonBlank(name), let city = cal.findCity(n) { return city }
        }
        return cal.cities[0]
    }

    /// Custom list wins, then the settings override (even empty), Otzaria's list, then the defaults.
    func zmanimIds(custom: [String]) -> [String] {
        let cal = Self.calendarService
        if !custom.isEmpty { return custom }
        if let o = settings.zmanimOverride { return o }
        if let p = snapshot?.preferences.enabledZmanim, !p.isEmpty { return p }
        return cal.defaultEnabledZmanim
    }
}

func nonBlank(_ s: String) -> String? { s.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? nil : s }
