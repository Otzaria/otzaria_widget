import Foundation
import OtzariaCore
import WidgetKit

struct OtzariaEntry<Options: Sendable>: TimelineEntry {
    var date: Date
    var options: Options
    var snapshot: WidgetSnapshot?
    var settings: WidgetSettings

    func context(size: WidgetSize) -> WidgetContext {
        WidgetContext(snapshot: snapshot, settings: settings, now: date, size: size)
    }
}

enum TimelinePlan {
    /// Fallback refresh of the data widgets; the host app also reloads timelines when it writes a new snapshot.
    static let dataRefresh: TimeInterval = 15 * 60

    /// Entries at `now` and at each refresh moment, plus the date at which WidgetKit should ask again.
    static func dates(now: Date, moments: [Date], fallback: TimeInterval = dataRefresh) -> (entries: [Date], reloadAt: Date) {
        let later = moments.filter { $0 > now }.sorted()
        let entries = [now] + later
        return (entries, later.last ?? now.addingTimeInterval(fallback))
    }
}

struct OtzariaProvider<Intent: OptionsIntent>: AppIntentTimelineProvider {
    typealias Entry = OtzariaEntry<Intent.Options>

    func placeholder(in context: Context) -> Entry {
        Entry(date: .now, options: Intent.defaultOptions, snapshot: Fixtures.snapshot(now: .now), settings: WidgetSettings())
    }

    func snapshot(for configuration: Intent, in context: Context) async -> Entry {
        if context.isPreview {
            return Entry(date: .now, options: configuration.options, snapshot: Fixtures.snapshot(now: .now), settings: WidgetSettings())
        }
        return current(configuration, now: .now)
    }

    func timeline(for configuration: Intent, in context: Context) async -> Timeline<Entry> {
        let now = Date()
        let base = current(configuration, now: now)
        let ctx = base.context(size: WidgetSize(context.family))
        let plan = TimelinePlan.dates(now: now, moments: Intent.refreshDates(ctx, base.options))
        var entries = plan.entries.map { d -> Entry in var e = base; e.date = d; return e }
        if entries.isEmpty { entries = [base] }
        return Timeline(entries: entries, policy: .after(plan.reloadAt))
    }

    private func current(_ configuration: Intent, now: Date) -> Entry {
        Entry(date: now, options: configuration.options, snapshot: WidgetEnvironment.loadSnapshot(),
              settings: WidgetEnvironment.loadSettings())
    }
}
