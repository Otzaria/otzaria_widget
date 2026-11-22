import Foundation
import KosherSwift

/// Otzaria's holiday / special-day labels: a port of `CalendarDayCell._calcJewishEvents` on top of the library formatter,
/// with the remaps needed to match kosher_dart 2.0.20 ('שושן פורים' -> 'פורים שושן', 'אסרו חג' dropped, parsha 'נצבים' -> 'ניצבים').
enum HolidayLabels {
    private static let letters = ["", "א", "ב", "ג", "ד", "ה", "ו", "ז", "ח"]

    private static func letter(_ n: Int) -> String { n > 0 && n < letters.count ? letters[n] : "" }

    private static func newFormatter() -> HebrewDateFormatter {
        let f = HebrewDateFormatter()
        f.hebrewFormat = true
        return f
    }

    /// Holiday labels of the day in Otzaria's order, de-duplicated. The weekly parsha is not included (see `parasha`).
    static func calc(_ jc: JewishCalendar) -> [String] {
        let hdf = newFormatter()
        var l: [String] = []

        let yomTov = hdf.formatYomTov(jewishCalendar: jc)
        if !yomTov.isEmpty {
            for part in yomTov.split(separator: ",", omittingEmptySubsequences: false) {
                let label = remapYomTov(part.trimmingCharacters(in: .whitespaces))
                if !label.isEmpty { l.append(label) }
            }
        }

        if jc.isRoshChodesh() && !l.contains("ראש חודש") { l.append("ר\"ח") }

        let index = jc.getYomTovIndex()
        let day = jc.getJewishDayOfMonth()

        if index == JewishCalendar.CHOL_HAMOED_SUCCOS || index == JewishCalendar.CHOL_HAMOED_PESACH {
            l.removeAll { $0.contains("חול המועד") }
            let dayOfCholHamoed = day - (jc.inIsrael ? 15 : 16)
            let name = index == JewishCalendar.CHOL_HAMOED_PESACH ? "פסח" : "סוכות"
            l.append("\(letter(dayOfCholHamoed)) דחוה\"מ \(name)")
        }

        if index == JewishCalendar.PESACH && day == 21 {
            l.removeAll { $0 == "פסח" }
            l.append("שביעי של פסח")
        }

        if index == JewishCalendar.CHANUKAH {
            l.removeAll { $0.contains("חנוכה") }
            let d = jc.getDayOfChanukah()
            if d != -1 { l.append("נר \(letter(d)) דחנוכה") }
        }

        if index == JewishCalendar.HOSHANA_RABBA {
            l.append((jc.inIsrael ? "ו" : "ה") + "' דחוה\"מ")
        }

        if jc.getJewishMonth() == JewishCalendar.TISHREI {
            if day == 22 {
                if !l.contains("שמיני עצרת") { l.append("שמיני עצרת") }
                if jc.inIsrael && !l.contains("שמחת תורה") { l.append("שמחת תורה") }
            }
            if day == 23 && !jc.inIsrael && !l.contains("שמחת תורה") { l.append("שמחת תורה") }
        }

        var seen = Set<String>()
        return l.filter { seen.insert($0).inserted }
    }

    private static func remapYomTov(_ label: String) -> String {
        switch label {
        case "אסרו חג": return ""
        case "שושן פורים": return "פורים שושן"
        case "שושן פורים קטן": return "פורים שושן קטן"
        default: return label
        }
    }

    /// Parsha of the given Shabbat, "" when that Shabbat has none (Yom Tov / Chol HaMoed). Double parshiyot are space-joined.
    static func parashaOfShabbat(_ shabbat: JewishCalendar) -> String {
        let s = newFormatter().formatParshah(jewishCalendar: shabbat)
        return s.isEmpty ? "" : s.replacingOccurrences(of: "נצבים", with: "ניצבים")
    }

    /// The Shabbat on or after the date; nil when it has no regular parsha.
    static func parasha(_ jc: JewishCalendar, date: CivilDate) -> String? {
        let dow = date.dayOfWeek
        let daysUntilShabbat = dow == 7 ? 0 : 7 - dow
        let shabbat = daysUntilShabbat == 0
            ? jc : HebrewCalendarArithmetic.jewishCalendar(for: date.addingDays(daysUntilShabbat), inIsrael: jc.inIsrael)
        let p = parashaOfShabbat(shabbat)
        return p.isEmpty ? nil : p
    }
}
