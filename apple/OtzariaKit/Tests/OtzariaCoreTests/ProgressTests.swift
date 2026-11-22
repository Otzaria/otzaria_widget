import XCTest
@testable import OtzariaCore

final class ProgressTests: XCTestCase {
    private struct FakeCatalog: LibraryCatalogProviding {
        var books: [Int: (title: String, cat: String?, n: Int?)] = [:]
        func learnableItemCount(bookId: Int) -> Int? { books[bookId]?.n }
        func bookInfo(bookId: Int) -> (title: String, categoryPath: String?)? { books[bookId].map { ($0.title, $0.cat) } }
        func findBookId(title: String) -> Int? { nil }
    }

    private func catalog(_ books: (Int, String, String?, Int?)...) -> FakeCatalog {
        var c = FakeCatalog()
        for b in books { c.books[b.0] = (b.1, b.2, b.3) }
        return c
    }

    private func json(_ o: Any) -> String { UserStateFixture.json(o) }

    /// sz:progress_by_id for books: bookId -> [(item index, column)]
    private func progress(_ books: [Int: [(Int, String)]]) -> String {
        var root: [String: Any] = [:]
        for (book, marks) in books {
            var items: [String: [String: Bool]] = [:]
            for (idx, col) in marks { items[String(idx), default: [:]][col] = true }
            root[String(book)] = items
        }
        return json(root)
    }

    private func range(_ col: String, _ from: Int, _ to: Int) -> [(Int, String)] { (from..<to).map { ($0, col) } }

    private func prefs(tracked: String? = nil, progress: String? = nil, columns: String? = nil,
                       completion: String? = nil, columnCompletion: String? = nil) -> OtzariaPreferences {
        var d: [String: String] = [:]
        if let tracked { d["sz:tracked_books"] = tracked }
        if let progress { d["sz:progress_by_id"] = progress }
        if let columns { d["sz:book_columns"] = columns }
        if let completion { d["sz:completion_dates_by_id"] = completion }
        if let columnCompletion { d["sz:column_completion_dates"] = columnCompletion }
        return OtzariaPreferences(shamorZachorRaw: d)
    }

    private func single(_ p: OtzariaPreferences, _ c: LibraryCatalogProviding?, file: StaticString = #filePath, line: UInt = #line) -> BookProgress {
        let r = ProgressService().compute(preferences: p, catalog: c)
        XCTAssertEqual(r.count, 1, file: file, line: line)
        return r[0]
    }

    func testNoDataGivesEmpty() {
        XCTAssertTrue(ProgressService().compute(preferences: OtzariaPreferences(), catalog: catalog()).isEmpty)
        XCTAssertTrue(ProgressService().compute(preferences: OtzariaPreferences(), catalog: nil).isEmpty)
    }

    func testDefaultColumnsHalfDone() {
        let b = single(prefs(tracked: "[1]", progress: progress([1: range("learn", 0, 5)])), catalog((1, "בראשית", "תנ\"ך", 10)))
        XCTAssertEqual(b.bookId, 1)
        XCTAssertEqual(b.title, "בראשית")
        XCTAssertEqual(b.categoryPath, "תנ\"ך")
        XCTAssertEqual(b.totalItems, 10)
        XCTAssertEqual(b.columns.map(\.id), ["learn", "review1", "review2", "review3"])
        XCTAssertEqual(b.columns.map(\.label), ["לימוד", "חזרה 1", "חזרה 2", "חזרה 3"])
        XCTAssertEqual(b.columns.map(\.done), [5, 0, 0, 0])
        XCTAssertEqual(b.headline, 0.5)
        XCTAssertEqual(b.visibleColumns.map(\.id), ["learn"])
        XCTAssertTrue(b.isInProgress)
        XCTAssertTrue(b.isTracked)
        XCTAssertFalse(b.isCompleted)
        XCTAssertEqual(b.statusText, "חצי הדרך מאחוריך!")
        XCTAssertEqual(b.currentCycleText, "5 מתוך 10")
        XCTAssertEqual(b.deepLink, "otzaria://open/book/1")
    }

