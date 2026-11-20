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

    static func yerushalmi(_ jc: JewishCalendar) -> String? {
        guard let daf = jc.getDafYomiYerushalmi() else { return nil }
        return "\(daf.getYerushalmiMasechta()) \(HebrewNumbers.letters(daf.getDaf()))"
    }
}
