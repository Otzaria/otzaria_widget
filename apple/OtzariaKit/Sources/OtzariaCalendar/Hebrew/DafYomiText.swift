import Foundation
import KosherSwift

/// Daf Yomi labels. Bavli exactly as Otzaria (formatAmud): masechta + page in gematria WITHOUT geresh/gershayim ("בכורות נו").
/// Yerushalmi is not shown by Otzaria; same style, nil when unavailable.
public enum DafYomiText {
    static func bavli(_ jc: JewishCalendar) -> String? {
        guard let daf = jc.getDafYomiBavli() else { return nil } // before the first cycle (11 Sep 1923)
        return "\(daf.getMasechta()) \(HebrewNumbers.letters(daf.getDaf()))"
    }

    static func yerushalmi(_ jc: JewishCalendar) -> String? {
        guard let daf = jc.getDafYomiYerushalmi() else { return nil }
        return "\(daf.getYerushalmiMasechta()) \(HebrewNumbers.letters(daf.getDaf()))"
    }
}
