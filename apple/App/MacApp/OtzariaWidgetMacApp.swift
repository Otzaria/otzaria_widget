import SwiftUI
import CoreServices
import OtzariaCore

@main
struct OtzariaWidgetMacApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    @StateObject private var model: SettingsModel
    @StateObject private var agent: SnapshotAgent
    @StateObject private var router = SettingsRouter()

    init() {
        let model = SettingsModel(location: AppGroupLocation.resolve())
        _model = StateObject(wrappedValue: model)
        let agent = SnapshotAgent(model: model)
        agent.start()
        _agent = StateObject(wrappedValue: agent)
    }

    var body: some Scene {
        Window("ווידג'ט אוצריא", id: SettingsRouter.windowID) {
            MacSettingsView(model: model, agent: agent, router: router)
                .hebrewRTL()
                .frame(minWidth: 760, minHeight: 520)
                .onOpenURL { router.handle($0) }
        }
        .defaultSize(width: 900, height: 640)

        MenuBarExtra("אוצריא", systemImage: "books.vertical") {
            MenuBarContent(model: model, agent: agent, router: router).hebrewRTL()
        }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        // "פתח הגדרות" shows a Dock icon (.regular); once the last window closes the app is menu-bar only again.
        NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification, object: nil, queue: .main) { note in
            guard let closing = note.object as? NSWindow, closing.styleMask.contains(.titled) else { return }
            DispatchQueue.main.async {
                MainActor.assumeIsolated {
                    let open = NSApp.windows.contains { $0 !== closing && $0.isVisible && $0.styleMask.contains(.titled) }
                    if !open { NSApp.setActivationPolicy(.accessory) }
                }
            }
        }
        guard Self.launchedAtLogin else { return }
        // Started by the login item: stay in the menu bar without a window.
        NSApp.setActivationPolicy(.accessory)
        DispatchQueue.main.async { NSApp.windows.forEach { $0.orderOut(nil) } }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }

    private static var launchedAtLogin: Bool {
        guard let event = NSAppleEventManager.shared().currentAppleEvent else { return false }
        return event.eventID == AEEventID(kAEOpenApplication)
            && event.paramDescriptor(forKeyword: AEKeyword(keyAEPropData))?.enumCodeValue == AEKeyword(keyAELaunchedAsLogInItem)
    }
}

/// Page selection plus the `otzaria-widget://` deep links (`settings[/<page>]`, `open?url=<otzaria link>`).
@MainActor
final class SettingsRouter: ObservableObject {
    static let windowID = "settings"

    enum Page: String, CaseIterable, Identifiable {
        case status, location, calendar, behavior, about
        var id: String { rawValue }
        var title: String {
            switch self {
            case .status: "מצב"
            case .location: "מיקום הנתונים"
            case .calendar: "זמנים ולוח שנה"
            case .behavior: "תצוגה והתנהגות"
            case .about: "אודות"
            }
        }
        var symbol: String {
            switch self {
            case .status: "checkmark.circle"
            case .location: "folder"
            case .calendar: "calendar"
            case .behavior: "slider.horizontal.3"
            case .about: "info.circle"
            }
        }
    }

    @Published var page: Page = .status

    func handle(_ url: URL) {
        guard url.scheme == "otzaria-widget" else { return }
        switch url.host {
        case "open":
            let link = URLComponents(url: url, resolvingAgainstBaseURL: false)?.queryItems?.first { $0.name == "url" }?.value
            if let link { OtzariaLauncher.open(link: link) }
        default:
            if let name = url.pathComponents.dropFirst().first, let target = Page(rawValue: name) { page = target }
            NSApp.activate(ignoringOtherApps: true)
        }
    }
}

private struct MenuBarContent: View {
    @ObservedObject var model: SettingsModel
    @ObservedObject var agent: SnapshotAgent
    @ObservedObject var router: SettingsRouter
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        Button("פתח הגדרות") {
            NSApp.setActivationPolicy(.regular)
            NSApp.activate(ignoringOtherApps: true)
            openWindow(id: SettingsRouter.windowID)
        }
        Button("רענן עכשיו") { agent.refresh(force: true) }
        Button("פתח את אוצריא") { OtzariaLauncher.openOtzaria(appPath: agent.snapshot?.paths.otzariaApp) }
        Divider()
        Button("יציאה") { NSApp.terminate(nil) }
    }
}
