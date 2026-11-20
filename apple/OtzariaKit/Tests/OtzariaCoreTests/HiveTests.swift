import XCTest
@testable import OtzariaCore

final class HiveTests: XCTestCase {
    typealias W = HiveTestWriter

    private func parse(_ w: HiveTestWriter, filter: (String) -> Bool = { _ in true }) -> HiveParseResult {
        HiveParser.parse(w.bytes, keyFilter: filter)
    }

    func testAllValueTypes() {
        var w = HiveTestWriter()
        w.put("null", W.null())
        w.put("int", W.int(-42))
        w.put("double", W.double(3.5))
        w.put("bool", W.bool(true))
        w.put("str", W.str("שלום"))
        w.put("bytes", W.bytesValue([1, 2, 3]))
        w.put("ints", W.intList([1, 2]))
        w.put("strs", W.strList(["a", "ב"]))
        w.put("list", W.list([W.int(1), W.str("x")]))
        w.put("map", W.map([(W.str("k"), W.int(7)), (W.int(5), W.bool(false))]))
        w.put("date", W.dateMs(1_700_000_000_000))
        w.put(7, W.str("int-key"))
        let r = parse(w)
        XCTAssertTrue(r.isClean)
        XCTAssertEqual(r.frameCount, 12)
        XCTAssertEqual(r.values["null"], .null)
        XCTAssertEqual(r.values["int"], .int(-42))
        XCTAssertEqual(r.values["double"], .double(3.5))
        XCTAssertEqual(r.values["bool"], .bool(true))
        XCTAssertEqual(r.values["str"], .string("שלום"))
        XCTAssertEqual(r.values["bytes"], .bytes([1, 2, 3]))
        XCTAssertEqual(r.values["ints"], .list([.int(1), .int(2)]))
        XCTAssertEqual(r.values["strs"], .list([.string("a"), .string("ב")]))
        XCTAssertEqual(r.values["list"], .list([.int(1), .string("x")]))
        XCTAssertEqual(r.values["map"], .map(["k": .int(7), "5": .bool(false)]))
        XCTAssertEqual(r.values["date"], .date(Date(timeIntervalSince1970: 1_700_000_000)))
        XCTAssertEqual(r.values["7"], .string("int-key"))
    }

    func testLastFrameWinsAndTombstone() {
        var w = HiveTestWriter()
        w.put("a", W.int(1))
        w.put("b", W.int(2))
        w.put("a", W.int(3))
        w.delete("b")
        let r = parse(w)
        XCTAssertEqual(r.values["a"], .int(3))
        XCTAssertNil(r.values["b"])
        XCTAssertEqual(r.tombstoneCount, 1)
    }

    func testKeyFilterNeverMaterializesOtherKeys() {
        var w = HiveTestWriter()
        w.put("key-selected-city", W.str("חיפה"))
        w.put("google-calendar-token", W.str("secret"))
        w.put("sz:tracked_books", W.str("[1]"))
        let r = parse(w, filter: PreferencesReader.isWhitelisted)
        XCTAssertEqual(Set(r.values.keys), ["key-selected-city", "sz:tracked_books"])
    }

    func testTruncatedTailKeepsParsedFrames() {
        var w = HiveTestWriter()
        w.put("a", W.int(1))
        let good = w.bytes.count
        w.put("b", W.str("hello"))
        let r = HiveParser.parse(Array(w.bytes.dropLast(3)))
        XCTAssertEqual(r.values["a"], .int(1))
        XCTAssertNil(r.values["b"])
        XCTAssertFalse(r.isClean)
        XCTAssertEqual(r.parsedBytes, good)
    }

    func testPartialLengthPrefixAtEnd() {
        var w = HiveTestWriter()
        w.put("a", W.int(1))
        w.appendRaw([9, 0])
        let r = parse(w)
        XCTAssertEqual(r.values["a"], .int(1))
        XCTAssertEqual(r.error, "partial length prefix at end of file")
    }

    func testCrcMismatchStopsParsing() {
        var w = HiveTestWriter()
        w.put("a", W.int(1))
        w.put("b", W.int(2))
        var bytes = w.bytes
        bytes[bytes.count - 6] ^= 0xFF // corrupt the second frame's payload
        let r = HiveParser.parse(bytes)
        XCTAssertEqual(r.values["a"], .int(1))
        XCTAssertNil(r.values["b"])
        XCTAssertEqual(r.error, "CRC mismatch")
    }

