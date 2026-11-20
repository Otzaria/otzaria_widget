import Foundation

/// Omer counting text as Otzaria builds it (calendar_times_panel.dart). Spellings are Otzaria's (שלשה / חמשה / ששה).
public enum OmerText {
    private static let ones = [
        "", "יום אחד", "שני ימים", "שלשה ימים", "ארבעה ימים", "חמשה ימים", "ששה ימים", "שבעה ימים", "שמונה ימים", "תשעה ימים", "עשרה ימים",
        "אחד עשר יום", "שנים עשר יום", "שלשה עשר יום", "ארבעה עשר יום", "חמשה עשר יום", "ששה עשר יום", "שבעה עשר יום", "שמונה עשר יום", "תשעה עשר יום",
    ]
    private static let tens = ["", "", "עשרים", "שלשים", "ארבעים"]
    private static let onesSimple = ["", "אחד", "שנים", "שלשה", "ארבעה", "חמשה", "ששה", "שבעה", "שמונה", "תשעה"]
    private static let oneToNine = ["", "אחד", "שני", "שלשה", "ארבעה", "חמשה", "ששה", "שבעה", "שמונה", "תשעה"]

    /// "היום שלשה ושלשים יום שהם ארבעה שבועות וחמשה ימים בעומר" for day 1...49; nil otherwise.
    public static func build(_ day: Int) -> String? {
        if day < 1 || day > 49 { return nil }
        let total = dayCount(day)
        let weeks = day / 7
        let extra = day % 7
        if weeks == 0 { return "היום \(total) בעומר" }
        let weeksText = weekCount(weeks)
        return extra == 0
            ? "היום \(total) שהם \(weeksText) בעומר"
            : "היום \(total) שהם \(weeksText) ו\(dayCount(extra)) בעומר"
    }

    private static func dayCount(_ day: Int) -> String {
        if day <= 0 || day > 49 { return "יום \(day)" }
        if day < 20 { return ones[day] }
        let t = tens[day / 10]
        let o = day % 10
        return o == 0 ? "\(t) יום" : "\(onesSimple[o]) ו\(t) יום"
    }

    private static func weekCount(_ weeks: Int) -> String {
        switch weeks {
        case ..<1: return ""
        case 1: return "שבוע אחד"
        case 2: return "שני שבועות"
        default: return "\(oneToNine[weeks]) שבועות"
        }
    }
}
