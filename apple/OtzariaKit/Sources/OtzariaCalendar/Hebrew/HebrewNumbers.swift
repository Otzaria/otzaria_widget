import Foundation

/// Gematria exactly as kosher_dart / KosherJava `formatHebrewNumber` (what Otzaria shows):
/// 15 = ט״ו, single letter = letter + geresh, several letters = gershayim before the last.
public enum HebrewNumbers {
    public static let geresh: Character = "\u{05F3}"
    public static let gershayim: Character = "\u{05F4}"

    private static let ones = ["", "א", "ב", "ג", "ד", "ה", "ו", "ז", "ח", "ט"]
    private static let tens = ["", "י", "כ", "ל", "מ", "נ", "ס", "ע", "פ", "צ"]
    private static let hundreds = ["", "ק", "ר", "ש", "ת"]

    /// Letters only, no geresh/gershayim (56 = נו, 15 = טו). Thousands are dropped; non-positive gives "".
    public static func letters(_ number: Int) -> String {
        if number <= 0 { return "" }
        var n = number >= 1000 ? number % 1000 : number
        if n == 0 { return "" }
        var s = ""
        while n >= 400 { s += "ת"; n -= 400 }
        if n >= 100 { s += hundreds[n / 100]; n %= 100 }
        if n == 15 { s += "טו" } else if n == 16 { s += "טז" } else {
            if n >= 10 { s += tens[n / 10]; n %= 10 }
            if n > 0 { s += ones[n] }
        }
        return s
    }

    /// With geresh/gershayim: 3 = ג׳, 15 = ט״ו, 5787 = תשפ״ז.
    public static func format(_ number: Int) -> String {
        let chars = Array(letters(number))
        switch chars.count {
        case 0: return ""
        case 1: return String(chars) + String(geresh)
        default: return String(chars.dropLast()) + String(gershayim) + String(chars[chars.count - 1])
        }
    }

    /// Hebrew year without the thousands (5787 -> תשפ״ז).
    public static func year(_ year: Int) -> String { format(year) }

    /// Gematria text to number (ignores geresh/gershayim/ASCII quotes); 0 when not a Hebrew numeral.
    public static func parse(_ text: String) -> Int {
        var total = 0
        for ch in text {
            if ch == "'" || ch == "\"" || ch == geresh || ch == gershayim { continue }
            let v: Int
            switch ch {
            case "א": v = 1
            case "ב": v = 2
            case "ג": v = 3
            case "ד": v = 4
            case "ה": v = 5
            case "ו": v = 6
            case "ז": v = 7
            case "ח": v = 8
            case "ט": v = 9
            case "י": v = 10
            case "כ", "ך": v = 20
            case "ל": v = 30
            case "מ", "ם": v = 40
            case "נ", "ן": v = 50
            case "ס": v = 60
            case "ע": v = 70
            case "פ", "ף": v = 80
            case "צ", "ץ": v = 90
            case "ק": v = 100
            case "ר": v = 200
            case "ש": v = 300
            case "ת": v = 400
            default: return 0
            }
            total += v
        }
        return total
    }
}
