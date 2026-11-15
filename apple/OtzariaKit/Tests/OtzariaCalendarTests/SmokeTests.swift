import XCTest
@testable import OtzariaCalendar

final class SmokeTests: XCTestCase {
    func testTodayJerusalem() {
        let svc = CalendarService()
        let jer = svc.findCity("ירושלים")!
        let i = svc.dayInfo(for: D("2026-11-13"), city: jer)
        print(i)
        let z = svc.zmanim(for: D("2026-11-13"), city: jer, ids: svc.defaultEnabledZmanim)
        for t in z { print(t.id, t.displayText ?? "nil") }
    }
}
