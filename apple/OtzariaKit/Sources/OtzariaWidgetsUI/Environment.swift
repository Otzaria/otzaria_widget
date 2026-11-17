import Foundation
import OtzariaCore

/// Widget kind ids, identical to the Windows definition ids.
public enum WidgetKinds {
    public static let recentBooks = "RecentBooks"
    public static let bookmarks = "Bookmarks"
    public static let shamorZachor = "ShamorZachor"
    public static let zmanim = "Zmanim"
    public static let hebrewCalendar = "HebrewCalendar"
    public static let dashboard = "Dashboard"

    public static let all = [recentBooks, bookmarks, shamorZachor, zmanim, hebrewCalendar, dashboard]

    /// Hebrew display names (same strings as the Windows manifest).
    public static func displayName(for kind: String) -> String {
        switch kind {
        case recentBooks: return "אוצריא · ספרים אחרונים"
        case bookmarks: return "אוצריא · סימניות"
        case shamorZachor: return "שמור וזכור"
        case zmanim: return "זמני היום"
        case hebrewCalendar: return "לוח עברי"
        case dashboard: return "אוצריא · לוח אישי"
        default: return "אוצריא"
        }
    }
}

/// Locates the shared App Group container and loads what the host app wrote there.
public enum WidgetEnvironment {
    /// Info.plist key (macOS extension) holding the team-prefixed group id, injected by the project.
    public static let appGroupInfoKey = "OtzariaAppGroup"

    /// Tests and previews point this at a temporary directory.
    nonisolated(unsafe) static var directoryOverride: URL?

    /// iOS: `group.org.otzaria.widget`; macOS: the group id from the extension's Info.plist (nil when missing or unexpanded).
    public static var appGroupDirectory: URL? {
        if let directoryOverride { return directoryOverride }
        #if os(macOS)
        guard let id = Bundle.main.object(forInfoDictionaryKey: appGroupInfoKey) as? String,
              !id.isEmpty, !id.contains("$(") else { return nil }
        return WidgetSnapshotStore.appGroupDirectory(groupID: id)
        #else
        return WidgetSnapshotStore.appGroupDirectory(groupID: WidgetSnapshotStore.iOSAppGroupID)
        #endif
    }

    static func loadSnapshot() -> WidgetSnapshot? {
        appGroupDirectory.flatMap { WidgetSnapshotStore(directory: $0).read() }
    }

    static func loadSettings() -> WidgetSettings {
        appGroupDirectory.map { WidgetSettingsStore(directory: $0).load() } ?? WidgetSettings()
    }
}

/// Deep links the widgets use besides the per-entry ones.
enum WidgetLinks {
    static let library = "otzaria://open/library"
    static let history = "otzaria://open/history"
    static let bookmarks = "otzaria://open/bookmarks"
    static let calendar = "otzaria://open/calendar"
    static let shamorZachor = "otzaria://open/shamor_zachor"
    static let settings = "otzaria-widget://settings"

    /// Links from the snapshot are used only when they pass the otzaria:// whitelist.
    static func url(_ text: String?, fallback: String = library) -> URL {
        if let text, text == settings || OtzariaLinks.isSafeOtzariaUri(text), let url = URL(string: text) { return url }
        return URL(string: fallback)!
    }
}
