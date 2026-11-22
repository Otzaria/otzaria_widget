import Foundation

/// A decoded Hive value. Map keys are normalised to strings (integer keys become decimal strings).
public enum HiveValue: Sendable, Equatable {
    case null
    case int(Int64)
    case double(Double)
    case bool(Bool)
    case string(String)
    case bytes([UInt8])
    case list([HiveValue])
    case map([String: HiveValue])
    case date(Date)
    case duration(TimeInterval)

    public var stringValue: String? { if case .string(let s) = self { return s } else { return nil } }
    public var boolValue: Bool? { if case .bool(let b) = self { return b } else { return nil } }
    public var intValue: Int64? { if case .int(let i) = self { return i } else { return nil } }
}

/// Outcome of parsing a hive_ce box file (hive-and-progress.md section 1).
public struct HiveParseResult: Sendable, Equatable {
    public var values: [String: HiveValue]
    public var frameCount: Int
    public var tombstoneCount: Int
    public var skippedValueCount: Int
    public var parsedBytes: Int
    public var totalBytes: Int
    public var error: String?

    /// True when every byte of the file was consumed by CRC-valid frames.
    public var isClean: Bool { error == nil }
}

private struct HiveFormatError: Error {
    let message: String
    init(_ message: String) { self.message = message }
}

/// Pure in-memory hive_ce frame parser. Never throws on malformed data; stops at the first bad frame.
public enum HiveParser {
    private static let maxDepth = 64

    public static func parse(_ data: [UInt8], keyFilter: (String) -> Bool = { _ in true }) -> HiveParseResult {
        var map: [String: HiveValue] = [:]
        var pos = 0
        var frames = 0, tombstones = 0, skipped = 0
        var error: String?
        let n = data.count

        while n - pos >= 4 {
            let lenU = readU32(data, pos)
            if lenU < 8 || Int(lenU) > n - pos {
                error = lenU < 8 ? "invalid frame length" : "truncated frame"
                break
            }
            let len = Int(lenU)
            let crc = readU32(data, pos + len - 4)
            if Crc32.compute(data, range: pos..<(pos + len - 4)) != crc {
                error = "CRC mismatch"
                break
            }

            var r = Cursor(data, pos: pos + 4, limit: pos + len - 4)
            var key: String?
            do { key = try readKey(&r) } catch { skipped += 1 }

            if let key, keyFilter(key) {
                if r.pos == r.limit {
                    map[key] = nil
                    tombstones += 1
                } else {
                    do {
                        map[key] = try readValue(&r, depth: 0)
                    } catch {
                        // Unknown custom adapter / corrupt value: the old value is superseded and unreadable.
                        map[key] = nil
                        skipped += 1
                    }
                }
            }
            frames += 1
            pos += len
        }

        if error == nil && pos != n { error = "partial length prefix at end of file" }
        return HiveParseResult(values: map, frameCount: frames, tombstoneCount: tombstones,
                               skippedValueCount: skipped, parsedBytes: pos, totalBytes: n, error: error)
    }

    private static func readU32(_ d: [UInt8], _ p: Int) -> UInt32 {
        UInt32(d[p]) | UInt32(d[p + 1]) << 8 | UInt32(d[p + 2]) << 16 | UInt32(d[p + 3]) << 24
    }

    private static func readKey(_ r: inout Cursor) throws -> String {
        switch try r.u8() {
        case 0: return String(try r.u32())
        case 1: return try r.str(Int(try r.u8()))
        default: throw HiveFormatError("unsupported key type")
        }
    }

    private static func readKeyValue(_ r: inout Cursor) throws -> HiveValue {
        switch try r.u8() {
        case 0: return .int(Int64(try r.u32()))
        case 1: return .string(try r.str(Int(try r.u8())))
        default: throw HiveFormatError("unsupported key type")
        }
    }

    /// Hive stores ints as IEEE doubles; truncation toward zero like Dart's double.toInt().
    private static func readHiveInt(_ r: inout Cursor) throws -> Int64 {
        let d = try r.f64()
        if d.isNaN || d >= 9.2233720368547758E18 || d <= -9.2233720368547758E18 {
            throw HiveFormatError("int out of range")
        }
        return Int64(d)
    }

    private static func readCount(_ r: inout Cursor, minElementBytes: Int) throws -> Int {
        let c = try r.u32()
        if Int64(c) * Int64(minElementBytes) > Int64(r.remaining) { throw HiveFormatError("bad element count") }
        return Int(c)
    }

