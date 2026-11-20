import XCTest
@testable import OtzariaCalendar

/// Hand-checked values for the tricky days (all of them are also covered day-by-day by HebrewGoldenTests).
final class DayInfoTests: XCTestCase {
    private let svc = CalendarService()
    private lazy var jerusalem = svc.findCity("ירושלים")!
    private lazy var modiinIllit = svc.findCity("מודיעין עילית")!
    private lazy var newYork = svc.findCity("ניו יורק")!

    private func info(_ date: String, _ city: CityInfo? = nil) -> DayInfo { svc.dayInfo(for: D(date), city: city ?? jerusalem) }

    func testToday2026_11_13() {
        let i = info("2026-11-13")
        XCTAssertEqual(i.hebrewDate, "ג׳ כסלו תשפ״ז")
        XCTAssertEqual(i.hebrewDayOfWeek, "יום שישי")
        XCTAssertEqual(i.gregorianDate, "13/11/2026")
        XCTAssertEqual(i.parasha, "תולדות")
        XCTAssertEqual(i.dafYomiBavli, "בכורות נו")
        XCTAssertNotNil(i.dafYomiYerushalmi)
        XCTAssertNil(i.omer)
        XCTAssertTrue(i.holidays.isEmpty)
        XCTAssertFalse(i.isShabbat)
        XCTAssertFalse(i.isYomTov)
        XCTAssertEqual(svc.hhmm(i.candleLighting!, jerusalem), "16:05")
        XCTAssertNil(i.havdalah)
        XCTAssertEqual(svc.hhmm(info("2026-11-13", modiinIllit).candleLighting!, modiinIllit), "16:14")
    }

    func testShabbatHasHavdalah34MinutesAfterSunsetAndNoCandleLighting() {
        let i = info("2026-11-14")
        XCTAssertTrue(i.isShabbat)
        XCTAssertEqual(i.hebrewDayOfWeek, "שבת קודש")
        XCTAssertEqual(i.hebrewDate, "ד׳ כסלו תשפ״ז")
        XCTAssertEqual(i.parasha, "תולדות")
        XCTAssertTrue(i.holidays.isEmpty) // the parasha is kept in parasha, not duplicated in holidays
        XCTAssertNil(i.candleLighting)
        XCTAssertEqual(i.havdalah, svc.zman("2026-11-14", jerusalem, "sunset").addingTimeInterval(34 * 60))
    }

    func testWeekdayParashaIsTheUpcomingShabbat() {
        XCTAssertEqual(info("2026-11-08").parasha, "תולדות")  // Sunday after Chayei Sarah
        XCTAssertEqual(info("2026-11-07").parasha, "חיי שרה") // Shabbat itself
        XCTAssertEqual(info("2026-11-15", newYork).parasha, "ויצא")
    }

    func testRoshHashanaAndErev() {
        let erev = info("2026-09-11")
        XCTAssertEqual(erev.hebrewDate, "כ״ט אלול תשפ״ו")
        XCTAssertEqual(erev.holidays, ["ערב ראש השנה"])
        XCTAssertNotNil(erev.candleLighting)
        XCTAssertFalse(erev.isYomTov)

        let rh1 = info("2026-09-12")
        XCTAssertEqual(rh1.hebrewDate, "א׳ תשרי תשפ״ז")
        XCTAssertEqual(rh1.holidays, ["ראש השנה"])
        XCTAssertTrue(rh1.isYomTov)
        XCTAssertTrue(rh1.isShabbat)
        XCTAssertNil(rh1.havdalah) // next day is Yom Tov

        let rh2 = info("2026-09-13", newYork)
        XCTAssertEqual(rh2.hebrewDate, "ב׳ תשרי תשפ״ז")
        XCTAssertEqual(rh2.holidays, ["ראש השנה"])
        XCTAssertTrue(rh2.isYomTov)
        XCTAssertNotNil(rh2.havdalah)
        XCTAssertEqual(rh2.parasha, "האזינו")
        XCTAssertEqual(info("2026-09-14").holidays, ["צום גדליה"])
    }

    func testYomKippur() {
        XCTAssertEqual(info("2026-09-20").holidays, ["ערב יום כיפור"])
        XCTAssertNotNil(info("2026-09-20").candleLighting)
        let yk = info("2026-09-21")
        XCTAssertEqual(yk.hebrewDate, "י׳ תשרי תשפ״ז")
        XCTAssertEqual(yk.holidays, ["יום כיפור"])
        XCTAssertTrue(yk.isYomTov)
        XCTAssertNil(yk.dafYomiYerushalmi) // the Yerushalmi cycle skips Yom Kippur
    }

