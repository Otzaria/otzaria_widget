import Foundation
import KosherSwift

/// Result of computing one zman: the instant plus an optional Otzaria label (kidush levana only).
struct ZmanValue {
    var time: Ms?
    var label: String?

    init(_ time: Ms?, label: String? = nil) {
        self.time = time
        self.label = label
    }
}

/// One computation per Otzaria registry id (kZmanimRegistry), translated from KosherJava's definitions with kosher_dart's arithmetic.
enum ZmanCalculator {
    private static let tikochinsky = 13.5 // minutes

    static func compute(_ id: String, _ c: ZmanContext) -> ZmanValue? { table[id]?(c) }

    static func hasCalculation(_ id: String) -> Bool { table[id] != nil }

    /// Otzaria's per-day relevance predicates. No predicate = always relevant.
    static func isRelevant(_ id: String, _ jc: JewishCalendar) -> Bool {
        switch id {
        case "candleLighting": return isErevShabbosOrYomTov(jc)
        case "motzeiShabbos", "motzeiShabbosChazonIsh": return isMotzeiShabbos(jc)
        case "motzeiYomTov", "motzeiYomTovChazonIsh": return isMotzeiYomTov(jc)
        case "achilasChametzMGADegrees", "achilasChametzGRA", "achilasChametzMGAShavos",
             "biurChametzMGADegrees", "biurChametzGRA", "biurChametzMGAShavos": return isErevPesach(jc)
        case "fastEndTikLenient", "fastEndItimLenient", "fastEndTikStringent", "fastEndItimStringent": return isTaanisNotYomKippur(jc)
        default: return true
        }
    }

    // 1 = Sunday ... 7 = Shabbat.
    static func isErevShabbosOrYomTov(_ jc: JewishCalendar) -> Bool {
        let dow = jc.getDayOfWeek()
        return dow == 6 || (jc.isErevYomTov() && dow != 7)
    }

    static func isMotzeiShabbos(_ jc: JewishCalendar) -> Bool {
        jc.isAssurBemelacha() && jc.getDayOfWeek() == 7 && !jc.isTomorrowShabbosOrYomTov()
    }

    static func isMotzeiYomTov(_ jc: JewishCalendar) -> Bool {
        jc.isAssurBemelacha() && jc.isYomTov() && jc.getDayOfWeek() != 7 && !jc.isTomorrowShabbosOrYomTov()
    }

    static func isErevPesach(_ jc: JewishCalendar) -> Bool { jc.getYomTovIndex() == JewishCalendar.EREV_PESACH }

    static func isTaanisNotYomKippur(_ jc: JewishCalendar) -> Bool {
        jc.isTaanis() && jc.getYomTovIndex() != JewishCalendar.YOM_KIPPUR
    }

    /// chatzos layla: midpoint of today's sunset and tomorrow's sunrise (both elevation adjusted); falls back to chatzos + 12h.
    static func chatzosLayla(_ c: ZmanContext) -> Ms? {
        guard let sunsetToday = c.sunset else { return nil }
        guard let sunriseTomorrow = c.tomorrow.sunrise else { return chatzosHayom(c).map { $0 + 12 * 3_600_000 } }
        return sunsetToday + (sunriseTomorrow - sunsetToday) / 2
    }

    /// Half-day chatzos (sea level); astronomical noon only when there is no sunrise/sunset.
    static func chatzosHayom(_ c: ZmanContext) -> Ms? {
        if let half = ZmanContext.sunTransit(c.seaLevelSunrise, c.seaLevelSunset) { return half }
        guard let noon = c.calendar.getSunTransit() else { return nil }
        let ms = Int64((noon.timeIntervalSince1970 * 1000).rounded())
        return ms - floorMod(ms, 1000)
    }

    // MARK: building blocks (names follow the library)

    private static func alos16(_ c: ZmanContext) -> Ms? { c.sunriseByDegrees(106.1) }
    private static func alos19(_ c: ZmanContext) -> Ms? { c.sunriseByDegrees(109.8) }
    private static func alos26(_ c: ZmanContext) -> Ms? { c.sunriseByDegrees(116.0) }
    private static func tzais16(_ c: ZmanContext) -> Ms? { c.sunsetByDegrees(106.1) }
    private static func tzais19(_ c: ZmanContext) -> Ms? { c.sunsetByDegrees(109.8) }
    private static func tzais26(_ c: ZmanContext) -> Ms? { c.sunsetByDegrees(116.0) }
    private static func tzais8_5(_ c: ZmanContext) -> Ms? { c.sunsetByDegrees(98.5) }
    private static func tzais7_083(_ c: ZmanContext) -> Ms? { c.sunsetByDegrees(90 + 7 + 5.0 / 60) }

