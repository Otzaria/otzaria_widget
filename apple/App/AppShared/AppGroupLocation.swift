import Foundation
import OtzariaCore

/// Where the app and the widget extension exchange files (the App Group container).
struct AppGroupLocation {
    let directory: URL
    /// False when the widgets cannot share the files: no container (iOS without the entitlement, files stay private to
    /// the app), or on macOS a build that is not signed by a developer team (ad-hoc or unsigned).
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
            #if os(macOS)
            // macOS hands out the container even without a real entitlement: keep using it, but say it may not be shared.
            let signed = WidgetSnapshotStore.isTeamSignedGroup(groupID, signingTeamID: WidgetSnapshotStore.currentSigningTeamID())
            return AppGroupLocation(directory: shared, isShared: signed)
            #else
            return AppGroupLocation(directory: shared, isShared: true)
            #endif
        }
        let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return AppGroupLocation(directory: support.appendingPathComponent("OtzariaWidget", isDirectory: true), isShared: false)
    }
}
