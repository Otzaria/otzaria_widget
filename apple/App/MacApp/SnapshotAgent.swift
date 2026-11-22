import AppKit
import WidgetKit
import OtzariaCore

/// Polls Otzaria's files and keeps the widget snapshot in the App Group up to date. Read-only toward Otzaria.
@MainActor
final class SnapshotAgent: ObservableObject {
    static let pollInterval: TimeInterval = 45

    @Published private(set) var snapshot: OtzariaSnapshot?
    @Published private(set) var lastWrite: Date?
    @Published private(set) var lastError: String?

    private let model: SettingsModel
    private let service: OtzariaDataService
    private let snapshotStore: WidgetSnapshotStore
    private var lastWritten: WidgetSnapshot?
    private var timer: Timer?
    private var busy = false
    /// A forced refresh asked for while busy (e.g. a settings change) runs right after the current one.
    private var pendingForce = false

    init(model: SettingsModel) {
        self.model = model
        let settingsStore = model.store
        service = OtzariaDataService(settingsProvider: { settingsStore.load() })
        snapshotStore = WidgetSnapshotStore(directory: model.location.directory)
        lastWritten = snapshotStore.read()
        model.didChange = { [weak self] in self?.refresh(force: true) }
    }

    func start() {
        guard timer == nil else { return }
        refresh(force: true)
        timer = Timer.scheduledTimer(withTimeInterval: Self.pollInterval, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.refresh(force: false) }
        }
        // Polling needs no precision: let the system coalesce the wake-ups.
        timer?.tolerance = Self.pollInterval / 5
        NotificationCenter.default.addObserver(forName: NSApplication.didBecomeActiveNotification, object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor in self?.refresh(force: false) }
        }
    }

    /// `force` re-reads everything (first run, settings change, manual refresh); otherwise only when Otzaria's files changed.
    func refresh(force: Bool) {
        guard !busy else {
            pendingForce = pendingForce || force
            return
        }
        busy = true
        let service = service
        Task.detached(priority: .utility) { [weak self] in
            let fresh = force ? service.reload() : service.refreshIfChanged()
            let widget = fresh == nil ? nil : service.widgetSnapshot()
            await self?.finish(fresh: fresh, widget: widget)
        }
    }

    private func finish(fresh: OtzariaSnapshot?, widget: WidgetSnapshot?) {
        busy = false
        if pendingForce {
            pendingForce = false
            refresh(force: true)
        }
        guard let fresh, let widget else { return }
        snapshot = fresh
        var comparable = widget
        if let previous = lastWritten { comparable.generatedAt = previous.generatedAt }
        guard comparable != lastWritten else { return }
        do {
            try snapshotStore.write(widget)
            lastWritten = widget
            lastWrite = Date()
            lastError = nil
            WidgetCenter.shared.reloadAllTimelines()
            AppLog.write("snapshot written: recent=\(widget.recentBooks.count) bookmarks=\(widget.bookmarks.count) progress=\(widget.shamorZachor.count) health=\(widget.health.rawValue)")
        } catch {
            lastError = "לא ניתן היה לכתוב את קובץ הווידג'טים"
            AppLog.write("snapshot write failed: \(error.localizedDescription)")
        }
    }
}