    private static func alosMin(_ c: ZmanContext, _ m: Double) -> Ms? { ZmanContext.minutes(c.seaLevelSunrise, -m) }
    private static func tzaisMin(_ c: ZmanContext, _ m: Double) -> Ms? { ZmanContext.minutes(c.seaLevelSunset, m) }

    private static func shaah(_ start: Ms?, _ end: Ms?, _ hours: Double) -> Ms? {
        ZmanContext.shaahZmanisBased(start, end, hours: hours)
    }

    /// Sunrise-to-sunset (GRA) based zman.
    private static func gra(_ c: ZmanContext, _ hours: Double) -> Ms? { shaah(c.seaLevelSunrise, c.seaLevelSunset, hours) }

    private static func plagGRA(_ c: ZmanContext) -> Ms? { gra(c, 10.75) }

    private static func nullIfBeforePlagGRA(_ c: ZmanContext, _ zman: Ms?) -> Ms? {
        if let z = zman, let plag = plagGRA(c), z < plag { return nil }
        return zman
    }

    private static func minchaGedola30(_ c: ZmanContext) -> Ms? {
        let m = ZmanContext.minutes(chatzosHayom(c), 30)
        if let m, let ss = c.seaLevelSunset, m > ss { return nil }
        return m
    }

    private static func minchaGedolaGreater30(_ c: ZmanContext) -> Ms? {
        guard let gedola = gra(c, 6.5) else { return nil }
        guard let g30 = minchaGedola30(c) else { return gedola }
        return g30 > gedola ? g30 : gedola
    }

    /// Chametz-time guard of the library (Erev Pesach only); relevance is already filtered per day.
    private static func onErevPesach(_ c: ZmanContext, _ f: () -> Ms?) -> Ms? {
        c.jewish.getJewishMonth() == JewishCalendar.NISSAN && c.jewish.getJewishDayOfMonth() == 14 ? f() : nil
    }

    private static func localChatzos(_ c: ZmanContext) -> Ms? {
        let hours = 12.0
        let day = Int64(c.date.epochDay) * 86_400_000
        let noon = day + Int64((hours * 3_600_000).rounded(.towardZero))
        let lmt = Int64((c.city.longitude * 4 * 60_000).rounded(.towardZero))
        let fixed = noon - lmt
        if let sr = c.seaLevelSunrise, fixed < sr { return nil }
        if let ss = c.seaLevelSunset, fixed > ss { return nil }
        return fixed
    }