    private static func readValue(_ r: inout Cursor, depth: Int) throws -> HiveValue {
        if depth > maxDepth { throw HiveFormatError("nesting too deep") }
        var t = Int(try r.u8())
        if t == 21 { t = Int(try r.u16()) }
        switch t {
        case 0: return .null
        case 1: return .int(try readHiveInt(&r))
        case 2: return .double(try r.f64())
        case 3: return .bool(try r.u8() > 0)
        case 4: return .string(try r.str(try r.u32AsLength()))
        case 5: return .bytes(try r.bytes(try r.u32AsLength()))
        case 6, 13:
            let c = try readCount(&r, minElementBytes: 8)
            return .list(try (0..<c).map { _ in .int(try readHiveInt(&r)) })
        case 7, 14:
            let c = try readCount(&r, minElementBytes: 8)
            return .list(try (0..<c).map { _ in .double(try r.f64()) })
        case 8:
            let c = try readCount(&r, minElementBytes: 1)
            return .list(try (0..<c).map { _ in .bool(try r.u8() > 0) })
        case 9, 15:
            let c = try readCount(&r, minElementBytes: 4)
            return .list(try (0..<c).map { _ in .string(try r.str(try r.u32AsLength())) })
        case 10, 19:
            let c = try readCount(&r, minElementBytes: 1)
            return .list(try (0..<c).map { _ in try readValue(&r, depth: depth + 1) })
        case 11:
            let c = try readCount(&r, minElementBytes: 2)
            var m: [String: HiveValue] = [:]
            for _ in 0..<c {
                let k = try readValue(&r, depth: depth + 1)
                let v = try readValue(&r, depth: depth + 1)
                if let name = mapKeyName(k) { m[name] = v }
            }
            return .map(m)
        case 12: // HiveList: u32 count, u8 boxNameLen + name, count x key
            let c = try readCount(&r, minElementBytes: 2)
            _ = try r.str(Int(try r.u8()))
            return .list(try (0..<c).map { _ in try readKeyValue(&r) })
        case 16:
            return .date(Date(timeIntervalSince1970: Double(try readHiveInt(&r)) / 1000))
        case 17:
            let s = try r.str(Int(try r.u8()))
            guard let v = Int64(s) else { throw HiveFormatError("bad bigint") }
            return .int(v)
        case 18:
            let ms = try r.f64()
            _ = try r.u8()
            if ms.isNaN || abs(ms) > 2.5e14 { throw HiveFormatError("bad datetime") }
            return .date(Date(timeIntervalSince1970: ms / 1000))
        case 20:
            let ms = try r.f64()
            if ms.isNaN || abs(ms) > 9e14 { throw HiveFormatError("bad duration") }
            return .duration(ms / 1000)
        default:
            throw HiveFormatError("unknown type id (custom adapter)")
        }
    }

    private static func mapKeyName(_ k: HiveValue) -> String? {
        switch k {
        case .string(let s): return s
        case .int(let i): return String(i)
        case .double(let d): return String(d)
        case .bool(let b): return b ? "true" : "false"
        default: return nil
        }
    }

    private struct Cursor {
        let b: [UInt8]
        var pos: Int
        let limit: Int

        init(_ b: [UInt8], pos: Int, limit: Int) {
            self.b = b
            self.pos = pos
            self.limit = limit
        }

        var remaining: Int { limit - pos }

        private func need(_ n: Int) throws {
            if n < 0 || n > limit - pos { throw HiveFormatError("not enough bytes") }
        }

        mutating func u8() throws -> UInt8 {
            try need(1)
            defer { pos += 1 }
            return b[pos]
        }

        mutating func u16() throws -> UInt16 {
            try need(2)
            defer { pos += 2 }
            return UInt16(b[pos]) | UInt16(b[pos + 1]) << 8
        }

        mutating func u32() throws -> UInt32 {
            try need(4)
            defer { pos += 4 }
            return UInt32(b[pos]) | UInt32(b[pos + 1]) << 8 | UInt32(b[pos + 2]) << 16 | UInt32(b[pos + 3]) << 24
        }

        mutating func u32AsLength() throws -> Int {
            let v = try u32()
            if v > UInt32(Int32.max) { throw HiveFormatError("bad length") }
            return Int(v)
        }

        mutating func f64() throws -> Double {
            try need(8)
            var bits: UInt64 = 0
            for i in 0..<8 { bits |= UInt64(b[pos + i]) << (8 * UInt64(i)) }
            pos += 8
            return Double(bitPattern: bits)
        }

        mutating func str(_ n: Int) throws -> String {
            try need(n)
            defer { pos += n }
            return String(decoding: b[pos..<(pos + n)], as: UTF8.self)
        }

        mutating func bytes(_ n: Int) throws -> [UInt8] {
            try need(n)
            defer { pos += n }
            return Array(b[pos..<(pos + n)])
        }
    }
}
