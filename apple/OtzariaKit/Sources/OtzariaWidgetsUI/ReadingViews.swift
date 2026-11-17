import SwiftUI
import WidgetKit

struct RecentBooksView: View {
    let entry: OtzariaEntry<RecentBooksOptions>
    @Environment(\.widgetFamily) private var widgetFamily
    @Environment(\.familyOverride) private var familyOverride
    private var family: WidgetFamily { familyOverride ?? widgetFamily }
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        let size = WidgetSize(family)
        let model = RecentBooksModel.make(entry.context(size: size), entry.options)
        let tint = Accent.color(entry.snapshot, scheme: scheme)
        // Small widgets have no links, so the whole widget opens the item (as on Windows).
        let url = size == .small ? model.hero?.url ?? model.url : model.url
        WidgetBody(title: "ספרים אחרונים", size: size, state: model.state, url: url, tint: tint) {
            if let hero = model.hero {
                if size == .small {
                    small(model, hero, tint)
                } else {
                    listContent(model, hero, size, tint)
                }
            }
        }
    }

    private func small(_ model: RecentBooksModel, _ hero: ReadingRow, _ tint: Color) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(model.heroLabel).font(.caption2.weight(.semibold)).foregroundStyle(tint)
            Text(hero.title).font(.headline).lineLimit(4).minimumScaleFactor(0.85)
            if !hero.sub.isEmpty { Text(hero.sub).font(.caption).foregroundStyle(.secondary).lineLimit(2) }
            if !hero.meta.isEmpty { Text(hero.meta).font(.caption2).foregroundStyle(.secondary) }
        }
    }

    @ViewBuilder private func listContent(_ model: RecentBooksModel, _ hero: ReadingRow, _ size: WidgetSize, _ tint: Color) -> some View {
        let dense = size == .medium ? model.slots > 3 : model.slots > 6
        RowLink(destination: hero.url) {
            VStack(alignment: .leading, spacing: 1) {
                Text(model.heroLabel).font(.caption2.weight(.semibold)).foregroundStyle(tint)
                HStack(alignment: .firstTextBaseline) {
                    Text(hero.title).font(size == .large ? .headline : .subheadline.weight(.semibold)).lineLimit(1)
                    Spacer(minLength: 4)
                    if !hero.meta.isEmpty { Text(hero.meta).font(.caption2).foregroundStyle(.secondary) }
                }
                if !hero.sub.isEmpty { Text(hero.sub).font(.caption).foregroundStyle(.secondary).lineLimit(1) }
            }
        }
        if !model.rows.isEmpty { Divider() }
        ForEach(model.rows.indices, id: \.self) { i in
            RowLink(destination: model.rows[i].url) { ReadingRowView(row: model.rows[i], dense: dense) }
        }
        ForEach(model.searches.indices, id: \.self) { i in
            let s = model.searches[i]
            RowLink(destination: s.url) {
                HStack(spacing: 5) {
                    Image(systemName: "magnifyingglass").font(.caption2).foregroundStyle(tint)
                    Text(s.text).font(.caption).lineLimit(1)
                    Spacer(minLength: 4)
                    if !s.sub.isEmpty { Text(s.sub).font(.caption2).foregroundStyle(.secondary) }
                }
            }
        }
    }
}

struct BookmarksView: View {
    let entry: OtzariaEntry<BookmarksOptions>
    @Environment(\.widgetFamily) private var widgetFamily
    @Environment(\.familyOverride) private var familyOverride
    private var family: WidgetFamily { familyOverride ?? widgetFamily }
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        let size = WidgetSize(family)
        let model = BookmarksModel.make(entry.context(size: size), entry.options)
        let tint = Accent.color(entry.snapshot, scheme: scheme)
        let dense = size == .medium ? model.rows.count > 3 : model.rows.count > 6
        WidgetBody(title: "סימניות", headerEnd: model.groupName, size: size, state: model.state, url: model.url, tint: tint) {
            ForEach(model.rows.indices, id: \.self) { i in
                RowLink(destination: model.rows[i].url) {
                    HStack(spacing: 6) {
                        Image(systemName: "bookmark.fill").font(.caption2).foregroundStyle(tint)
                        ReadingRowView(row: model.rows[i], dense: dense && size == .large, trailingShort: size == .medium)
                    }
                }
            }
        }
    }
}

struct ShamorZachorView: View {
    let entry: OtzariaEntry<ShamorZachorOptions>
    @Environment(\.widgetFamily) private var widgetFamily
    @Environment(\.familyOverride) private var familyOverride
    private var family: WidgetFamily { familyOverride ?? widgetFamily }
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        let size = WidgetSize(family)
        let model = ShamorZachorModel.make(entry.context(size: size), entry.options)
        let tint = Accent.color(entry.snapshot, scheme: scheme)
        let url = size == .small ? model.rows.first?.url ?? model.url : model.url
        WidgetBody(title: "שמור וזכור", headerEnd: size == .large ? model.headerEnd : model.subtitle, size: size,
                   state: model.state, url: url, tint: tint) {
            switch size {
            case .small:
                if let r = model.rows.first { small(r, tint) }
            case .medium:
                ForEach(model.rows.indices, id: \.self) { i in
                    let r = model.rows[i]
                    RowLink(destination: r.url) {
                        HStack(spacing: 8) {
                            Text(r.title).font(.subheadline).lineLimit(1)
                            Spacer(minLength: 2)
                            ProgressBar(ratio: r.ratio, complete: r.complete, tint: tint).frame(width: 64)
                            Text(r.meta).font(.caption.weight(.bold)).frame(minWidth: 38, alignment: .trailing)
                        }
                    }
                }
            case .large:
                ForEach(model.rows.indices, id: \.self) { i in large(model.rows[i], tint) }
                if !model.note.isEmpty { Text(model.note).font(.caption2).foregroundStyle(.secondary) }
            }
        }
    }

    private func small(_ r: ProgressRow, _ tint: Color) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(r.meta).font(.largeTitle.weight(.bold)).minimumScaleFactor(0.6).lineLimit(1)
            Text(r.title).font(.subheadline.weight(.semibold)).lineLimit(3)
            Spacer(minLength: 2)
            ProgressBar(ratio: r.ratio, complete: r.complete, tint: tint)
        }
    }

    private func large(_ r: ProgressRow, _ tint: Color) -> some View {
        RowLink(destination: r.url) {
            VStack(alignment: .leading, spacing: 2) {
                HStack {
                    Text(r.title).font(.subheadline.weight(.medium)).lineLimit(1)
                    Spacer(minLength: 4)
                    Text(r.meta).font(.caption.weight(.bold))
                }
                HStack(spacing: 8) {
                    ProgressBar(ratio: r.ratio, complete: r.complete, tint: tint)
                    if !r.cycle.isEmpty { Text(r.cycle).font(.caption2).foregroundStyle(.secondary).fixedSize() }
                }
                if !r.sub.isEmpty { Text(r.sub).font(.caption2).foregroundStyle(.secondary).lineLimit(1) }
                ForEach(r.columns.indices, id: \.self) { j in
                    let c = r.columns[j]
                    HStack(spacing: 6) {
                        Text(c.label).font(.caption2).foregroundStyle(.secondary).frame(width: 52, alignment: .leading).lineLimit(1)
                        ProgressBar(ratio: c.ratio, complete: c.ratio >= 1, tint: tint, height: 3)
                        Text(c.pct).font(.caption2).frame(width: 34, alignment: .trailing)
                    }
                }
            }
        }
    }
}
