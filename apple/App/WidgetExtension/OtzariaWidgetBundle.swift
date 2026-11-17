import SwiftUI
import WidgetKit
import OtzariaWidgetsUI

@main
struct OtzariaWidgetBundle: WidgetBundle {
    var body: some Widget {
        RecentBooksWidget()
        BookmarksWidget()
        ShamorZachorWidget()
        ZmanimWidget()
        HebrewCalendarWidget()
        DashboardWidget()
    }
}
