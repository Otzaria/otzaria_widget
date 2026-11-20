import Foundation
import SQLite3
import XCTest
@testable import OtzariaCore

/// Temp directory removed on deinit.
final class TempDir {
    let url: URL
    var path: String { url.path }

    init() {
        url = FileManager.default.temporaryDirectory.appendingPathComponent("otz-core-" + UUID().uuidString)
        try! FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
    }

    deinit { try? FileManager.default.removeItem(at: url) }

    func file(_ name: String) -> String { url.appendingPathComponent(name).path }

    @discardableResult
    func write(_ name: String, _ text: String) -> String {
        let p = file(name)
        try! FileManager.default.createDirectory(atPath: (p as NSString).deletingLastPathComponent, withIntermediateDirectories: true)
        try! text.write(toFile: p, atomically: true, encoding: .utf8)
        return p
    }

    @discardableResult
    func write(_ name: String, bytes: [UInt8]) -> String {
        let p = file(name)
        try! FileManager.default.createDirectory(atPath: (p as NSString).deletingLastPathComponent, withIntermediateDirectories: true)
        try! Data(bytes).write(to: URL(fileURLWithPath: p))
        return p
    }
}

/// In-memory file system for path-resolution tests.
struct FakeEnv: ResolverEnvironment {
    var homeDirectory = "/Users/test"
    private(set) var files = Set<String>()
    private(set) var dirs = Set<String>()
    private(set) var texts: [String: String] = [:]

    func addingDir(_ dir: String) -> FakeEnv {
        var copy = self
        var d = dir
        while d != "/" && !d.isEmpty {
            copy.dirs.insert(d)
            d = (d as NSString).deletingLastPathComponent
        }
        return copy
    }

    func addingFile(_ path: String, text: String? = nil) -> FakeEnv {
        var copy = addingDir((path as NSString).deletingLastPathComponent)
        copy.files.insert(path)
        if let text { copy.texts[path] = text }
        return copy
    }

    func fileExists(_ path: String) -> Bool { files.contains(path) }
    func directoryExists(_ path: String) -> Bool { dirs.contains(path) }
    func readText(_ path: String) -> String? { texts[path] }
    func environmentVariable(_ name: String) -> String? { nil }
}

/// Test-only hive_ce writer, independent of the production parser (own CRC32).
struct HiveTestWriter {
    private(set) var bytes: [UInt8] = []

    mutating func put(_ key: String, _ value: [UInt8]) { bytes += Self.frame(Self.keyStr(key), value) }
    mutating func put(_ key: UInt32, _ value: [UInt8]) { bytes += Self.frame(Self.keyInt(key), value) }
    mutating func delete(_ key: String) { bytes += Self.frame(Self.keyStr(key), []) }
    mutating func appendRaw(_ raw: [UInt8]) { bytes += raw }

    static func frame(_ key: [UInt8], _ value: [UInt8]) -> [UInt8] {
        let len = 4 + key.count + value.count + 4
        var buf = u32(UInt32(len)) + key + value
        buf += u32(crc32(buf))
        return buf
    }

    static func keyStr(_ k: String) -> [UInt8] { [1, UInt8(k.utf8.count)] + Array(k.utf8) }
    static func keyInt(_ k: UInt32) -> [UInt8] { [0] + u32(k) }

    static func u32(_ v: UInt32) -> [UInt8] { (0..<4).map { UInt8((v >> (8 * UInt32($0))) & 0xFF) } }
    static func f64(_ v: Double) -> [UInt8] { (0..<8).map { UInt8((v.bitPattern >> (8 * UInt64($0))) & 0xFF) } }
    static func utf8Len(_ s: String) -> [UInt8] { u32(UInt32(s.utf8.count)) + Array(s.utf8) }

    static func null() -> [UInt8] { [0] }
    static func int(_ v: Double) -> [UInt8] { [1] + f64(v) }
    static func double(_ v: Double) -> [UInt8] { [2] + f64(v) }
    static func bool(_ v: Bool) -> [UInt8] { [3, v ? 1 : 0] }
    static func str(_ v: String) -> [UInt8] { [4] + utf8Len(v) }
    static func bytesValue(_ v: [UInt8]) -> [UInt8] { [5] + u32(UInt32(v.count)) + v }
    static func intList(_ v: [Double]) -> [UInt8] { [6] + u32(UInt32(v.count)) + v.flatMap(f64) }
    static func strList(_ v: [String]) -> [UInt8] { [9] + u32(UInt32(v.count)) + v.flatMap(utf8Len) }
    static func list(_ items: [[UInt8]]) -> [UInt8] { [10] + u32(UInt32(items.count)) + items.flatMap { $0 } }
    static func map(_ entries: [([UInt8], [UInt8])]) -> [UInt8] {
        [11] + u32(UInt32(entries.count)) + entries.flatMap { $0.0 + $0.1 }
    }
    static func dateMs(_ ms: Double) -> [UInt8] { [16] + f64(ms) }
    static func ext(_ typeId: UInt16, _ payload: [UInt8]) -> [UInt8] { [21, UInt8(typeId & 0xFF), UInt8(typeId >> 8)] + payload }

