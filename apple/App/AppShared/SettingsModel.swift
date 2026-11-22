import SwiftUI
import WidgetKit
import OtzariaCore

/// The widget settings file in the App Group; every change is saved at once and the widgets are reloaded.
@MainActor
final class SettingsModel: ObservableObject {
    @Published private(set) var settings: WidgetSettings
    @Published private(set) var saveError: String?
    let location: AppGroupLocation
    let store: WidgetSettingsStore
    /// Called after each saved change (the Mac agent rewrites the snapshot from it).
    var didChange: (() -> Void)?

    init(location: AppGroupLocation) {
        self.location = location
        store = WidgetSettingsStore(directory: location.directory)
        settings = store.load()
    }

    func update(_ change: (inout WidgetSettings) -> Void) {
        var next = settings
        change(&next)
        guard next != settings else { return }
        settings = next
        do {
            try store.save(next)
            saveError = nil
        } catch {
            saveError = "לא ניתן היה לשמור את ההגדרות"
        }
        WidgetCenter.shared.reloadAllTimelines()
        didChange?()
    }

    func binding<T>(_ keyPath: WritableKeyPath<WidgetSettings, T>) -> Binding<T> {
        Binding(get: { self.settings[keyPath: keyPath] }, set: { value in self.update { $0[keyPath: keyPath] = value } })
    }
}

extension View {
    /// Hebrew, right to left, for the whole window.
    func hebrewRTL() -> some View {
        environment(\.layoutDirection, .rightToLeft).environment(\.locale, Locale(identifier: "he_IL"))
    }
}
