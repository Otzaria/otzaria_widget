import Foundation

/// Plain-text log in ~/Library/Logs/OtzariaWidget. Never holds personal content (no titles, notes or searches).
enum AppLog {
    static let directory = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Library/Logs/OtzariaWidget", isDirectory: true)
    private static let file = directory.appendingPathComponent("widget.log")
    private static let maxBytes = 256 * 1024
    private static let queue = DispatchQueue(label: "org.otzaria.widget.log")

    static func write(_ message: String) {
        queue.async {
            let fm = FileManager.default
            try? fm.createDirectory(at: directory, withIntermediateDirectories: true)
            if let size = (try? fm.attributesOfItem(atPath: file.path))?[.size] as? Int, size > maxBytes {
                let old = directory.appendingPathComponent("widget.log.old")
                try? fm.removeItem(at: old)
                try? fm.moveItem(at: file, to: old)
            }
            let line = "\(ISO8601DateFormatter().string(from: Date())) \(message)\n"
            if let handle = try? FileHandle(forWritingTo: file) {
                defer { try? handle.close() }
                _ = try? handle.seekToEnd()
                try? handle.write(contentsOf: Data(line.utf8))
            } else {
                try? Data(line.utf8).write(to: file)
            }
        }
    }
}
