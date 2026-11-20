import XCTest
@testable import OtzariaCalendar

final class RegistryTests: XCTestCase {
    private let svc = CalendarService()

    private let expectedDefaults = [
        "alos72Degrees", "alos90Degrees", "misheyakir10point2", "sunrise", "sofZmanShmaMGA90Degrees", "sofZmanShmaGRA",
        "sofZmanTfilaMGA90Degrees", "sofZmanTfilaGRA", "chatzos", "chatzosLayla", "minchaGedolaGreater30", "minchaKetanaGRA", "plagGRA",
        "sunset", "seaLevelSunset", "tzeitTikochinsky", "tzeitItimLeVina", "rt72Shavos", "candleLighting", "motzeiShabbos",
        "motzeiShabbosChazonIsh", "motzeiYomTov", "motzeiYomTovChazonIsh", "achilasChametzMGADegrees", "achilasChametzGRA",
        "biurChametzMGADegrees", "biurChametzGRA", "fastEndTikLenient", "fastEndItimLenient", "fastEndTikStringent", "fastEndItimStringent",
    ]

    func testRegistryHas98UniqueIdsInOtzariaOrder() {
        let r = svc.zmanimRegistry
        XCTAssertEqual(r.count, 98)
        XCTAssertEqual(Set(r.map(\.id)).count, 98)
        XCTAssertEqual(r[0].id, "alos72Degrees")
        XCTAssertEqual(r[1].id, "alos90Degrees")
        XCTAssertEqual(r[53].id, "sunset")
        XCTAssertEqual(r[79].id, "candleLighting")
        XCTAssertEqual(r.last?.id, "sofKidushLevana15")
    }

    func testDefaultEnabledSetIsThe31OtzariaDefaults() {
        XCTAssertEqual(svc.defaultEnabledZmanim.count, 31)
        XCTAssertEqual(svc.defaultEnabledZmanim, expectedDefaults)
        let ids = Set(svc.zmanimRegistry.map(\.id))
        XCTAssertTrue(svc.defaultEnabledZmanim.allSatisfy(ids.contains))
        XCTAssertEqual(svc.zmanimRegistryDetails.filter(\.defaultEnabled).count, 31)
    }

    func testEveryEntryHasAHebrewTitleAndDescription() {
        func hasHebrew(_ s: String) -> Bool { s.unicodeScalars.contains { (0x05D0...0x05EA).contains($0.value) } }
        for z in svc.zmanimRegistry {
            XCTAssertTrue(hasHebrew(z.title), z.id)
            XCTAssertFalse((z.description ?? "").trimmingCharacters(in: .whitespaces).isEmpty, z.id)
            XCTAssertTrue(hasHebrew(z.description ?? ""), z.id)
        }
        for e in svc.zmanimRegistryDetails {
            XCTAssertTrue(hasHebrew(e.title))
            XCTAssertTrue(hasHebrew(e.category))
        }
    }

    func testTitlesAreOtzariasFullNames() {
        let cases = [
            ("alos72Degrees", "עלות השחר — 72 דק׳ (מעלות)"), ("sunrise", "הנץ הנראה"), ("sunriseAstronomical", "נץ אסטרונומי — בתיקון גובה"),
            ("sofZmanShmaGRA", "סוף זמן ק\"ש — גר\"א"), ("chatzos", "חצות היום"), ("chatzosLayla", "חצות לילה"), ("sunset", "שקיעה"),
            ("seaLevelSunset", "שקיעה מישורית"), ("tzeitTikochinsky", "צאת הכוכבים — טיקוצ׳ינסקי"), ("rt72Shavos", "רבנו תם — 72 דק׳ (שוות)"),
            ("candleLighting", "הדלקת נרות"), ("motzeiShabbosChazonIsh", "מוצאי שבת — חזו\"א"),
            ("fastEndItimStringent", "יציאת הצום — לחומרא — עיתים לבינה"), ("sofKidushLevana15", "סוף קידוש לבנה — 15 ימים"),
        ]
        for (id, title) in cases { XCTAssertEqual(svc.zmanimRegistry.first { $0.id == id }?.title, title, id) }
    }

    func testDetailsExposeSubtitleCategoryAndPairing() throws {
        let alos = try XCTUnwrap(svc.zmanimRegistryDetails.first { $0.id == "alos72Degrees" })
        XCTAssertEqual(alos.title, "עלות השחר")
        XCTAssertEqual(alos.subtitle, "72 דק׳ (מעלות)")
        XCTAssertEqual(alos.category, "עלות השחר")
        XCTAssertEqual(alos.pairId, "alosDeg")
        XCTAssertTrue(svc.zmanimRegistryDetails.first { $0.id == "tchilasKidushLevana3" }!.showHebrewDate)
        XCTAssertFalse(svc.zmanimRegistryDetails.first { $0.id == "sunset" }!.showHebrewDate)
    }

    func testEveryRegistryIdHasACalculation() {
        for z in svc.zmanimRegistry { XCTAssertTrue(ZmanCalculator.hasCalculation(z.id), z.id) }
    }

    func testEveryZmanComputesForEveryCityOnARegularDayWithTheCityOffset() {
        // 2026-03-04 (Wed): only the always-relevant zmanim apply
        let date = D("2026-03-04")
        let ids = svc.zmanimRegistry.map(\.id)
        for city in svc.cities {
            let zone = CityZone.resolve(city)
            let result = svc.zmanim(for: date, city: city, ids: ids)
            XCTAssertTrue(result.contains { $0.id == "sunset" && $0.time != nil }, city.name)
            for z in result {
                guard let t = z.time else { continue }
                // the formatted clock is the city's wall clock at that instant
                let local = zone.local(atMs: ZmanTimeFormatter.ms(t))
                if ZmanimRegistry.find(z.id)?.showHebrewDate != true {
                    XCTAssertEqual(z.displayText, ZmanTimeFormatter.format(secondOfDay: local.secondOfDay), "\(city.name) \(z.id)")
                }
            }
        }
    }
}
