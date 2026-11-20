import Foundation
import OtzariaCore

/// Where the app and the widget extension exchange files (the App Group container).
struct AppGroupLocation {
    let directory: URL
    /// False when the App Group container is unavailable (an unsigned build): files stay private to the app.
    let isShared: Bool

    static var groupID: String {
        Bundle.main.object(forInfoDictionaryKey: "OtzariaAppGroup") as? String ?? WidgetSnapshotStore.iOSAppGroupID
    }

    static func resolve() -> AppGroupLocation {
        #if DEBUG
        if let path = ProcessInfo.processInfo.environment["OTZARIA_WIDGET_GROUP_DIR"], !path.isEmpty {
            return AppGroupLocation(directory: URL(fileURLWithPath: path, isDirectory: true), isShared: true)
        }
        #endif
        if let shared = WidgetSnapshotStore.appGroupDirectory(groupID: groupID) {
            return AppGroupLocation(directory: shared, isShared: true)
        }
        let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return AppGroupLocation(directory: support.appendingPathComponent("OtzariaWidget", isDirectory: true), isShared: false)
    }
}
