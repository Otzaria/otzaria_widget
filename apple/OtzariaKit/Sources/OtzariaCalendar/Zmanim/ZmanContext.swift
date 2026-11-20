import Foundation
import KosherSwift

/// Instant in epoch milliseconds. Whole-second sun events mirror kosher_dart, which Otzaria uses.
typealias Ms = Int64

/// One (city, civil date) computation context. KosherSwift supplies the astronomical primitives (UTC hours of a solar
/// event); instants and shaah-zmanis arithmetic are rebuilt here the way kosher_dart does them, so results match Otzaria to the second.
final class ZmanContext {
    static let kosherDartEarthRadiusKm = 6356.9

    let city: CityInfo
    let date: CivilDate
    let zone: CityZone
    let calendar: ComprehensiveZmanimCalendar

    private(set) lazy var jewish = HebrewCalendarArithmetic.jewishCalendar(for: date, inIsrael: city.isInIsrael)
    /// The same city for date + 1 (chatzos layla needs tomorrow's sunrise).
    private(set) lazy var tomorrow = ZmanContext(city: city, date: date.addingDays(1))

    init(city: CityInfo, date: CivilDate) {
        self.city = city
        self.date = date
        zone = CityZone.resolve(city)
        // Otzaria: setElevation(elevation > 0 ? elevation : 0) - negative elevations (Tiberias) become 0.
        let geo = GeoLocation(locationName: city.name, latitude: city.latitude, longitude: city.longitude,
                              elevation: city.elevation > 0 ? city.elevation : 0, timeZone: zone.timeZone)
        calendar = ComprehensiveZmanimCalendar(geoLocation: geo)
        calendar.localDate = LocalDate(year: date.year, month: date.month, day: date.day)
        // kosher_dart parity: fixed 16' solar radius, earth radius 6356.9 km.
        calendar.astronomicalCalculator.useApparentSolarRadius = false
        calendar.astronomicalCalculator.earthRadius = Self.kosherDartEarthRadiusKm
    }

    // MARK: base instants (UseElevation = false: "based on elevation setting" means sea level)

    private(set) lazy var seaLevelSunrise: Ms? = instant(calendar.getUTCSeaLevelSunrise(zenith: 90), .sunrise)
    private(set) lazy var seaLevelSunset: Ms? = instant(calendar.getUTCSeaLevelSunset(zenith: 90), .sunset)
    /// Elevation-adjusted sunrise / sunset (kosher_dart getSunrise / getSunset).
    private(set) lazy var sunrise: Ms? = instant(calendar.getUTCSunrise(zenith: 90), .sunrise)
    private(set) lazy var sunset: Ms? = instant(calendar.getUTCSunset(zenith: 90), .sunset)

    func sunriseByDegrees(_ zenith: Double) -> Ms? { instant(calendar.getUTCSunrise(zenith: zenith), .sunrise) }
    func sunsetByDegrees(_ zenith: Double) -> Ms? { instant(calendar.getUTCSunset(zenith: zenith), .sunset) }

    /// kosher_dart getDateFromTime: hours / minutes / seconds are each truncated from the fractional UTC hour (sub-second dropped).
    func instant(_ time: Double, _ event: AstronomicalCalendar.SolarEvent) -> Ms? {
        if time.isNaN { return nil }
        guard let d = calendar.getDateFromTime(time: time, solarEvent: event) else { return nil }
        let lib = Int64((d.timeIntervalSince1970 * 1000).rounded())
        if time.isInfinite || time < 0 || time >= 24 { return lib - floorMod(lib, 1000) }
        var c = time
        let hours = Int(c); c -= Double(hours)
        c *= 60; let minutes = Int(c); c -= Double(minutes)
        c *= 60; let seconds = Int(c)
        let hms = Int64(hours * 3600 + minutes * 60 + seconds) * 1000
        let baseDay = floorDiv(lib, 86_400_000) * 86_400_000
        var best = baseDay + hms
        for shift in [Int64(-1), 1] {
            let cand = baseDay + shift * 86_400_000 + hms
            if abs(cand - lib) < abs(best - lib) { best = cand }
        }
        return best
    }

    // MARK: kosher_dart arithmetic

    /// getTemporalHour(start, end): (end - start) in epoch milliseconds / 12, as a double.
    static func temporalHourMs(_ start: Ms, _ end: Ms) -> Double { Double(end - start) / 12.0 }

    /// getTimeOffset: start + offset.toInt() milliseconds.
    static func offset(_ start: Ms, _ offsetMs: Double) -> Ms { start + Int64(offsetMs.rounded(.towardZero)) }

    static func shaahZmanisBased(_ start: Ms?, _ end: Ms?, hours: Double) -> Ms? {
        guard let start, let end else { return nil }
        return offset(start, temporalHourMs(start, end) * hours)
    }

    /// Half-day chatzos: midpoint of the two instants.
    static func sunTransit(_ start: Ms?, _ end: Ms?) -> Ms? { shaahZmanisBased(start, end, hours: 6) }

    /// getZmanisBasedOffset (sea-level sunrise / sunset).
    func zmanisBasedOffset(hours: Double) -> Ms? {
        if hours == 0 { return nil }
        guard let sr = seaLevelSunrise, let ss = seaLevelSunset else { return nil }
        let th = Self.temporalHourMs(sr, ss)
        return Self.offset(hours > 0 ? ss : sr, th * hours)
    }

    static func minutes(_ t: Ms?, _ m: Double) -> Ms? {
        guard let t else { return nil }
        return t + Int64((m * 60_000).rounded())
    }
}