    func testSukkotAndSheminiAtzeretIsraelVsDiaspora() {
        XCTAssertEqual(info("2026-09-26").holidays, ["סוכות"])
        XCTAssertEqual(info("2026-09-27").holidays, ["א דחוה\"מ סוכות"]) // Israel: chol hamoed already
        XCTAssertEqual(info("2026-09-27", newYork).holidays, ["סוכות"])    // diaspora: second day yom tov
        XCTAssertTrue(info("2026-09-27", newYork).isYomTov)
        XCTAssertFalse(info("2026-09-27").isYomTov)
        XCTAssertEqual(info("2026-10-02").holidays, ["הושענא רבה", "ו' דחוה\"מ"])
        XCTAssertEqual(info("2026-10-02", newYork).holidays, ["הושענא רבה", "ה' דחוה\"מ"])
        XCTAssertEqual(info("2026-10-03").holidays, ["שמיני עצרת", "שמחת תורה"])
        XCTAssertEqual(info("2026-10-03", newYork).holidays, ["שמיני עצרת"])
        XCTAssertTrue(info("2026-10-04").holidays.isEmpty)
        XCTAssertEqual(info("2026-10-04", newYork).holidays, ["שמחת תורה"])
        XCTAssertTrue(info("2026-10-04", newYork).isYomTov)
        XCTAssertEqual(info("2026-10-04").parasha, "בראשית")
    }

    func testChanukahAcrossRoshChodeshTevet() {
        XCTAssertEqual(info("2026-12-05").holidays, ["נר א דחנוכה"])
        XCTAssertEqual(info("2026-12-06").holidays, ["נר ב דחנוכה"])
        XCTAssertEqual(info("2026-12-10").holidays, ["ר\"ח", "נר ו דחנוכה"]) // 30 Kislev is Rosh Chodesh
        XCTAssertEqual(info("2026-12-11").holidays, ["ר\"ח", "נר ז דחנוכה"]) // 1 Tevet
        XCTAssertEqual(info("2026-12-12").holidays, ["נר ח דחנוכה"])
        XCTAssertTrue(info("2026-12-13").holidays.isEmpty)
        XCTAssertEqual(info("2026-12-11").hebrewDate, "א׳ טבת תשפ״ז")
        XCTAssertEqual(info("2026-12-20", newYork).holidays, ["עשרה בטבת"])
    }

    func testPurimAndShushanPurimInLeapYear() {
        XCTAssertEqual(info("2027-03-22").hebrewDate, "י״ג אדר ב׳ תשפ״ז")
        XCTAssertEqual(info("2027-03-22").holidays, ["תענית אסתר"])
        XCTAssertEqual(info("2027-03-23").holidays, ["פורים"])
        XCTAssertEqual(info("2027-03-24").holidays, ["פורים שושן"]) // Otzaria's (kosher_dart) spelling
        XCTAssertEqual(info("2027-03-06").hebrewDate, "כ״ז אדר א׳ תשפ״ז")
        XCTAssertEqual(info("2027-02-21").holidays, ["פורים קטן"])
        XCTAssertEqual(info("2027-02-22").holidays, ["פורים שושן קטן"])
    }

    func testPesachCholHamoedAndSeventhDay() {
        XCTAssertEqual(info("2026-04-01").holidays, ["ערב פסח"])
        XCTAssertEqual(info("2026-04-02").holidays, ["פסח"])
        XCTAssertEqual(info("2026-04-03").holidays, ["א דחוה\"מ פסח"])
        XCTAssertEqual(info("2026-04-03", newYork).holidays, ["פסח"]) // diaspora second day
        XCTAssertEqual(info("2026-04-04").holidays, ["ב דחוה\"מ פסח"])
        XCTAssertEqual(info("2026-04-08").holidays, ["שביעי של פסח"])
        XCTAssertEqual(info("2026-04-08", newYork).holidays, ["שביעי של פסח"])
        XCTAssertTrue(info("2026-04-09").holidays.isEmpty)            // Israel: Isru Chag is filtered out
        XCTAssertEqual(info("2026-04-09", newYork).holidays, ["פסח"]) // diaspora: last day
        XCTAssertEqual(info("2026-04-03").omer, "היום יום אחד בעומר")
        XCTAssertEqual(info("2026-04-09").omer, "היום שבעה ימים שהם שבוע אחד בעומר")
    }

