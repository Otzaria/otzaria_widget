import XCTest
@testable import OtzariaCore

final class LinksTests: XCTestCase {
    private func entry(_ id: Int?, _ type: String = "TextBook", _ source: String = "o", _ index: Int = 0) -> ReadingEntry {
        ReadingEntry(book: BookRef(id: id, title: "ספר", type: type, source: source), index: index)
    }

    func testBuildLinkMapping() {
        let cases: [(Int, String, String, Int, String)] = [
            (1938, "TextBook", "o", 140, "otzaria://open/book/1938?index=140"),
            (1938, "TextBook", "o", 0, "otzaria://open/book/1938?index=0"),
            (1938, "TextBook", "o", -3, "otzaria://open/book/1938?index=0"),
            (131, "PdfBook", "o", 150, "otzaria://open/pdf/131?index=150"),
            (131, "PdfBook", "o", 0, "otzaria://open/pdf/131?index=1"),
            (5, "TextBook", "u", 7, "otzaria://open/book/5?source=user&index=7"),
            (5, "PdfBook", "u", 7, "otzaria://open/pdf/5?source=user&index=7"),
            (5, "TextBook", "d:my-lib_1.2", 7, "otzaria://open/book/5?source=db:my-lib_1.2&index=7"),
            (6, "DocxBook", "o", 2, "otzaria://open/book/6?index=2"),
            (6, "EpubBook", "o", 2, "otzaria://open/book/6?index=2"),
            (6, "DocumentBook", "o", 2, "otzaria://open/book/6?index=2"),
        ]
        for (id, type, source, index, expected) in cases {
            XCTAssertEqual(OtzariaLinks.buildLink(for: entry(id, type, source, index)), expected)
        }
    }

    func testHebrewSlugIsPercentEncoded() {
        XCTAssertEqual(OtzariaLinks.buildLink(for: entry(5, "TextBook", "d:ספרים")),
                       "otzaria://open/book/5?source=db:%D7%A1%D7%A4%D7%A8%D7%99%D7%9D&index=0")
    }

    func testNotLinkable() {
        XCTAssertNil(OtzariaLinks.buildLink(for: entry(nil)))
        XCTAssertNil(OtzariaLinks.buildLink(for: entry(0)))
        XCTAssertNil(OtzariaLinks.buildLink(for: entry(-1)))
        XCTAssertNil(OtzariaLinks.buildLink(for: entry(3, "ExternalLibraryBook")))
        XCTAssertNil(OtzariaLinks.buildLink(for: entry(3, "WeirdBook")))
        XCTAssertNil(OtzariaLinks.buildLink(for: entry(3, "TextBook", "x:strange")))
        XCTAssertNil(OtzariaLinks.buildLink(for: entry(3, "TextBook", "d:bad slug!")))
        XCTAssertNil(OtzariaLinks.buildLink(for: entry(3, "TextBook", "d:")))
    }

    func testSearchLink() {
        XCTAssertEqual(OtzariaLinks.buildSearchLink(query: "שלום עולם", mode: "exact"),
                       "otzaria://open/search?q=%D7%A9%D7%9C%D7%95%D7%9D%20%D7%A2%D7%95%D7%9C%D7%9D&mode=exact")
        XCTAssertEqual(OtzariaLinks.buildSearchLink(query: "a&b=c", mode: nil), "otzaria://open/search?q=a%26b%3Dc")
        XCTAssertEqual(OtzariaLinks.buildSearchLink(query: "x", mode: "bogus"), "otzaria://open/search?q=x")
        XCTAssertTrue(OtzariaLinks.buildSearchLink(query: "x", mode: "Smart").hasSuffix("&mode=smart"))
        XCTAssertTrue(OtzariaLinks.buildSearchLink(query: "x", mode: " fuzzy ").hasSuffix("&mode=fuzzy"))
        XCTAssertEqual(OtzariaLinks.buildSearchLink(query: "a-b_c.d~e", mode: nil), "otzaria://open/search?q=a-b_c.d~e")
    }

    func testVeryLongQueryJustOpensSearch() {
        XCTAssertEqual(OtzariaLinks.buildSearchLink(query: String(repeating: "א", count: 2000), mode: "exact"), "otzaria://open/search")
    }

    func testBookOpenLink() {
        XCTAssertEqual(OtzariaLinks.buildBookOpenLink(bookId: 12), "otzaria://open/book/12")
        XCTAssertNil(OtzariaLinks.buildBookOpenLink(bookId: 0))
    }

    func testSlugValidation() {
        XCTAssertTrue(OtzariaLinks.isValidSlug("my-lib_1.2"))
        XCTAssertTrue(OtzariaLinks.isValidSlug("ספרים"))
        XCTAssertFalse(OtzariaLinks.isValidSlug(""))
        XCTAssertFalse(OtzariaLinks.isValidSlug("a b"))
        XCTAssertFalse(OtzariaLinks.isValidSlug("a\n"))
        XCTAssertFalse(OtzariaLinks.isValidSlug(String(repeating: "a", count: 65)))
        XCTAssertTrue(OtzariaLinks.isValidSlug(String(repeating: "a", count: 64)))
    }

    func testSafeUri() {
        XCTAssertTrue(OtzariaLinks.isSafeOtzariaUri("otzaria://open/book/1?index=2"))
        XCTAssertTrue(OtzariaLinks.isSafeOtzariaUri("OTZARIA://open/search?q=%D7%90"))
        for bad in ["otzaria://open/plugin/x", "otzaria://open/%70lugin/x", "otzaria://open/../x", "otzaria://install/x",
                    "http://x", "otzaria://open/book/1 2", "otzaria://open/book/1\"", "otzaria://open/a/./b", ""] {
            XCTAssertFalse(OtzariaLinks.isSafeOtzariaUri(bad), bad)
        }
        XCTAssertFalse(OtzariaLinks.isSafeOtzariaUri(nil))
        XCTAssertFalse(OtzariaLinks.isSafeOtzariaUri("otzaria://open/" + String(repeating: "a", count: 9000)))
    }
}