    func testStaleAndInvalidIndexesIgnored() {
        let marks = range("learn", 8, 12) + [(99, "learn"), (-1, "learn")]
        let b = single(prefs(progress: progress([1: marks])), catalog((1, "x", nil, 10)))
        XCTAssertEqual(b.columns[0].done, 2)
        XCTAssertEqual(b.columns[0].ratio, 0.2, accuracy: 1e-10)
        XCTAssertTrue(b.isInProgress)
    }

    func testCompletedCountsEveryEntryButRingsUseValidItems() {
        let cat = catalog((1, "x", nil, 3))
        let marks = range("learn", 0, 3) + range("review1", 0, 3) + range("review2", 0, 3) + range("review3", 10, 13)
        let b = single(prefs(progress: progress([1: marks])), cat)
        XCTAssertTrue(b.isCompleted)
        XCTAssertEqual(b.columns[3].done, 0)
        XCTAssertFalse(b.isInProgress)
        let few = range("learn", 0, 3) + range("review1", 0, 3) + range("review2", 0, 3) + range("review3", 10, 12)
        XCTAssertFalse(single(prefs(progress: progress([1: few])), cat).isCompleted)
    }

    func testItemKeysMustBeCanonicalDecimal() {
        let j = "{\"1\":{\"1\":{\"learn\":true},\"01\":{\"learn\":true},\" 2\":{\"learn\":true},\"+3\":{\"learn\":true},\"4.0\":{\"learn\":true},\"x\":{\"learn\":true},\"0\":{\"learn\":true}}}"
        XCTAssertEqual(single(prefs(progress: j), catalog((1, "x", nil, 5))).columns[0].done, 2)
    }

    func testIsValidIndexKey() {
        let cases: [(String, Int, Bool)] = [
            ("0", 1, true), ("0", 0, false), ("9", 10, true), ("10", 10, false), ("-1", 10, false), ("007", 10, false),
            ("", 10, false), ("99999999999", Int(Int32.max), false), ("2147483646", Int(Int32.max), true),
        ]
        for (k, n, expected) in cases { XCTAssertEqual(ProgressService.isValidIndexKey(k, n), expected, k) }
    }

    func testCurrentCycleText() {
        let cat = catalog((1, "x", nil, 10))
        XCTAssertEqual(single(prefs(progress: progress([1: range("learn", 0, 10) + range("review1", 0, 3)])), cat).currentCycleText, "3 מתוך 10")
        XCTAssertEqual(single(prefs(progress: progress([1: range("learn", 0, 10)])), cat).currentCycleText, "0 מתוך 10")
        let all = ["learn", "review1", "review2", "review3"].flatMap { range($0, 0, 10) }
        XCTAssertNil(single(prefs(progress: progress([1: all])), cat).currentCycleText)
        XCTAssertNil(single(prefs(tracked: "[1]"), nil).currentCycleText)
    }

    func testStatusTextThresholds() {
        let cat = catalog((1, "x", nil, 100))
        let expected: [(Int, String)] = [
            (0, "עדיין לא התחלת!"), (1, "התחלה מצוינת!"), (29, "התחלה מצוינת!"), (30, "שליש הדרך כבר הושלם!"),
            (49, "שליש הדרך כבר הושלם!"), (50, "חצי הדרך מאחוריך!"), (59, "חצי הדרך מאחוריך!"),
            (60, "רוב הדרך כבר מאחוריך!"), (74, "רוב הדרך כבר מאחוריך!"), (75, "הסוף כבר באופק!"), (99, "הסוף כבר באופק!"),
        ]
        for (done, text) in expected {
            let b = single(prefs(tracked: "[1]", progress: progress([1: range("learn", 0, done)])), cat)
            XCTAssertEqual(b.statusText, text, "done=\(done)")
        }
        XCTAssertEqual(single(prefs(tracked: "[1]"), catalog((1, "x", nil, 0))).statusText, "לימוד פעיל")
    }

