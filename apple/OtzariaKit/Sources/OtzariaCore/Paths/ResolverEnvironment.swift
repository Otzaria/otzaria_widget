import Foundation

/// Thin abstraction over the file system and environment so path resolution can be tested with fakes.
/// Every member must be exception-free (return false/nil on failure).
public protocol ResolverEnvironment: Sendable {
    /// The user's home directory (for example /Users/name).
    var homeDirectory: String { get }
    func fileExists(_ path: String) -> Bool
    func directoryExists(_ path: String) -> Bool
    /// Whole file as UTF-8 text (BOM stripped by the caller), or nil on any failure.
    func readText(_ path: String) -> String?
    func environmentVariable(_ name: String) -> String?
}

/// Real implementation. Read-only.
public struct SystemResolverEnvironment: ResolverEnvironment {
    public init() {}

    public var homeDirectory: String {
        if let h = ProcessInfo.processInfo.environment["HOME"], !h.isEmpty { return h }
        return NSHomeDirectory()
    }

    public func fileExists(_ path: String) -> Bool {
        var isDir: ObjCBool = false
        return FileManager.default.fileExists(atPath: path, isDirectory: &isDir) && !isDir.boolValue
    }

    public func directoryExists(_ path: String) -> Bool {
        var isDir: ObjCBool = false
        return FileManager.default.fileExists(atPath: path, isDirectory: &isDir) && isDir.boolValue
    }

    public func readText(_ path: String) -> String? {
        // These are tiny text/json files: refuse anything absurd.
        guard let attrs = try? FileManager.default.attributesOfItem(atPath: path),
              let size = attrs[.size] as? Int, size <= 1024 * 1024,
              let data = FileManager.default.contents(atPath: path) else { return nil }
        return String(data: data, encoding: .utf8)
    }

    public func environmentVariable(_ name: String) -> String? { ProcessInfo.processInfo.environment[name] }
}
