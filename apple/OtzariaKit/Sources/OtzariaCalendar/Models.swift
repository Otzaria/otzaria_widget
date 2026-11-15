import Foundation

/// A city of Otzaria's table. `name` is the Hebrew key Otzaria stores in `key-selected-city`.
public struct CityInfo: Hashable, Sendable {
    public let name: String
    public let country: String
    public let latitude: Double
    public let longitude: Double
    public let elevation: Double
    public let timeZoneId: String
    public let isInIsrael: Bool
    public let candleLightingMinutes: Int

    public init(name: String, country: String, latitude: Double, longitude: Double, elevation: Double,
                timeZoneId: String, isInIsrael: Bool, candleLightingMinutes: Int = 30) {
        self.name = name
        self.country = country
        self.latitude = latitude
        self.longitude = longitude
        self.elevation = elevation
        self.timeZoneId = timeZoneId
        self.isInIsrael = isInIsrael
        self.candleLightingMinutes = candleLightingMinutes
    }
}

public struct ZmanDefinition: Hashable, Sendable {
    public let id: String
    public let title: String
    public let description: String?
}

/// One computed zman. `time == nil` means it does not occur that day. `displayText` is Otzaria's text
/// (24h "HH:mm." wrapped in LTR isolates; kidush levana: the full Hebrew night label).
public struct ZmanTime: Hashable, Sendable {
    public let id: String
    public let title: String
    public let time: Date?
    public let displayText: String?
}

public struct DayInfo: Hashable, Sendable {
    public let date: CivilDate
    /// e.g. "כ״ב בחשון תשפ״ז" style: "ג׳ כסלו תשפ״ז".
    public let hebrewDate: String
    public let hebrewDayOfWeek: String
    /// "dd/MM/yyyy".
    public let gregorianDate: String
    public let parasha: String?
    public let holidays: [String]
    public let dafYomiBavli: String?
    public let dafYomiYerushalmi: String?
    public let omer: String?
    public let candleLighting: Date?
    public let havdalah: Date?
    public let isShabbat: Bool
    public let isYomTov: Bool
}

/// Everything Otzaria's registry knows about one zman.
public struct ZmanRegistryEntry: Hashable, Sendable {
    public let id: String
    /// General name, e.g. "עלות השחר".
    public let title: String
    /// Method detail, e.g. "72 דק׳ (מעלות)"; empty when none.
    public let subtitle: String
    public let category: String
    /// First line of Otzaria's long Hebrew explanation.
    public let description: String
    public let defaultEnabled: Bool
    /// Kidush levana entries are shown as a Hebrew night label instead of a clock time.
    public let showHebrewDate: Bool
    public let pairId: String?
    public let pairLabel: String

    /// Otzaria's `fullName`: "title — subtitle".
    public var fullName: String { subtitle.isEmpty ? title : "\(title) — \(subtitle)" }
}