    func testCycleDoneMessages() {
        let cat = catalog((1, "x", nil, 4))
        let first = single(prefs(progress: progress([1: range("learn", 0, 4)])), cat)
        XCTAssertEqual(first.statusText.isEmpty, false)
        let second = single(prefs(progress: progress([1: range("learn", 0, 4) + range("review1", 0, 4)])), cat)
        XCTAssertEqual(second.visibleColumns.map(\.id), ["learn", "review1", "review2"])
        let all = ["learn", "review1", "review2", "review3"].flatMap { range($0, 0, 4) }
        XCTAssertEqual(single(prefs(progress: progress([1: all])), cat).statusText, "סיימת מחזור רביעי בהצלחה!")
    }

    func testVisibleRingRuleForAllPatterns() {
        let cat = catalog((1, "x", nil, 2))
        let levels = [0, 1, 2]
        for a in levels { for b in levels { for c in levels { for d in levels {
            let r = [a, b, c, d]
            var marks: [(Int, String)] = []
            for (i, col) in ["learn", "review1", "review2", "review3"].enumerated() { marks += range(col, 0, r[i]) }
            let book = single(prefs(tracked: "[1]", progress: progress([1: marks])), cat)
            let expected = (0..<4).filter { $0 == 0 || r[$0] > 0 || r[$0 - 1] >= 2 }
            XCTAssertEqual(book.visibleColumns.map(\.id), expected.map { ["learn", "review1", "review2", "review3"][$0] }, "\(r)")
        } } } }
    }

    func testCustomColumnsAndDuplicates() {
        let columns = json(["1": [["id": "a", "label": "ראשון"], ["id": "a", "label": "כפול"], ["id": "b"], ["id": "c", "label": "  "], ["nope": 1]]])
        let b = single(prefs(tracked: "[1]", columns: columns), catalog((1, "x", nil, 4)))
        XCTAssertEqual(b.columns.map(\.id), ["a", "b", "c"])
        XCTAssertEqual(b.columns.map(\.label), ["ראשון", "מחזור 2", "מחזור 3"])
    }

    func testUnknownBookDroppedAndNoCatalogKeepsEverything() {
        let p = prefs(tracked: "[1,2]")
        let withCatalog = ProgressService().compute(preferences: p, catalog: catalog((1, "x", nil, 3)))
        XCTAssertEqual(withCatalog.map(\.bookId), [1])
        let without = ProgressService().compute(preferences: p, catalog: nil)
        XCTAssertEqual(without.map(\.bookId).sorted(), [1, 2])
        XCTAssertEqual(without.first { $0.bookId == 2 }?.title, "ספר 2")
        XCTAssertEqual(without.first?.totalItems, 0)
    }

    func testMalformedJsonNeverThrows() {
        let p = prefs(tracked: "[1, \"2\", 2.0, null, \"x\", true]", progress: "{broken", columns: "[]", completion: "5")
        XCTAssertEqual(ProgressService().compute(preferences: p, catalog: nil).map(\.bookId), [2, 1])
    }

    func testOrdering() {
        let cat = catalog((1, "a", nil, 2), (2, "b", nil, 2), (3, "c", nil, 2), (4, "d", nil, 2))
        let done = ["learn", "review1", "review2", "review3"].flatMap { range($0, 0, 2) }
        let p = prefs(tracked: "[1,2,3]", progress: progress([1: range("learn", 0, 1), 3: done, 4: range("learn", 0, 1)]),
                      completion: json(["3": "2026-01-02T03:04:05Z"]))
        let r = ProgressService().compute(preferences: p, catalog: cat)
        // in progress with progress: 4 (untracked, pos -1) after 1 (tracked); tracked-only 2 last of in-progress; completed 3 last
        XCTAssertEqual(r.map(\.bookId), [1, 4, 2, 3])
        XCTAssertEqual(r.last?.completedAt, DateParsing.parse("2026-01-02T03:04:05Z"))
    }

    func testCompletionAndColumnDates() {
        let p = prefs(tracked: "[1]", completion: json(["1": "2026-03-04T05:06:07.123456"]),
                      columnCompletion: json(["1": ["learn": "2026-03-04T05:06:07Z"]]))
        let b = single(p, nil)
        XCTAssertNotNil(b.completedAt)
        XCTAssertNotNil(b.columns[0].completedAt)
        XCTAssertNil(b.columns[1].completedAt)
    }
}