    static func crc32(_ data: [UInt8]) -> UInt32 {
        var c: UInt32 = 0xFFFF_FFFF
        for b in data {
            c ^= UInt32(b)
            for _ in 0..<8 { c = (c & 1) != 0 ? 0xEDB8_8320 ^ (c >> 1) : c >> 1 }
        }
        return c ^ 0xFFFF_FFFF
    }
}

/// A temp SQLite file written with the system library (the production code only ever reads).
final class SQLiteFixture {
    let dir = TempDir()
    let path: String
    private var db: OpaquePointer?

    init(name: String, wal: Bool = false) {
        path = dir.file(name)
        XCTAssertEqual(sqlite3_open(path, &db), SQLITE_OK)
        if wal { exec("PRAGMA journal_mode=WAL") }
    }

    deinit { close() }

    func close() {
        if db != nil { sqlite3_close(db); db = nil }
    }

    func exec(_ sql: String) {
        var err: UnsafeMutablePointer<CChar>?
        if sqlite3_exec(db, sql, nil, nil, &err) != SQLITE_OK {
            XCTFail("sqlite: \(err.map { String(cString: $0) } ?? "?") in \(sql)")
        }
    }

    func insert(_ sql: String, _ params: [String]) {
        var stmt: OpaquePointer?
        XCTAssertEqual(sqlite3_prepare_v2(db, sql, -1, &stmt, nil), SQLITE_OK)
        let transient = unsafeBitCast(-1, to: sqlite3_destructor_type.self)
        for (i, p) in params.enumerated() { sqlite3_bind_text(stmt, Int32(i + 1), p, -1, transient) }
        XCTAssertEqual(sqlite3_step(stmt), SQLITE_DONE)
        sqlite3_finalize(stmt)
    }
}

/// user_state.db with Otzaria's real schema.
final class UserStateFixture {
    let sqlite: SQLiteFixture
    var path: String { sqlite.path }

    init(userVersion: Int = 1, createSchema: Bool = true, wal: Bool = false) {
        sqlite = SQLiteFixture(name: "user_state.db", wal: wal)
        if createSchema {
            sqlite.exec("""
                CREATE TABLE lists (box TEXT NOT NULL, key TEXT NOT NULL, payload_json TEXT NOT NULL, updated_at INTEGER NOT NULL, PRIMARY KEY (box, key));
                CREATE TABLE window_sessions (slot INTEGER PRIMARY KEY, tabs_json TEXT NOT NULL, current_index INTEGER NOT NULL, active_workspace_id TEXT, bounds_json TEXT, updated_at INTEGER NOT NULL);
                """)
        }
        sqlite.exec("PRAGMA user_version = \(userVersion)")
    }

    func setList(_ box: String, _ key: String, _ payload: Any) { setListRaw(box, key, Self.json(payload)) }

    func setListRaw(_ box: String, _ key: String, _ json: String) {
        sqlite.insert("INSERT OR REPLACE INTO lists(box,key,payload_json,updated_at) VALUES(?,?,?,1)", [box, key, json])
    }

    func setSession(slot: Int, tabs: Any, current: Int, activeWorkspace: String? = nil) {
        sqlite.exec("DELETE FROM window_sessions WHERE slot = \(slot)")
        sqlite.insert("INSERT INTO window_sessions(slot,tabs_json,current_index,active_workspace_id,bounds_json,updated_at) VALUES(\(slot),?,\(current),?,NULL,1)",
                      [Self.json(tabs), activeWorkspace ?? ""])
        if activeWorkspace == nil { sqlite.exec("UPDATE window_sessions SET active_workspace_id = NULL WHERE slot = \(slot)") }
    }

    static func json(_ o: Any) -> String {
        String(data: try! JSONSerialization.data(withJSONObject: o, options: [.sortedKeys]), encoding: .utf8)!
    }

    static func book(_ id: Int?, _ title: String, type: String = "TextBook", source: String = "o",
                     path: String? = nil, versionTitle: String? = nil) -> [String: Any] {
        var d: [String: Any] = ["title": title, "type": type, "source": source, "categoryPath": "קטגוריה, תת"]
        d["id"] = id ?? NSNull()
        d["versionTitle"] = versionTitle ?? NSNull()
        if let path { d["path"] = path }
        return d
    }

    static func entry(_ book: [String: Any], index: Int, ref: String = "מקום", targetKind: String = "book",
                      label: String? = nil, createdAt: String? = nil) -> [String: Any] {
        ["ref": ref, "book": book, "index": index, "commentatorsToShow": [String](), "isSearch": false,
         "targetKind": targetKind, "label": label ?? NSNull(), "createdAt": createdAt ?? NSNull(),
         "extraUnknownField": ["a": 1]]
    }

    static func search(_ query: String, mode: String = "advanced", ref: String = "(ק)formatted") -> [String: Any] {
        ["ref": ref, "book": book(nil, query), "index": 0, "isSearch": true, "searchMode": mode, "targetKind": "book"]
    }
}

/// Real file system with a fake home directory.
struct TempHomeEnv: ResolverEnvironment {
    let homeDirectory: String
    private let system = SystemResolverEnvironment()
    func fileExists(_ path: String) -> Bool { system.fileExists(path) }
    func directoryExists(_ path: String) -> Bool { system.directoryExists(path) }
    func readText(_ path: String) -> String? { system.readText(path) }
    func environmentVariable(_ name: String) -> String? { nil }
}