    private static let table: [String: (ZmanContext) -> ZmanValue] = {
        var d: [String: (ZmanContext) -> ZmanValue] = [:]
        func add(_ id: String, _ f: @escaping (ZmanContext) -> Ms?) { d[id] = { ZmanValue(f($0)) } }

        // עלות השחר
        add("alos72Degrees") { alos16($0) }
        add("alos90Degrees") { alos19($0) }
        add("alos120Degrees") { alos26($0) }
        add("alos72Shavos") { alosMin($0, 72) }
        add("alos90Shavos") { alosMin($0, 90) }
        add("alos120Shavos") { alosMin($0, 120) }
        add("alos72Zmanis") { $0.zmanisBasedOffset(hours: -1.2) }
        add("alos90Zmanis") { $0.zmanisBasedOffset(hours: -1.5) }
        add("alos120Zmanis") { $0.zmanisBasedOffset(hours: -2.0) }
        // ציצית ותפילין / זריחה
        add("misheyakir10point2") { $0.sunriseByDegrees(90 + 10.2) }
        add("misheyakir11") { $0.sunriseByDegrees(90 + 11) }
        add("misheyakir11point5") { $0.sunriseByDegrees(90 + 11.5) }
        add("sunriseAstronomical") { $0.sunrise }
        add("sunrise") { $0.seaLevelSunrise } // "הנץ הנראה" deliberately uses sea level
        // קריאת שמע
        add("sofZmanShmaMGA90Degrees") { shaah(alos19($0), tzais19($0), 3) }
        add("sofZmanShmaGRA") { gra($0, 3) }
        add("sofZmanShmaMGA72Degrees") { shaah(alos16($0), tzais16($0), 3) }
        add("sofZmanShmaMGA72Shavos") { shaah(alosMin($0, 72), tzaisMin($0, 72), 3) }
        add("sofZmanShmaMGA72Zmanis") { shaah($0.zmanisBasedOffset(hours: -1.2), $0.zmanisBasedOffset(hours: 1.2), 3) }
        add("sofZmanShmaMGA90Shavos") { shaah(alosMin($0, 90), tzaisMin($0, 90), 3) }
        add("sofZmanShmaMGA90Zmanis") { shaah($0.zmanisBasedOffset(hours: -1.5), $0.zmanisBasedOffset(hours: 1.5), 3) }
        add("sofZmanShmaMGA120Shavos") { shaah(alosMin($0, 120), tzaisMin($0, 120), 3) }
        // תפילה
        add("sofZmanTfilaMGA90Degrees") { shaah(alos19($0), tzais19($0), 4) }
        add("sofZmanTfilaGRA") { gra($0, 4) }
        add("sofZmanTfilaMGA72Degrees") { shaah(alos16($0), tzais16($0), 4) }
        add("sofZmanTfilaMGA72Shavos") { shaah(alosMin($0, 72), tzaisMin($0, 72), 4) }
        add("sofZmanTfilaMGA72Zmanis") { shaah($0.zmanisBasedOffset(hours: -1.2), $0.zmanisBasedOffset(hours: 1.2), 4) }
        add("sofZmanTfilaMGA90Shavos") { shaah(alosMin($0, 90), tzaisMin($0, 90), 4) }
        add("sofZmanTfilaMGA90Zmanis") { shaah($0.zmanisBasedOffset(hours: -1.5), $0.zmanisBasedOffset(hours: 1.5), 4) }
        add("sofZmanTfilaMGA120Shavos") { shaah(alosMin($0, 120), tzaisMin($0, 120), 4) }
        // חצות
        add("chatzos") { chatzosHayom($0) }
        add("chatzosFixedLocal") { localChatzos($0) }
        add("chatzosLayla") { chatzosLayla($0) }
        // מנחה
        add("minchaGedolaGreater30") { minchaGedolaGreater30($0) }
        add("minchaGedolaGRA") { gra($0, 6.5) }
        add("minchaGedola30") { minchaGedola30($0) }
        add("minchaGedola16point1") { shaah(alos16($0), tzais16($0), 6.5) }
        add("minchaKetanaGRA") { gra($0, 9.5) }
        add("minchaKetana16point1") { shaah(alos16($0), tzais16($0), 9.5) }
        add("minchaKetana90Degrees") { shaah(alos19($0), tzais19($0), 9.5) }
        add("minchaKetana72") { shaah(alosMin($0, 72), tzaisMin($0, 72), 9.5) }
        // פלג המנחה
        add("plagGRA") { plagGRA($0) }
        add("plag72Shavos") { shaah(alosMin($0, 72), tzaisMin($0, 72), 10.75) }
        add("plag90Shavos") { shaah(alosMin($0, 90), tzaisMin($0, 90), 10.75) }
        add("plag120Shavos") { shaah(alosMin($0, 120), tzaisMin($0, 120), 10.75) }
        add("plag72Zmanis") { shaah($0.zmanisBasedOffset(hours: -1.2), $0.zmanisBasedOffset(hours: 1.2), 10.75) }
        add("plag90Zmanis") { shaah($0.zmanisBasedOffset(hours: -1.5), $0.zmanisBasedOffset(hours: 1.5), 10.75) }
        add("plag120Zmanis") { shaah($0.zmanisBasedOffset(hours: -2.0), $0.zmanisBasedOffset(hours: 2.0), 10.75) }
        add("plag72Degrees") { shaah(alos16($0), tzais16($0), 10.75) }
        add("plag90Degrees") { shaah(alos19($0), tzais19($0), 10.75) }
        add("plag120Degrees") { shaah(alos26($0), tzais26($0), 10.75) }
        add("plagAlosToSunset") { shaah(alos16($0), $0.seaLevelSunset, 10.75) }
        add("plagAlos16ToGeonim") { shaah(alos16($0), tzais7_083($0), 10.75) }
        // שקיעה / בין השמשות
        add("sunset") { $0.sunset }
        add("seaLevelSunset") { $0.seaLevelSunset }
        add("binRT13point24") { $0.sunsetByDegrees(90 + 13.24) }
        add("binRT58point5") { ZmanContext.minutes($0.seaLevelSunset, 58.5) }
        add("binRT13point5") { ZmanContext.minutes(tzais7_083($0), -13.5) }
        add("binYereim18") { nullIfBeforePlagGRA($0, ZmanContext.minutes($0.seaLevelSunset, -18)) }
        add("binYereim16point875") { nullIfBeforePlagGRA($0, ZmanContext.minutes($0.seaLevelSunset, -16.875)) }
        add("binYereim13point5") { nullIfBeforePlagGRA($0, ZmanContext.minutes($0.seaLevelSunset, -13.5)) }
        add("binYereim3point5") { $0.sunsetByDegrees(90 - 3.05) } // kosher_dart's "3.5" is really 3.05 degrees
        add("binYereim2point8") { $0.sunsetByDegrees(90 - 2.8) }
        add("binYereim2point1") { $0.sunsetByDegrees(90 - 2.1) }
        // צאת הכוכבים (fixed minutes are added to the elevation-adjusted sunset, by Otzaria's own code)
        add("tzeitTikochinsky") { ZmanContext.minutes($0.sunset, tikochinsky) }
        add("tzeitItimLeVina") { ZmanContext.minutes($0.sunset, 18) }
        add("tzeitGeonim3point65") { $0.sunsetByDegrees(90 + 3.65) }
        add("tzeitGeonim7point083") { tzais7_083($0) }
        add("tzeitGeonim8point5") { tzais8_5($0) }
        add("tzeitChazonIsh") { $0.sunsetByDegrees(90 + 9.3) }
        // רבנו תם
        add("rt72Shavos") { ZmanContext.minutes($0.sunset, 72) }
        add("rt72Zmanis") { $0.zmanisBasedOffset(hours: 1.2) }
        add("rt72Degrees") { tzais16($0) }
        add("rt90Shavos") { tzaisMin($0, 90) }
        add("rt90Zmanis") { $0.zmanisBasedOffset(hours: 1.5) }
        add("rt90Degrees") { tzais19($0) }
        add("rt120Shavos") { tzaisMin($0, 120) }
        add("rt120Zmanis") { $0.zmanisBasedOffset(hours: 2.0) }
        add("rt120Degrees") { tzais26($0) }
        // שבת וחג (Otzaria's own rules, not KosherJava's candle lighting)
        add("candleLighting") { ZmanContext.minutes($0.sunset, Double(-$0.city.candleLightingMinutes)) }
        add("motzeiShabbos") { ZmanContext.minutes($0.sunset, 34) }
        add("motzeiShabbosChazonIsh") { ZmanContext.minutes($0.sunset, 38) }
        add("motzeiYomTov") { ZmanContext.minutes($0.sunset, 34) }
        add("motzeiYomTovChazonIsh") { ZmanContext.minutes($0.sunset, 38) }
        // ערב פסח
        add("achilasChametzMGADegrees") { c in onErevPesach(c) { shaah(alos16(c), tzais16(c), 4) } }
        add("achilasChametzGRA") { c in onErevPesach(c) { gra(c, 4) } }
        add("achilasChametzMGAShavos") { c in onErevPesach(c) { shaah(alosMin(c, 72), tzaisMin(c, 72), 4) } }
        add("biurChametzMGADegrees") { c in onErevPesach(c) { shaah(alos16(c), tzais16(c), 5) } }
        add("biurChametzGRA") { c in onErevPesach(c) { gra(c, 5) } }
        add("biurChametzMGAShavos") { c in onErevPesach(c) { shaah(alosMin(c, 72), tzaisMin(c, 72), 5) } }
        // תעניות
        add("fastEndTikLenient") { ZmanContext.minutes($0.sunset, tikochinsky) }
        add("fastEndItimLenient") { ZmanContext.minutes($0.sunset, 18) }
        add("fastEndTikStringent") { ZmanContext.minutes($0.sunset, 18) }
        add("fastEndItimStringent") { ZmanContext.minutes($0.sunset, 22) }
        // קידוש לבנה
        d["tchilasKidushLevana3"] = { KidushLevana.compute($0, offsetMs: 3 * 86_400_000, pushToTzais: true) }
        d["tchilasKidushLevana7"] = { KidushLevana.compute($0, offsetMs: 7 * 86_400_000, pushToTzais: true) }
        d["sofKidushLevanaMoldos"] = { KidushLevana.compute($0, offsetMs: ((14 * 24 + 18) * 3600 + 22 * 60 + 1) * 1000 + 666, pushToTzais: false) }
        d["sofKidushLevana15"] = { KidushLevana.compute($0, offsetMs: 15 * 86_400_000, pushToTzais: false) }
        return d
    }()
}
