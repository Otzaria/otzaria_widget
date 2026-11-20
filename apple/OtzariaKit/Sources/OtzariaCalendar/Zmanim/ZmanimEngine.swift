import Foundation
import KosherSwift

/// Computes Otzaria zmanim for a (city, civil date).
enum ZmanimEngine {
    /// Years the astronomical and calendar code is exercised for; outside it an empty answer is returned.
    static let supportedYears = 1000...3000

    /// Zmanim in registry (Otzaria display) order, only those in `ids` (unknown ids ignored). A zman whose per-day predicate is
    /// false (candle lighting, motzei, chametz, fast ends) is omitted; a relevant zman that cannot be computed gets `time == nil`.
    static func compute(date: CivilDate, city: CityInfo, ids: [String]) -> [ZmanTime] {
        guard supportedYears.contains(date.year), !ids.isEmpty else { return [] }
        let wanted = Set(ids)
        var result: [ZmanTime] = []
        result.reserveCapacity(wanted.count)
        let ctx = ZmanContext(city: city, date: date)
        let jc = ctx.jewish
        for e in ZmanimRegistry.all where wanted.contains(e.id) {
            if !ZmanCalculator.isRelevant(e.id, jc) { continue }
            guard let v = ZmanCalculator.compute(e.id, ctx) else { continue }
            var display: String?
            var instant: Date?
            if let t = v.time {
                instant = Date(timeIntervalSince1970: Double(t) / 1000)
                display = v.label ?? ZmanTimeFormatter.format(secondOfDay: ctx.zone.local(atMs: t).secondOfDay)
            }
            result.append(ZmanTime(id: e.id, title: e.fullName, time: instant, displayText: display))
        }
        return result
    }
}
