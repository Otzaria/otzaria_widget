import Foundation

/// Read-only hive_ce box reader. Loads the whole file with a plain read-only descriptor (no locks taken),
/// retrying on transient errors because Hive compaction renames `.hivec` over `.hive`, then parses in memory.
/// Never writes and never throws for corrupt content (stops at the first bad frame).
public struct HiveReader: Sendable {
    /// Sanity cap: a real app_preferences.hive is KBs to a few MB.
    static let maxFileBytes = 256 * 1024 * 1024
    private static let maxAttempts = 4

    public init() {}

    public func read(path: String, keyFilter: (String) -> Bool) -> [String: HiveValue] {
        readDetailed(path: path, keyFilter: keyFilter).values
    }

    /// Like `read` but also reports frame statistics and why parsing stopped.
    public func readDetailed(path: String, keyFilter: (String) -> Bool = { _ in true }) -> HiveParseResult {
        guard let bytes = Self.readAllBytes(path) else {
            return HiveParseResult(values: [:], frameCount: 0, tombstoneCount: 0, skippedValueCount: 0,
                                   parsedBytes: 0, totalBytes: 0, error: "file not readable")
        }
        return HiveParser.parse(bytes, keyFilter: keyFilter)
    }

    private static func readAllBytes(_ path: String) -> [UInt8]? {
        if path.trimmingCharacters(in: .whitespaces).isEmpty { return nil }
        for attempt in 0..<maxAttempts {
            let fd = open(path, O_RDONLY)
            if fd < 0 {
                let code = errno
                if code == ENOENT || code == ENOTDIR {
                    // "Missing" normally means Otzaria isn't installed: one short retry only.
                    if attempt == 0 { usleep(30_000); continue }
                    return nil
                }
                if code == EACCES || code == EPERM || code == ENAMETOOLONG { return nil }
                if attempt < maxAttempts - 1 { usleep(UInt32(50_000 << attempt)) }
                continue
            }
            defer { close(fd) }
            var st = stat()
            guard fstat(fd, &st) == 0 else { continue }
            let size = Int(st.st_size)
            if size > maxFileBytes { return nil }
            var buffer = [UInt8](repeating: 0, count: size)
            var offset = 0
            while offset < size {
                let n = buffer.withUnsafeMutableBytes { raw in
                    Foundation.read(fd, raw.baseAddress! + offset, size - offset)
                }
                if n <= 0 { break }
                offset += n
            }
            if offset < size { buffer.removeSubrange(offset...) }
            return buffer
        }
        return nil
    }
}