    func testUnknownAdapterSkippedAndFramingContinues() {
        var w = HiveTestWriter()
        w.put("a", W.int(1))
        w.put("custom", W.ext(40, [1, 2, 3]))
        w.put("a2", W.int(2))
        w.put("a", W.ext(41, [0]))
        let r = parse(w)
        XCTAssertNil(r.values["custom"])
        XCTAssertEqual(r.values["a2"], .int(2))
        XCTAssertNil(r.values["a"], "an unreadable newer value supersedes the old one")
        XCTAssertEqual(r.skippedValueCount, 2)
        XCTAssertTrue(r.isClean)
    }

    func testHugeElementCountDoesNotAllocate() {
        var w = HiveTestWriter()
        w.put("l", [9] + W.u32(0xFFFF_FFF0))
        w.put("ok", W.int(1))
        let r = parse(w)
        XCTAssertNil(r.values["l"])
        XCTAssertEqual(r.values["ok"], .int(1))
    }

    func testIntOutOfRangeAndNaNAreSkipped() {
        var w = HiveTestWriter()
        w.put("big", W.int(1e30))
        w.put("nan", W.int(.nan))
        let r = parse(w)
        XCTAssertTrue(r.values.isEmpty)
        XCTAssertEqual(r.skippedValueCount, 2)
    }

    func testNestingTooDeepIsSkippedNotCrashed() {
        var value = W.int(1)
        for _ in 0..<200 { value = W.list([value]) }
        var w = HiveTestWriter()
        w.put("deep", value)
        w.put("ok", W.int(1))
        let r = parse(w)
        XCTAssertNil(r.values["deep"])
        XCTAssertEqual(r.values["ok"], .int(1))
    }

    func testFuzzNeverCrashes() {
        var w = HiveTestWriter()
        w.put("key-selected-city", W.str("ירושלים"))
        w.put("sz:tracked_books", W.str("[1,2]"))
        w.put("m", W.map([(W.str("k"), W.list([W.int(1)]))]))
        var rng = SystemRandomNumberGenerator()
        for _ in 0..<2000 {
            var bytes = w.bytes
            for _ in 0..<Int.random(in: 1...6, using: &rng) {
                bytes[Int.random(in: 0..<bytes.count, using: &rng)] = UInt8.random(in: 0...255, using: &rng)
            }
            if Bool.random(using: &rng) { bytes = Array(bytes.prefix(Int.random(in: 0...bytes.count, using: &rng))) }
            _ = HiveParser.parse(bytes)
        }
        for _ in 0..<500 {
            _ = HiveParser.parse((0..<Int.random(in: 0...200, using: &rng)).map { _ in UInt8.random(in: 0...255, using: &rng) })
        }
    }

    func testReaderMissingFileAndFileReplacement() {
        let dir = TempDir()
        let reader = HiveReader()
        let missing = reader.readDetailed(path: dir.file("none.hive"))
        XCTAssertEqual(missing.error, "file not readable")
        XCTAssertTrue(missing.values.isEmpty)

        var w = HiveTestWriter()
        w.put("a", W.int(1))
        let p = dir.write("box.hive", bytes: w.bytes)
        XCTAssertEqual(reader.read(path: p, keyFilter: { _ in true })["a"], .int(1))
        // Compaction replaces the file atomically: the next read sees the new content.
        var w2 = HiveTestWriter()
        w2.put("a", W.int(2))
        let tmp = dir.write("box.hivec", bytes: w2.bytes)
        _ = try? FileManager.default.replaceItemAt(URL(fileURLWithPath: p), withItemAt: URL(fileURLWithPath: tmp))
        XCTAssertEqual(reader.read(path: p, keyFilter: { _ in true })["a"], .int(2))
    }

    func testReaderDoesNotModifyFile() throws {
        let dir = TempDir()
        var w = HiveTestWriter()
        w.put("a", W.int(1))
        w.appendRaw([1, 2, 3])
        let p = dir.write("box.hive", bytes: w.bytes)
        let before = try Data(contentsOf: URL(fileURLWithPath: p))
        _ = HiveReader().readDetailed(path: p)
        XCTAssertEqual(try Data(contentsOf: URL(fileURLWithPath: p)), before)
    }

    func testCrc32KnownVector() {
        let bytes = Array("123456789".utf8)
        XCTAssertEqual(Crc32.compute(bytes, range: 0..<bytes.count), 0xCBF4_3926)
    }
}
