import SwiftUI
import WidgetKit

private struct ZmanLine: View {
    var row: ZmanRow
    var tint: Color

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack {
                Text(row.name).lineLimit(1)
                Spacer(minLength: 4)
                Text(row.timeText).monospacedDigit().environment(\.layoutDirection, .leftToRight)
            }
            .font(.subheadline.weight(row.isNext ? .bold : .regular))
            .foregroundStyle(row.isNext ? tint : Color.primary)
            if row.isNext, let t = row.time {
                CountdownText(target: t).font(.caption2).foregroundStyle(.secondary)
            } else if !row.note.isEmpty {
                Text(row.note).font(.caption2).foregroundStyle(.secondary).lineLimit(1)
            }
        }
        .opacity(row.isPast ? 0.45 : 1)
    }
}

struct ZmanimView: View {
    let entry: OtzariaEntry<ZmanimOptions>
    @Environment(\.widgetFamily) private var widgetFamily
    @Environment(\.familyOverride) private var familyOverride
    private var family: WidgetFamily { familyOverride ?? widgetFamily }
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        let size = WidgetSize(family)
        let model = ZmanimModel.make(entry.context(size: size), entry.options)
        let tint = Accent.color(entry.snapshot, scheme: scheme)
        WidgetBody(title: "זמני היום", headerEnd: model.headerEnd, size: size, state: model.state, url: model.url, tint: tint) {
            if let next = model.next {
                switch size {
                case .small: small(model, next, tint)
                case .medium: medium(model, next, tint)
                case .large: ForEach(model.rows.indices, id: \.self) { ZmanLine(row: model.rows[$0], tint: tint) }
                }
            } else if size == .large, !model.rows.isEmpty {
                ForEach(model.rows.indices, id: \.self) { ZmanLine(row: model.rows[$0], tint: tint) }
            } else {
                Text("אין זמנים נוספים היום").font(.subheadline).foregroundStyle(.secondary)
            }
        }
    }

    private func hero(_ model: ZmanimModel, _ next: ZmanRow, _ tint: Color, big: Font) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(model.tomorrow ? "הזמן הבא · מחר" : "הזמן הבא").font(.caption2).foregroundStyle(.secondary)
            Text(next.timeText).font(big.weight(.bold)).monospacedDigit().foregroundStyle(tint)
                .environment(\.layoutDirection, .leftToRight).minimumScaleFactor(0.7).lineLimit(1)
            Text(next.name).font(.subheadline.weight(.semibold)).lineLimit(2)
            if let t = next.time { CountdownText(target: t).font(.caption).foregroundStyle(.secondary) }
        }
    }

    private func small(_ model: ZmanimModel, _ next: ZmanRow, _ tint: Color) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(model.cityName).font(.caption2).foregroundStyle(.secondary)
            hero(model, next, tint, big: .system(.largeTitle))
        }
    }

    private func medium(_ model: ZmanimModel, _ next: ZmanRow, _ tint: Color) -> some View {
        HStack(alignment: .top, spacing: 14) {
            hero(model, next, tint, big: .system(.title))
            VStack(spacing: 5) {
                ForEach(model.after.indices, id: \.self) { i in
                    HStack {
                        Text(model.after[i].name).lineLimit(1)
                        Spacer(minLength: 4)
                        Text(model.after[i].timeText).monospacedDigit().environment(\.layoutDirection, .leftToRight)
                    }
                    .font(.subheadline)
                }
            }
            .frame(maxWidth: .infinity)
        }
    }
}

