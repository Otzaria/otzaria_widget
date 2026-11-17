import Foundation

/// Kidush levana as Otzaria shows it ("ליל <weekday> <day> לחודש <time>"). Reproduces kosher_dart's molad without the
/// 20m56.5s LMT correction (wall-clock fields read as UTC+2), so the library's own kidush levana methods are not used.
enum KidushLevana {
    private static let nightNames = ["", "ליל ראשון", "ליל שני", "ליל שלישי", "ליל רביעי", "ליל חמישי", "ליל שישי", "ליל שבת"]

    static func compute(_ c: ZmanContext, offsetMs: Int64, pushToTzais: Bool) -> ZmanValue {
        let raw = rawInstant(c.date, offsetMs: offsetMs)
        let rawLocal = c.zone.local(atMs: raw)

        // The civil day (in the city) on which the moment falls gives alos / tzais / sunset.
        let day = ZmanContext(city: c.city, date: rawLocal.date)
        let alos = day.sunriseByDegrees(106.1)  // kosher_dart getAlosHashachar()
        let tzais = day.sunsetByDegrees(98.5)   // kosher_dart getTzais()
        let sunset = day.sunset

        var chosen = raw
        var advance = false
        if let alos, let tzais, let sunset {
            if raw > alos && raw < tzais { chosen = pushToTzais ? tzais : alos }
            advance = chosen > sunset
        }
        return ZmanValue(chosen, label: nightLabel(c, chosen, advanceToNextDay: advance))
    }

    /// The naive molad wall-clock fields of the month containing `date` plus `offsetMs`, read as UTC+2 (milliseconds dropped).
    static func rawInstant(_ date: CivilDate, offsetMs: Int64) -> Ms {
        let h = HebrewDate(epochDay: date.epochDay)
        let molad = HebrewCalendarArithmetic.molad(year: h.year, month: h.month)
        let seconds = Int(Double(molad.chalakim) * 10 / 3.0) // kosher_dart: moladSeconds.toInt()
        let naive = Int64(molad.day.epochDay) * 86_400_000
            + Int64(molad.hours * 3600 + molad.minutes * 60 + seconds) * 1000 + offsetMs
        return naive - floorMod(naive, 1000) - 2 * 3_600_000
    }

    private static func nightLabel(_ c: ZmanContext, _ moment: Ms, advanceToNextDay: Bool) -> String {
        let local = c.zone.local(atMs: moment)
        let date = advanceToNextDay ? local.date.addingDays(1) : local.date
        let night = nightNames[date.dayOfWeek]
        return "\(night) \(HebrewNumbers.format(HebrewDate(epochDay: date.epochDay).day)) לחודש \(ZmanTimeFormatter.format(secondOfDay: local.secondOfDay))"
    }
}