    func testShavuotAndLagBaomer() {
        XCTAssertEqual(info("2026-05-22").holidays, ["שבועות"])
        XCTAssertTrue(info("2026-05-23").holidays.isEmpty)
        XCTAssertEqual(info("2026-05-23", newYork).holidays, ["שבועות"])
        XCTAssertEqual(info("2026-05-05").holidays, ["ל״ג בעומר"])
        XCTAssertEqual(info("2026-05-05").omer, "היום שלשה ושלשים יום שהם ארבעה שבועות וחמשה ימים בעומר")
        XCTAssertNil(info("2026-05-22").omer) // day 50 is Shavuot itself
    }

    func testFasts17TammuzAnd9AvIncludingPostponementFromShabbat() {
        XCTAssertEqual(info("2026-07-02").holidays, ["שבעה עשר בתמוז"])
        XCTAssertEqual(info("2026-07-23").holidays, ["תשעה באב"])
        XCTAssertNil(info("2026-07-23").dafYomiYerushalmi) // Yerushalmi skips Tisha B'Av
        // 17 Tammuz 5789 falls on Shabbat 2029-06-30 -> the fast is Sunday 18 Tammuz
        XCTAssertTrue(info("2029-06-30").holidays.isEmpty)
        XCTAssertEqual(info("2029-07-01").holidays, ["שבעה עשר בתמוז"])
        XCTAssertEqual(info("2029-07-01").hebrewDate, "י״ח תמוז תשפ״ט")
        // 9 Av 5785 falls on Shabbat 2025-08-02 -> the fast is Sunday 10 Av
        XCTAssertTrue(info("2025-08-02").holidays.isEmpty)
        XCTAssertEqual(info("2025-08-03").holidays, ["תשעה באב"])
        XCTAssertEqual(svc.zmanim(for: D("2025-08-03"), city: jerusalem, ids: ["fastEndTikLenient", "fastEndItimStringent"]).count, 2)
        XCTAssertTrue(svc.zmanim(for: D("2025-08-02"), city: jerusalem, ids: ["fastEndTikLenient"]).isEmpty)
    }

    func testRoshChodeshLabelAndDoubleDay() {
        XCTAssertTrue(info("2026-12-11").holidays.contains("ר\"ח"))
        XCTAssertTrue(info("2026-12-10").holidays.contains("ר\"ח")) // 30 Kislev
    }

    func testDoubleParashiyotLeapAndNonLeapYearsAreSpaceJoinedLikeOtzaria() {
        XCTAssertEqual(info("2026-03-14").parasha, "ויקהל פקודי")
        XCTAssertEqual(info("2026-05-09", newYork).parasha, "בהר בחקתי")
        XCTAssertEqual(info("2027-03-06").parasha, "ויקהל")
        XCTAssertEqual(info("2027-03-13").parasha, "פקודי")
        XCTAssertEqual(info("2026-07-11").parasha, "מטות מסעי")
        XCTAssertEqual(info("2026-09-05").parasha, "ניצבים וילך") // Otzaria spelling (kosher_dart), not KosherJava's נצבים
        XCTAssertEqual(info("2025-09-20").parasha, "ניצבים")
    }

    func testShabbatThatIsAYomTovHasNoRegularParashaSoTheUpcomingParashaIsNilOnTheDaysBefore() {
        XCTAssertNil(info("2026-10-02").parasha) // upcoming Shabbat = Shemini Atzeret in Israel
        XCTAssertNil(info("2026-09-26").parasha) // Shabbat Sukkot
    }

    func testDafYomiBeforeTheFirstCycleIsNilAndDoesNotFail() {
        let i = svc.dayInfo(for: CivilDate(year: 1900, month: 1, day: 1), city: jerusalem)
        XCTAssertNil(i.dafYomiBavli)
        XCTAssertFalse(i.hebrewDate.isEmpty)
    }

    func testCandleLightingFollowsCityMinutes() {
        let friday = D("2026-11-13")
        for city in [jerusalem, modiinIllit, newYork, svc.findCity("תל אביב")!, svc.findCity("בני ברק")!] {
            let sunset = svc.zmanim(for: friday, city: city, ids: ["sunset"])[0].time!
            XCTAssertEqual(svc.dayInfo(for: friday, city: city).candleLighting, sunset.addingTimeInterval(-Double(city.candleLightingMinutes) * 60), city.name)
        }
    }
}
