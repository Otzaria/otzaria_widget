import AppKit
import OtzariaCore

enum OtzariaLauncher {
    /// Opens Otzaria itself (the located app, else whatever handles otzaria://).
    static func openOtzaria(appPath: String?) {
        if let appPath {
            let url = URL(fileURLWithPath: appPath)
            let bundle = url.pathExtension == "app" ? url : url.deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            if bundle.pathExtension == "app" {
                NSWorkspace.shared.openApplication(at: bundle, configuration: NSWorkspace.OpenConfiguration())
                return
            }
        }
        open(link: "otzaria://open/history")
    }

    /// Opens an otzaria:// link only when it passes the whitelist check.
    @discardableResult
    static func open(link: String) -> Bool {
        guard OtzariaLinks.isSafeOtzariaUri(link), let url = URL(string: link) else { return false }
        return NSWorkspace.shared.open(url)
    }
}
