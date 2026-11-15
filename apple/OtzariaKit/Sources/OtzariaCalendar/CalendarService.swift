import Foundation
import KosherSwift

/// Otzaria-consistent Hebrew calendar + zmanim engine. Stateless and thread-safe; every call builds its own calendars.
/// Machine formatting is always Gregorian / locale independent.
public final class CalendarService: Sendable {
    public static let dayTransitionSunset = "sunset"
    public static let dayTransitionTzais = "tzais"
    public static let dayTransitionRabbeinuTam = "rabbeinuTam"
    public static let dayTransitionMidnight = "midnight"

    public init() {}

    public var cities: [CityInfo] { CityTable.all }

    public func findCity(_ name: String?) -> CityInfo? { CityTable.find(name) }

    /// All zmanim Otzaria knows, in Otzaria's display order, with Hebrew titles.
    public var zmanimRegistry: [ZmanDefinition] { ZmanimRegistry.definitions }

    public var defaultEnabledZmanim: [String] { ZmanimRegistry.defaultEnabled }

    /// Full registry detail (subtitle, category, pairing, default flag).
    public var zmanimRegistryDetails: [ZmanRegistryEntry] { ZmanimRegistry.all }

    /// The city's time zone as used by all calculations (includes the tzdata 2026 Alberta / British Columbia correction).
    public func zone(for city: CityInfo) -> CityZone { CityZone.resolve(city) }

    public func utcOffsetSeconds(at instant: Date, city: CityInfo) -> Int { zone(for: city).offsetSeconds(at: instant) }

    // MARK: day info

    public func dayInfo(for date: CivilDate, city: CityInfo) -> DayInfo {
        guard ZmanimEngine.supportedYears.contains(date.year) else {
            // far outside the supported range: an empty-but-valid answer
            return DayInfo(date: date, hebrewDate: "", hebrewDayOfWeek: "", gregorianDate: HebrewDateText.gregorian(date), parasha: nil,
                           holidays: [], dafYomiBavli: nil, dafYomiYerushalmi: nil, omer: nil, candleLighting: nil, havdalah: nil,
                           isShabbat: false, isYomTov: false)
        }
        let jc = JewishCalendar(localDate: LocalDate(year: date.year, month: date.month, day: date.day), inIsrael: city.isInIsrael)
        let dow = date.dayOfWeek

        var candle: Date?
        var havdalah: Date?
        let erev = ZmanCalculator.isErevShabbosOrYomTov(jc)
        let motzei = ZmanCalculator.isMotzeiShabbos(jc) || ZmanCalculator.isMotzeiYomTov(jc)
        if erev || motzei {
            let ctx = ZmanContext(city: city, date: date)
            if let sunset = ctx.sunset {
                if erev { candle = Self.date(sunset - Int64(city.candleLightingMinutes) * 60_000) }
                // Otzaria's default motzei (motzeiShabbos / motzeiYomTov "רגיל") = sunset + 34
                if motzei { havdalah = Self.date(sunset + 34 * 60_000) }
            }
        }

        let omerDay = jc.getDayOfOmer()
        let parts = HebrewDateText.Parts(jc)
        return DayInfo(
            date: date,
            hebrewDate: HebrewDateText.format(parts),
            hebrewDayOfWeek: HebrewDateText.dayOfWeekName(dow),
            gregorianDate: HebrewDateText.gregorian(date),
            parasha: HolidayLabels.parasha(jc, date: date),
            holidays: HolidayLabels.calc(jc),
            dafYomiBavli: DafYomiText.bavli(jc),
            dafYomiYerushalmi: DafYomiText.yerushalmi(jc),
            omer: omerDay == -1 ? nil : OmerText.build(omerDay),
            candleLighting: candle,
            havdalah: havdalah,
            isShabbat: dow == 7,
            isYomTov: jc.isYomTovAssurBemelacha())
    }

    /// The Hebrew date chip in Otzaria's calendar-header style ("ג כסלו ה׳תשפ״ז").
    public func calendarHeaderDate(for date: CivilDate, city: CityInfo) -> String {
        guard ZmanimEngine.supportedYears.contains(date.year) else { return "" }
        let jc = JewishCalendar(localDate: LocalDate(year: date.year, month: date.month, day: date.day), inIsrael: city.isInIsrael)
        return HebrewDateText.formatCalendarHeader(HebrewDateText.Parts(jc))
    }

