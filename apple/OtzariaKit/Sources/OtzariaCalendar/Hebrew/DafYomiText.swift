import Foundation
import KosherSwift

/// Daf Yomi labels. Bavli exactly as Otzaria (formatAmud): masechta + page in gematria WITHOUT geresh/gershayim ("בכורות נו").
/// Yerushalmi is not shown by Otzaria; same style, nil when unavailable.
enum DafYomiText {
    private static let bavliStart = CivilDate(year: 1923, month: 9, day: 11).epochDay
    private static let shekalimChange = CivilDate(year: 1975, month: 6, day: 24).epochDay
    private static let blattPerMasechta = [
        64, 157, 105, 121, 22, 88, 56, 40, 35, 31, 32, 29, 27, 122, 112, 91, 66, 49, 90, 82,
        119, 119, 176, 113, 24, 49, 76, 14, 120, 110, 142, 61, 34, 34, 28, 22, 4, 9, 5, 73,
    ]

    /// Same cycle arithmetic as the library, on plain day numbers: the library derives its day numbers from a
    /// device-time-zone `Calendar`, which is one day off in zones at UTC+12 and beyond.
    static func bavli(date: CivilDate) -> String? {
        let day = date.epochDay
        if day < bavliStart { return nil } // before the first cycle (11 Sep 1923)
        let cycleNo: Int
        let dafNo: Int
        if day >= shekalimChange {
            cycleNo = 8 + (day - shekalimChange) / 2711
            dafNo = (day - shekalimChange) % 2711
        } else {
            cycleNo = 1 + (day - bavliStart) / 2702
            dafNo = (day - bavliStart) % 2702
        }
        var blatt = blattPerMasechta
        blatt[4] = cycleNo <= 7 ? 13 : 22 // Shekalim had 13 dapim in the old cycles
        var total = 0
        for (masechta, count) in blatt.enumerated() {
            total += count - 1
            guard dafNo < total else { continue }
            var page = 1 + count - (total - dafNo)
            switch masechta {
            case 36: page += 21 // Meilah
            case 37: page += 24 // Kinnim
            case 38: page += 32 // Tamid
            default: break
            }
            let daf = Daf(masechtaNumber: masechta, daf: page)
            return "\(daf.getMasechta()) \(HebrewNumbers.letters(page))"
        }
        return nil
    }

    private static let yerushalmiStart = CivilDate(year: 1980, month: 2, day: 2).epochDay
    private static let yerushalmiShasDafs = 1554
    private static let yerushalmiBlatt = [
        68, 37, 34, 44, 31, 59, 26, 33, 28, 20, 13, 92, 65, 71, 22, 22, 42, 26, 26, 33, 34, 22,
        19, 85, 72, 47, 40, 47, 54, 48, 44, 37, 34, 44, 9, 57, 37, 19, 13,
    ]

    /// The library's Yerushalmi cycle arithmetic, with Yom Kippur / Tisha B'Av taken from `HebrewDate` instead of the OS calendar.
    static func yerushalmi(date: CivilDate) -> String? {
        let day = date.epochDay
        if day < yerushalmiStart || isYomKippurOrTishaBav(day) { return nil }
        var cycleStart = yerushalmiStart
        var nextCycle = nextYerushalmiCycle(after: cycleStart)
        while day >= nextCycle {
            cycleStart = nextCycle
            nextCycle = nextYerushalmiCycle(after: cycleStart)
        }
        var total = day - cycleStart - specialDays(from: cycleStart, to: day)
        for (masechta, blatt) in yerushalmiBlatt.enumerated() {
            if total < blatt {
                let daf = Daf(masechtaNumber: masechta, daf: total + 1)
                return "\(daf.getYerushalmiMasechta()) \(HebrewNumbers.letters(daf.getDaf()))"
            }
            total -= blatt
        }
        return nil
    }

    private static func isYomKippurOrTishaBav(_ day: Int) -> Bool {
        let h = HebrewDate(epochDay: day)
        let dow = CivilDate(epochDay: day).dayOfWeek
        return (h.month == 7 && h.day == 10) || (h.month == 5 && ((dow == 1 && h.day == 10) || (dow != 7 && h.day == 9)))
    }

    private static func nextYerushalmiCycle(after start: Int) -> Int {
        var end = start + yerushalmiShasDafs - 1
        var special = specialDays(from: start, to: end)
        while special > 0 {
            let newStart = end + 1
            end += special
            special = specialDays(from: newStart, to: end)
        }
        return end + 1
    }

    /// Days without a daf (Yom Kippur, Tisha B'Av; pushed to Sunday when on Shabbat) from `start` to `end`, both included.
    private static func specialDays(from start: Int, to end: Int) -> Int {
        let firstYear = HebrewDate(epochDay: start).year
        let lastYear = HebrewDate(epochDay: end).year
        guard firstYear <= lastYear else { return 0 }
        var count = 0
        for year in firstYear...lastYear {
            var tishaBav = HebrewDate(year: year, month: 5, day: 9).epochDay
            if CivilDate(epochDay: tishaBav).dayOfWeek == 7 { tishaBav += 1 }
            for special in [HebrewDate(year: year, month: 7, day: 10).epochDay, tishaBav] where (start...end).contains(special) {
                count += 1
            }
        }
        return count
    }
}
