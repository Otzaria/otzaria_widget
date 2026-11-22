import Foundation
import KosherSwift

/// Kidush levana as Otzaria shows it ("ליל <weekday> <day> לחודש <time>"). Reproduces kosher_dart's molad without the
/// 20m56.5s LMT correction (wall-clock fields read as UTC+2), so the library's own kidush levana methods are not used.
enum KidushLevana {
    private static let nightNames = ["", "ליל ראשון", "ליל שני", "ליל שלישי", "ליל רביעי", "ליל חמישי", "ליל שישי", "ליל שבת"]

    static func compute(_ c: ZmanContext, offsetMs: Int64, pushToTzais: Bool) -> ZmanValue {
        let raw = rawInstant(c.jewish, offsetMs: offsetMs)
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

    /// The naive molad wall-clock fields of the month containing `jc` plus `offsetMs`, read as UTC+2 (milliseconds dropped).
    static func rawInstant(_ jc: JewishCalendar, offsetMs: Int64) -> Ms {
        let molad = jc.getMolad()
        let d = molad.getLocalDate()
        let seconds = Int(Double(molad.moladChalakim) * 10 / 3.0) // kosher_dart: moladSeconds.toInt()
        let naive = Int64(CivilDate(year: d.year, month: d.month, day: d.day).epochDay) * 86_400_000
            + Int64(molad.moladHours * 3600 + molad.moladMinutes * 60 + seconds) * 1000 + offsetMs
        return naive - floorMod(naive, 1000) - 2 * 3_600_000
    }

    private static func nightLabel(_ c: ZmanContext, _ moment: Ms, advanceToNextDay: Bool) -> String {
        let local = c.zone.local(atMs: moment)
        let date = advanceToNextDay ? local.date.addingDays(1) : local.date
        let night = nightNames[date.dayOfWeek]
        let jd = JewishCalendar(localDate: LocalDate(year: date.year, month: date.month, day: date.day))
        return "\(night) \(HebrewNumbers.format(jd.getJewishDayOfMonth())) לחודש \(ZmanTimeFormatter.format(secondOfDay: local.secondOfDay))"
    }
}
