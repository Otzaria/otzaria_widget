import Foundation

/// The zmanim of the day a widget shows: today's (by the Hebrew-day switch), or tomorrow's once today's are all over.
public struct ZmanimDay: Sendable {
    public let city: CityInfo
    public let date: CivilDate
    /// True when `date` is after the city's civil today.
    public let isTomorrow: Bool
    /// Zmanim whose instant falls on `date`, sorted by time.
    public let times: [ZmanTime]
    /// Index in `times` of the first zman at or after `now` (`times.count` when none is left).
    public let nextIndex: Int
    /// Zmanim whose instant falls on another civil date (kidush levana): shown after the list, never "next".
    public let otherDays: [ZmanTime]
    /// Selected zmanim that do not occur on this date.
    public let untimed: [ZmanTime]

    public var next: ZmanTime? { nextIndex < times.count ? times[nextIndex] : nil }
}

extension CalendarService {
    /// "Next zman" logic of the zmanim widget (ZmanimBuilder.Compute).
    public func zmanimDay(now: Date, city: CityInfo, ids: [String], dayTransition: String) -> ZmanimDay {
        var date = effectiveDate(now: now, city: city, dayTransition: dayTransition)
        var loaded = loadZmanimDay(date, city, ids)
        let nowMs = ZmanTimeFormatter.ms(now)
        if !loaded.times.isEmpty, loaded.times.allSatisfy({ ZmanTimeFormatter.ms($0.time!) <= nowMs }) {
            let tomorrow = loadZmanimDay(date.addingDays(1), city, ids)
            if !tomorrow.times.isEmpty {
                loaded = tomorrow
                date = date.addingDays(1)
            }
        }
        let civilToday = zone(for: city).local(atMs: nowMs).date
        let nextIndex = loaded.times.firstIndex { ZmanTimeFormatter.ms($0.time!) >= nowMs } ?? loaded.times.count
        return ZmanimDay(city: city, date: date, isTomorrow: date > civilToday, times: loaded.times, nextIndex: nextIndex,
                         otherDays: loaded.other, untimed: loaded.untimed)
    }

    private func loadZmanimDay(_ date: CivilDate, _ city: CityInfo, _ ids: [String]) -> (times: [ZmanTime], other: [ZmanTime], untimed: [ZmanTime]) {
        let z = zone(for: city)
        var times: [ZmanTime] = [], other: [ZmanTime] = [], untimed: [ZmanTime] = []
        for t in zmanim(for: date, city: city, ids: ids) {
            guard let instant = t.time else { untimed.append(t); continue }
            if z.local(atMs: ZmanTimeFormatter.ms(instant)).date != date { other.append(t) } else { times.append(t) }
        }
        let byTime: (ZmanTime, ZmanTime) -> Bool = { $0.time! < $1.time! }
        return (times.sorted(by: byTime), other.sorted(by: byTime), untimed)
    }

    /// The next `count` moments (strictly after `now`, ascending, de-duplicated) at which a widget's content can change:
    /// each selected zman, the Hebrew-day switch, the civil midnight in the city, and (optionally) the "אור ל" boundary.
    public func refreshMoments(after now: Date, count: Int, city: CityInfo, ids: [String], dayTransition: String,
                               includeOhrPrefixBoundary: Bool = false) -> [Date] {
        guard count > 0 else { return [] }
        let z = zone(for: city)
        let nowMs = ZmanTimeFormatter.ms(now)
        let civilToday = z.local(atMs: nowMs).date
        var found = Set<Ms>()
        var day = civilToday.addingDays(-1) // yesterday's late zmanim (chatzos layla, after-midnight events) may still be ahead
        var scanned = 0
        // each civil day yields several moments; stop once enough lie ahead and the scanned day is past the Nth moment
        while scanned < 400 {
            var candidates: [Ms] = []
            candidates.append(z.startOfDayMs(day.addingDays(1)))
            if let t = dayTransitionInstant(civilDate: day, city: city, dayTransition: dayTransition) { candidates.append(ZmanTimeFormatter.ms(t)) }
            if includeOhrPrefixBoundary, ZmanimEngine.supportedYears.contains(day.year),
               let sr = ZmanContext(city: city, date: day).sunrise { candidates.append(sr - 90 * 60_000) }
            for t in zmanim(for: day, city: city, ids: ids) { if let i = t.time { candidates.append(ZmanTimeFormatter.ms(i)) } }
            for c in candidates where c > nowMs { found.insert(c) }
            scanned += 1
            if scanned >= 2, found.count >= count, z.startOfDayMs(day) > nowMs { break }
            day = day.addingDays(1)
        }
        return found.sorted().prefix(count).map(Self.date)
    }
}
