import Foundation
import SQLite3

/// Failure from the system SQLite library (`code` is the primary or extended result code).
struct SQLiteError: Error {
    let code: Int32
    let message: String

    var primaryCode: Int32 { code & 0xFF }
}

/// Minimal read-only SQLite connection over the system `SQLite3` module. Never opens with write access and never
/// uses `immutable`, so WAL databases that Otzaria holds open are read correctly.
final class SQLiteReadConnection {
    private var db: OpaquePointer?

    /// Opens `path` with `file:...?mode=ro` (SQLITE_OPEN_READONLY | SQLITE_OPEN_URI).
    init(path: String, busyTimeoutMs: Int32) throws {
        let uri = Self.readOnlyURI(path)
        let rc = sqlite3_open_v2(uri, &db, SQLITE_OPEN_READONLY | SQLITE_OPEN_URI, nil)
        if rc != SQLITE_OK {
            let message = db.map { String(cString: sqlite3_errmsg($0)) } ?? "open failed"
            sqlite3_close(db)
            db = nil
            throw SQLiteError(code: rc, message: message)
        }
        sqlite3_busy_timeout(db, busyTimeoutMs)
    }

    deinit { sqlite3_close(db) }

    static func readOnlyURI(_ path: String) -> String {
        var allowed = CharacterSet.urlPathAllowed
        allowed.remove(charactersIn: "?#%")
        let escaped = path.addingPercentEncoding(withAllowedCharacters: allowed) ?? path
        return "file:" + escaped + "?mode=ro"
    }

    enum Value {
        case null
        case int(Int64)
        case text(String)
        case other
    }

    /// Runs one statement and maps every row. Parameters are bound as text or integers.
    func query<T>(_ sql: String, _ params: [Value] = [], _ map: (Row) -> T) throws -> [T] {
        var stmt: OpaquePointer?
        var rc = sqlite3_prepare_v2(db, sql, -1, &stmt, nil)
        guard rc == SQLITE_OK else { throw SQLiteError(code: rc, message: String(cString: sqlite3_errmsg(db))) }
        defer { sqlite3_finalize(stmt) }
        let transient = unsafeBitCast(-1, to: sqlite3_destructor_type.self)
        for (i, p) in params.enumerated() {
            let idx = Int32(i + 1)
            switch p {
            case .int(let v): sqlite3_bind_int64(stmt, idx, v)
            case .text(let s): sqlite3_bind_text(stmt, idx, s, -1, transient)
            default: sqlite3_bind_null(stmt, idx)
            }
        }
        var rows: [T] = []
        while true {
            rc = sqlite3_step(stmt)
            if rc == SQLITE_ROW { rows.append(map(Row(stmt: stmt))) }
            else if rc == SQLITE_DONE { break }
            else { throw SQLiteError(code: rc, message: String(cString: sqlite3_errmsg(db))) }
        }
        return rows
    }

    func scalarInt(_ sql: String, _ params: [Value] = []) throws -> Int64? {
        try query(sql, params) { $0.int(0) }.first ?? nil
    }

    struct Row {
        fileprivate let stmt: OpaquePointer?

        func isNull(_ i: Int32) -> Bool { sqlite3_column_type(stmt, i) == SQLITE_NULL }

        func int(_ i: Int32) -> Int64? { isNull(i) ? nil : sqlite3_column_int64(stmt, i) }

        func text(_ i: Int32) -> String? {
            guard !isNull(i), let c = sqlite3_column_text(stmt, i) else { return nil }
            return String(cString: c)
        }
    }
}
