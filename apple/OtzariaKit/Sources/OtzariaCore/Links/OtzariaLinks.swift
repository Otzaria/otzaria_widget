import Foundation

/// Builds otzaria:// deep links (storage-and-links.md section 5.2). Only builds URLs: opening them is the host app's job
/// (NSWorkspace / openURL). Never generates plugin or install URLs.
public enum OtzariaLinks {
    public static let scheme = "otzaria://"
    /// Longest URI handed to the system.
    public static let maxUriLength = 8192
    private static let maxQueryEscapedLength = 3000
    private static let textTypes: Set<String> = ["TextBook", "DocxBook", "EpubBook", "DocumentBook"]
    private static let searchModes: Set<String> = ["advanced", "exact", "fuzzy", "smart"]
    private static let unreserved = CharacterSet(
        charactersIn: "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~")

    /// RFC 3986 unreserved-only escaping, identical to .NET's Uri.EscapeDataString.
    public static func escapeDataString(_ s: String) -> String {
        s.addingPercentEncoding(withAllowedCharacters: unreserved) ?? ""
    }

    /// BookSource.isValidSlug: letters, digits and "._-" (1 to 64 UTF-16 units).
    public static func isValidSlug(_ slug: String?) -> Bool {
        guard let slug, (1...64).contains(slug.utf16.count) else { return false }
        return slug.unicodeScalars.allSatisfy { scalar in
            if scalar == "." || scalar == "_" || scalar == "-" { return true }
            switch scalar.properties.generalCategory {
            case .uppercaseLetter, .lowercaseLetter, .titlecaseLetter, .modifierLetter, .otherLetter,
                 .decimalNumber, .letterNumber, .otherNumber:
                return true
            default:
                return false
            }
        }
    }

    /// Link that opens the entry at its position, or nil when the entry is not linkable.
    public static func buildLink(for entry: ReadingEntry) -> String? {
        let book = entry.book
        guard let id = book.id, id > 0 else { return nil }
        guard let suffix = sourceParam(book.source) else { return nil }

        let kind: String
        let index: Int
        if book.isPdf {
            kind = "pdf"
            index = max(entry.index, 1)
        } else if textTypes.contains(book.type) {
            kind = "book"
            index = max(entry.index, 0)
        } else {
            return nil
        }
        let query = suffix.isEmpty ? "index=\(index)" : "\(suffix)&index=\(index)"
        return "\(scheme)open/\(kind)/\(id)?\(query)"
    }

    /// Link that just opens a book (no position), or nil for non-positive ids.
    public static func buildBookOpenLink(bookId: Int) -> String? {
        bookId > 0 ? "\(scheme)open/book/\(bookId)" : nil
    }

    public static func buildSearchLink(query: String, mode: String?) -> String {
        let escaped = escapeDataString(query)
        // An absurdly long query would exceed what the system accepts: just open the search screen.
        if escaped.utf16.count > maxQueryEscapedLength { return "\(scheme)open/search" }
        var link = "\(scheme)open/search?q=\(escaped)"
        if let mode {
            let m = mode.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
            if searchModes.contains(m) { link += "&mode=\(m)" }
        }
        return link
    }

    /// "" for official, "source=user", "source=db:<slug>", or nil when the source is unusable.
    private static func sourceParam(_ source: String?) -> String? {
        guard let source, !source.isEmpty, source != "o" else { return "" }
        if source == "u" { return "source=user" }
        if source.hasPrefix("d:") {
            let slug = String(source.dropFirst(2))
            guard isValidSlug(slug) else { return nil }
            return "source=db:" + escapeDataString(slug)
        }
        return nil
    }

    /// Only `otzaria://open/...` URIs (what the widget generates) may be launched: no plugin installs, other hosts,
    /// plugin segments or characters that could break out of an argument.
    public static func isSafeOtzariaUri(_ uri: String?) -> Bool {
        guard let uri, !uri.isEmpty, uri.utf16.count <= maxUriLength else { return false }
        let forbidden: Set<Character> = ["\"", "`", "^", "<", ">", "|", "\\", "{", "}"]
        for ch in uri.unicodeScalars {
            if ch.value <= 0x20 || ch.value == 0x7F || forbidden.contains(Character(ch)) { return false }
        }
        let prefix = scheme + "open/"
        guard uri.lowercased().hasPrefix(prefix) else { return false }
        let rest = String(uri.dropFirst(prefix.count))
        let path = rest.firstIndex(where: { $0 == "?" || $0 == "#" }).map { String(rest[..<$0]) } ?? rest
        for segment in path.split(separator: "/", omittingEmptySubsequences: false) {
            let decoded = String(segment).removingPercentEncoding ?? String(segment)
            if decoded == "." || decoded == ".." || decoded.lowercased() == "plugin" { return false }
        }
        return true
    }
}