struct HebrewCalendarView: View {
    let entry: OtzariaEntry<CalendarOptions>
    @Environment(\.widgetFamily) private var widgetFamily
    @Environment(\.familyOverride) private var familyOverride
    private var family: WidgetFamily { familyOverride ?? widgetFamily }
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        let size = WidgetSize(family)
        let model = HebrewCalendarModel.make(entry.context(size: size), entry.options)
        let tint = Accent.color(entry.snapshot, scheme: scheme)
        WidgetBody(title: "לוח עברי", headerEnd: model.cityName, size: size, state: model.state, url: model.url, tint: tint) {
            switch size {
            case .small:
                VStack(alignment: .leading, spacing: 2) {
                    Text(model.weekday).font(.caption).foregroundStyle(.secondary)
                    Text(model.day).font(.title2.weight(.bold)).foregroundStyle(tint).lineLimit(1).minimumScaleFactor(0.7)
                    Text(model.gregorian).font(.caption).foregroundStyle(.secondary).lineLimit(1).minimumScaleFactor(0.8)
                    Spacer(minLength: 2)
                    ForEach(model.lines.indices, id: \.self) { lineView(model.lines[$0], tint) }
                }
            case .medium:
                HStack(alignment: .top, spacing: 14) {
                    VStack(alignment: .leading, spacing: 2) {
                        Text(model.day).font(.title.weight(.bold)).foregroundStyle(tint).lineLimit(1).minimumScaleFactor(0.7)
                        Text([model.weekday, model.year].filter { !$0.isEmpty }.joined(separator: " · "))
                            .font(.caption).foregroundStyle(.secondary).lineLimit(1)
                        Text(model.gregorian).font(.caption).foregroundStyle(.secondary).lineLimit(1)
                    }
                    VStack(alignment: .leading, spacing: 5) {
                        ForEach(model.lines.indices, id: \.self) { lineView(model.lines[$0], tint) }
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                }
            case .large:
                VStack(alignment: .leading, spacing: 4) {
                    HStack(alignment: .firstTextBaseline) {
                        Text(model.day).font(.system(size: 34, weight: .bold)).foregroundStyle(tint).lineLimit(1).minimumScaleFactor(0.6)
                        Spacer(minLength: 4)
                        Text(model.year).font(.subheadline).foregroundStyle(.secondary)
                    }
                    Text([model.weekday, model.gregorian].joined(separator: " · ")).font(.subheadline).foregroundStyle(.secondary)
                    Divider().padding(.vertical, 4)
                    ForEach(model.lines.indices, id: \.self) { lineView(model.lines[$0], tint) }
                }
            }
        }
    }

    private func lineView(_ line: CalendarLine, _ tint: Color) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 6) {
            if !line.label.isEmpty { Text(line.label).font(.caption).foregroundStyle(.secondary) }
            Text(line.text).font(.subheadline.weight(line.emphasize ? .bold : .regular))
                .foregroundStyle(line.emphasize ? tint : Color.primary).lineLimit(2).minimumScaleFactor(0.85)
        }
    }
}

struct DashboardView: View {
    let entry: OtzariaEntry<DashboardOptions>
    @Environment(\.widgetFamily) private var widgetFamily
    @Environment(\.familyOverride) private var familyOverride
    private var family: WidgetFamily { familyOverride ?? widgetFamily }
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        let size = WidgetSize(family)
        let model = DashboardModel.make(entry.context(size: size), entry.options)
        let tint = Accent.color(entry.snapshot, scheme: scheme)
        WidgetBody(title: "לוח אישי", size: size, state: model.state, url: model.url, tint: tint) {
            ForEach(model.sections.indices, id: \.self) { i in
                let section = model.sections[i]
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Text(section.lead).font(.caption2).foregroundStyle(.secondary).frame(width: 52, alignment: .leading).lineLimit(1)
                    VStack(alignment: .leading, spacing: 3) {
                        ForEach(section.lines.indices, id: \.self) { j in lineView(section.lines[j], tint) }
                    }
                }
            }
        }
    }

    private func lineView(_ line: DashLine, _ tint: Color) -> some View {
        RowLink(destination: line.url) {
            VStack(alignment: .leading, spacing: 1) {
                HStack(alignment: .firstTextBaseline, spacing: 4) {
                    Text(line.text).font(.subheadline.weight(line.emphasize ? .bold : .regular))
                        .foregroundStyle(line.accent ? tint : Color.primary).lineLimit(1)
                    Spacer(minLength: 4)
                    if !line.meta.isEmpty { Text(line.meta).font(.caption).foregroundStyle(.secondary).lineLimit(1) }
                }
                if let target = line.countdownTo {
                    (Text(line.tomorrow ? "מחר · " : "") + Text("בעוד ") + Text(target, style: .relative))
                        .font(.caption2).foregroundStyle(.secondary)
                } else if !line.sub.isEmpty {
                    Text(line.sub).font(.caption2).foregroundStyle(.secondary).lineLimit(1)
                }
                if let ratio = line.bar { ProgressBar(ratio: ratio, tint: tint, height: 4) }
            }
        }
    }
}