    // MARK: zmanim

    /// Zmanim for the date in registry order, only those in `ids`. `time == nil` means it does not occur that day.
    public func zmanim(for date: CivilDate, city: CityInfo, ids: [String]) -> [ZmanTime] {
        ZmanimEngine.compute(date: date, city: city, ids: ids)
    }

    /// Display text for a zman in the city's zone. Kidush levana keeps Otzaria's full Hebrew night label.
    public func displayText(for zman: ZmanTime, city: CityInfo, twelveHour: Bool = false) -> String? {
        guard let time = zman.time else { return nil }
        if ZmanimRegistry.find(zman.id)?.showHebrewDate == true { return zman.displayText }
        return ZmanTimeFormatter.format(time, zone: zone(for: city), twelveHour: twelveHour)
    }

    /// Plain "h:mm" / "HH:mm" for widgets (minutes truncated like Otzaria, no seconds marker or bidi isolates).
    public func plainTime(_ instant: Date, city: CityInfo, twelveHour: Bool) -> String {
        let s = zone(for: city).local(atMs: ZmanTimeFormatter.ms(instant)).secondOfDay
        let h = s / 3600
        let m = s / 60 % 60
        let mm = m < 10 ? "0\(m)" : "\(m)"
        if !twelveHour { return (h < 10 ? "0\(h)" : "\(h)") + ":" + mm }
        return "\(h % 12 == 0 ? 12 : h % 12):\(mm)" + (h < 12 ? " AM" : " PM")
    }

    // MARK: effective date

    /// Port of `resolveCalendarDayForTransition`: the civil day of `now` in the city's zone, advanced by one day once the
    /// transition instant of that civil day has passed (sunset = elevation-adjusted sunset, tzais = 8.5 degrees,
    /// rabbeinuTam = sunset + 72 min, midnight = never). Unknown values behave as "sunset".
    public func effectiveDate(now: Date, city: CityInfo, dayTransition: String) -> CivilDate {
        let nowMs = ZmanTimeFormatter.ms(now)
        let civilToday = zone(for: city).local(atMs: nowMs).date
        if dayTransition == Self.dayTransitionMidnight { return civilToday }
        guard let transition = transitionInstant(civilToday, city, dayTransition), nowMs >= transition else { return civilToday }
        return civilToday.addingDays(1)
    }

    /// The instant at which the Hebrew day changes for `civilDate` (nil for midnight / uncomputable).
    public func dayTransitionInstant(civilDate: CivilDate, city: CityInfo, dayTransition: String) -> Date? {
        if dayTransition == Self.dayTransitionMidnight { return nil }
        return transitionInstant(civilDate, city, dayTransition).map(Self.date)
    }

    private func transitionInstant(_ civilDate: CivilDate, _ city: CityInfo, _ mode: String) -> Ms? {
        guard ZmanimEngine.supportedYears.contains(civilDate.year) else { return nil }
        let ctx = ZmanContext(city: city, date: civilDate)
        switch mode {
        case Self.dayTransitionTzais: return ctx.sunsetByDegrees(98.5)
        case Self.dayTransitionRabbeinuTam: return ZmanContext.minutes(ctx.sunset, 72)
        default: return ctx.sunset
        }
    }

    /// Otzaria's "אור ל" header prefix: true while `now` is before (elevation-adjusted sunrise - 90 min) of the effective "today".
    public func shouldShowOhrPrefix(now: Date, city: CityInfo, dayTransition: String) -> Bool {
        let today = effectiveDate(now: now, city: city, dayTransition: dayTransition)
        guard ZmanimEngine.supportedYears.contains(today.year), let sunrise = ZmanContext(city: city, date: today).sunrise else { return false }
        return ZmanTimeFormatter.ms(now) < sunrise - 90 * 60_000
    }

    static func date(_ ms: Ms) -> Date { Date(timeIntervalSince1970: Double(ms) / 1000) }
}
