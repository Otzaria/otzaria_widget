import XCTest
@testable import OtzariaCore

final class LibraryCatalogTests: XCTestCase {
    private func makeDb() -> SQLiteFixture {
        let db = SQLiteFixture(name: "seforim.db")
        db.exec("""
            CREATE TABLE category (id INTEGER PRIMARY KEY, title TEXT, level INTEGER);
            CREATE TABLE category_closure (ancestorId INTEGER, descendantId INTEGER);
            CREATE TABLE book (id INTEGER PRIMARY KEY, categoryId INTEGER, title TEXT, totalLines INTEGER);
            CREATE TABLE tocEntry (id INTEGER PRIMARY KEY, bookId INTEGER, parentId INTEGER);
            INSERT INTO category VALUES (1,'תנ"ך',0),(2,'תורה',1),(3,'תלמוד',0),(4,'תלמוד בבלי',1);
            INSERT INTO category_closure VALUES (1,1),(1,2),(2,2),(3,3),(3,4),(4,4);
            INSERT INTO book VALUES (1,2,'בראשית',100),(2,4,'ברכות',50),(3,2,'ללא תוכן',0);
            -- book 1: root 10 -> children 11, 12; 12 -> 13   => leaves 11 and 13
            INSERT INTO tocEntry VALUES (10,1,NULL),(11,1,10),(12,1,10),(13,1,12);
            """)
        return db
    }

    func testLeavesOfTocForest() {
        let db = makeDb()
        XCTAssertEqual(LibraryCatalog(databasePath: db.path).learnableItemCount(bookId: 1), 2)
        withExtendedLifetime(db) {}
    }

    func testNoTocFallsBackToTotalLinesAndTalmudDoubles() {
        let db = makeDb()
        let c = LibraryCatalog(databasePath: db.path)
        XCTAssertEqual(c.learnableItemCount(bookId: 2), 100)
        XCTAssertEqual(c.learnableItemCount(bookId: 3), 1)
        XCTAssertNil(c.learnableItemCount(bookId: 99))
    }

    func testOutOfRangeTotalLinesGivesNoCountInsteadOfTrapping() {
        let db = makeDb()
        db.exec("INSERT INTO book VALUES (4,4,'פגום',9223372036854775807),(5,2,'גדול',4611686018427387904),(6,4,'שלילי',-9223372036854775808)")
        let c = LibraryCatalog(databasePath: db.path)
        XCTAssertNil(c.learnableItemCount(bookId: 4))
        XCTAssertNil(c.learnableItemCount(bookId: 5))
        XCTAssertEqual(c.learnableItemCount(bookId: 6), 2)
        db.exec("INSERT INTO book VALUES (7,4,'גבול',2147483647)")
        XCTAssertEqual(c.learnableItemCount(bookId: 7), 2 * 2_147_483_647)
    }

    func testCycleInTocDoesNotHang() {
        let db = makeDb()
        db.exec("INSERT INTO tocEntry VALUES (20,3,NULL),(21,3,20),(22,3,21),(23,3,22)")
        XCTAssertEqual(LibraryCatalog(databasePath: db.path).learnableItemCount(bookId: 3), 1)
    }

    func testBookInfoAndTitleLookup() {
        let db = makeDb()
        let c = LibraryCatalog(databasePath: db.path)
        let info = c.bookInfo(bookId: 2)
        XCTAssertEqual(info?.title, "ברכות")
        XCTAssertEqual(info?.categoryPath, "תלמוד, תלמוד בבלי")
        XCTAssertEqual(c.bookInfo(bookId: 1)?.categoryPath, "תנ\"ך, תורה")
        XCTAssertNil(c.bookInfo(bookId: 77))
        XCTAssertEqual(c.findBookId(title: "בראשית"), 1)
        XCTAssertNil(c.findBookId(title: "אין"))
        XCTAssertNil(c.findBookId(title: ""))
    }

    func testMissingOrBrokenDatabaseReturnsNilNeverThrows() {
        let missing = LibraryCatalog(databasePath: "/nonexistent/seforim.db")
        XCTAssertNil(missing.bookInfo(bookId: 1))
        XCTAssertNil(missing.learnableItemCount(bookId: 1))
        XCTAssertFalse(missing.canOpen())
        let empty = SQLiteFixture(name: "empty.db")
        XCTAssertTrue(LibraryCatalog(databasePath: empty.path).canOpen())
        XCTAssertNil(LibraryCatalog(databasePath: empty.path).bookInfo(bookId: 1))
    }

    func testReadOnlyConnectionCannotWrite() throws {
        let db = makeDb()
        let conn = try SQLiteReadConnection(path: db.path, busyTimeoutMs: 100)
        XCTAssertThrowsError(try conn.query("INSERT INTO book VALUES (9,1,'x',1)") { _ in 0 })
    }

    func testProgressUsesCatalog() {
        let db = makeDb()
        let c = LibraryCatalog(databasePath: db.path)
        var p = OtzariaPreferences()
        p.shamorZachorRaw = ["sz:tracked_books": "[1,99]", "sz:progress_by_id": "{\"1\":{\"0\":{\"learn\":true}}}"]
        let r = ProgressService().compute(preferences: p, catalog: c)
        XCTAssertEqual(r.map(\.bookId), [1])
        XCTAssertEqual(r[0].totalItems, 2)
        XCTAssertEqual(r[0].title, "בראשית")
        XCTAssertEqual(r[0].statusText, "חצי הדרך מאחוריך!")
    }
}
