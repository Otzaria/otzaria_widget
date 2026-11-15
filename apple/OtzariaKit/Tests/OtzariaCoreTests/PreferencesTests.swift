import XCTest
@testable import OtzariaCore

final class PreferencesTests: XCTestCase {
    typealias W = HiveTestWriter

    func testDefaultsWhenEmpty() {
        let p = PreferencesReader.map([:])
        XCTAssertEqual(p, OtzariaPreferences())
        XCTAssertEqual(p.selectedCity, "ירושלים")
        XCTAssertEqual(p.calendarType, "combined")
        XCTAssertEqual(p.dayTransition, "sunset")
    }

    func testWhitelistExact() {
        for key in ["key-selected-city", "key-calendar-enabled-zmanim", "key-calendar-type", "key-calendar-day-transition",
                    "key-calendar-twelve-hour-clock", "key-dark-mode", "key-swatch-color", "key-dark-swatch-color",
                    "key-library-path", "key-library-folder-name", "key-databases-path", "sz:anything"] {
            XCTAssertTrue(PreferencesReader.isWhitelisted(key), key)
        }
        for key in ["key-calendar-google-token", "key-password", "email", "key-calendar", "key-library-path2"] {
            XCTAssertFalse(PreferencesReader.isWhitelisted(key), key)
        }
    }

    func testReadsFileWithTypedValues() {
        var w = HiveTestWriter()
        w.put("key-selected-city", W.str("תל אביב"))
        w.put("key-calendar-enabled-zmanim", W.str("[\"alos\",\"\",3,\"shkia\"]"))
        w.put("key-calendar-type", W.str("hebrew"))
        w.put("key-calendar-day-transition", W.str("tzais"))
        w.put("key-calendar-twelve-hour-clock", W.bool(true))
        w.put("key-dark-mode", W.bool(true))
        w.put("key-swatch-color", W.int(4_283_215_696))
        w.put("key-dark-swatch-color", W.str("#ff112233"))
        w.put("key-library-path", W.str("/lib"))
        w.put("key-library-folder-name", W.str(""))
        w.put("sz:tracked_books", W.str("[1]"))
        w.put("sz:not-a-string", W.int(1))
        w.put("secret-key", W.str("nope"))
        let dir = TempDir()
        let p = PreferencesReader().read(path: dir.write("app_preferences.hive", bytes: w.bytes))
        XCTAssertEqual(p.selectedCity, "תל אביב")
        XCTAssertEqual(p.enabledZmanim, ["alos", "shkia"])
        XCTAssertEqual(p.calendarType, "hebrew")
        XCTAssertEqual(p.dayTransition, "tzais")
        XCTAssertTrue(p.twelveHourClock)
        XCTAssertTrue(p.darkMode)
        XCTAssertEqual(p.lightSeedColorArgb, 4_283_215_696)
        XCTAssertNil(p.darkSeedColorArgb, "string colours mean default, like Otzaria")
        XCTAssertEqual(p.libraryPath, "/lib")
        XCTAssertNil(p.libraryFolderName)
        XCTAssertEqual(p.shamorZachorRaw, ["sz:tracked_books": "[1]"])
    }

    func testWrongTypesFallBackToDefaults() {
        let p = PreferencesReader.map([
            "key-selected-city": .int(1), "key-calendar-type": .string("Hebrew"), "key-calendar-day-transition": .string("nope"),
            "key-dark-mode": .string("true"), "key-calendar-enabled-zmanim": .string("{not json"),
        ])
        XCTAssertEqual(p, OtzariaPreferences())
    }

    func testMissingFileGivesDefaults() {
        XCTAssertEqual(PreferencesReader().read(path: "/nonexistent/app_preferences.hive"), OtzariaPreferences())
    }
}
