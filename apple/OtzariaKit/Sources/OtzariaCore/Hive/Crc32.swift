/// Standard CRC-32 (IEEE 802.3 / zlib; reflected polynomial 0xEDB88320).
enum Crc32 {
    private static let table: [UInt32] = (0..<256).map { i -> UInt32 in
        var c = UInt32(i)
        for _ in 0..<8 { c = (c & 1) != 0 ? 0xEDB8_8320 ^ (c >> 1) : c >> 1 }
        return c
    }

    static func compute(_ data: UnsafeBufferPointer<UInt8>) -> UInt32 {
        var c: UInt32 = 0xFFFF_FFFF
        for b in data { c = table[Int((c ^ UInt32(b)) & 0xFF)] ^ (c >> 8) }
        return c ^ 0xFFFF_FFFF
    }

    static func compute(_ data: [UInt8], range: Range<Int>) -> UInt32 {
        data.withUnsafeBufferPointer { compute(UnsafeBufferPointer(rebasing: $0[range])) }
    }
}
