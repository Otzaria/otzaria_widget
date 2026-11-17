import SwiftUI

@main
struct OtzariaWidgetiOSApp: App {
    @StateObject private var model = SettingsModel(location: AppGroupLocation.resolve())
    @State private var tab: Tab = .calendar

    enum Tab: String { case calendar, about }

    var body: some Scene {
        WindowGroup {
            TabView(selection: $tab) {
                NavigationStack {
                    CalendarSettingsView(model: model, defaults: .builtIn)
                        .navigationTitle("זמנים ולוח שנה")
                        .navigationBarTitleDisplayMode(.inline)
                }
                .tabItem { Label("הגדרות", systemImage: "calendar") }
                .tag(Tab.calendar)

                NavigationStack {
                    AboutWidgetsView(isShared: model.location.isShared)
                        .navigationTitle("על הווידג'טים")
                        .navigationBarTitleDisplayMode(.inline)
                }
                .tabItem { Label("על הווידג'טים", systemImage: "info.circle") }
                .tag(Tab.about)
            }
            .hebrewRTL()
            .onOpenURL { url in
                guard url.scheme == "otzaria-widget" else { return }
                tab = Tab(rawValue: url.pathComponents.dropFirst().first ?? "") ?? .calendar
            }
        }
    }
}
