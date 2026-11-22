import Foundation
import OtzariaCalendar
import OtzariaCore
@testable import OtzariaWidgetsUI

enum T {
    /// 2026-11-17 09:00 UTC = 11:00 in Jerusalem.
    static let now = Date(timeIntervalSince1970: 1_794_906_000)

    static func ctx(_ size: WidgetSize, snapshot: WidgetSnapshot? = Fixtures.snapshot(now: now), settings: WidgetSettings = WidgetSettings(),
                    now: Date = T.now) -> WidgetContext {
        WidgetContext(snapshot: snapshot, settings: settings, now: now, size: size)
    }

    static func entry(_ id: Int, _ title: String, ref: String = "", pdf: Bool = false, label: String? = nil, index: Int = 1) -> ReadingEntry {
        var e = ReadingEntry(book: BookRef(id: id, title: title, type: pdf ? "PdfBook" : "TextBook"), ref: ref, index: index, label: label)
        e.deepLink = OtzariaLinks.buildLink(for: e)
        return e
    }
}
