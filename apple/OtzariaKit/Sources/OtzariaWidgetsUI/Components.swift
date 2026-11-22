import SwiftUI
import WidgetKit
import OtzariaCore

extension View {
    /// Hebrew UI: right-to-left layout and he_IL formatting regardless of the device language.
    func otzariaLocale() -> some View {
        environment(\.layoutDirection, .rightToLeft).environment(\.locale, Locale(identifier: "he_IL"))
    }
}

enum Accent {
    static func color(_ snapshot: WidgetSnapshot?, scheme: ColorScheme) -> Color {
        let prefs = snapshot?.preferences
        if let hex = prefs?.accentColor, let c = parse(hex: hex) { return c }
        if let argb = scheme == .dark ? prefs?.darkSeedColorArgb : prefs?.lightSeedColorArgb {
            return Color(.sRGB, red: Double((argb >> 16) & 0xFF) / 255, green: Double((argb >> 8) & 0xFF) / 255,
                         blue: Double(argb & 0xFF) / 255)
        }
        return Color(red: 0.16, green: 0.38, blue: 0.62)
    }

    static func parse(hex: String) -> Color? {
        var s = hex.trimmingCharacters(in: .whitespaces)
        if s.hasPrefix("#") { s.removeFirst() }
        guard s.count == 6, let v = UInt32(s, radix: 16) else { return nil }
        return Color(.sRGB, red: Double((v >> 16) & 0xFF) / 255, green: Double((v >> 8) & 0xFF) / 255, blue: Double(v & 0xFF) / 255)
    }
}

struct ProgressBar: View {
    var ratio: Double
    var complete = false
    var tint: Color
    var height: CGFloat = 5

    var body: some View {
        let r = ratio.isNaN ? 0 : min(max(ratio, 0), 1)
        Capsule().fill(.secondary.opacity(0.25)).frame(height: height)
            .overlay(alignment: .leading) {
                GeometryReader { geo in
                    Capsule().fill(complete ? Color.green : tint).frame(width: max(r > 0 ? height : 0, geo.size.width * r))
                }
            }
            .accessibilityValue(HebrewFormat.percent(r))
    }
}

/// "בעוד 42 דקות", ticking without timeline entries.
struct CountdownText: View {
    var target: Date
    var body: some View { Text("בעוד ") + Text(target, style: .relative) }
}

struct SectionHeader: View {
    var title: String
    var trailing: String = ""

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 6) {
            Text(title).font(.caption.weight(.bold))
            Spacer(minLength: 4)
            if !trailing.isEmpty {
                Text(trailing).font(.caption2).foregroundStyle(.secondary).lineLimit(1).minimumScaleFactor(0.8)
            }
        }
    }
}

/// Shared chrome: header (not in small), state messages and the settings button for missing data.
struct WidgetBody<Content: View>: View {
    var title: String
    var headerEnd: String = ""
    var size: WidgetSize
    var state: ContentState
    var url: URL
    var tint: Color
    @ViewBuilder var content: () -> Content

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            if size != .small { SectionHeader(title: title, trailing: headerEnd) }
            switch state {
            case .ok:
                content()
            case .empty(let text):
                message(text, button: false)
            case .missing(let text):
                message(text, button: true)
            }
            Spacer(minLength: 0)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .widgetURL(state.isMissing ? WidgetLinks.url(WidgetLinks.settings) : url)
        .otzariaLocale()
    }

    @ViewBuilder private func message(_ text: String, button: Bool) -> some View {
        if size == .small { Text(title).font(.caption.weight(.bold)) }
        Text(text).font(size == .small ? .caption : .subheadline).foregroundStyle(.secondary).lineLimit(size == .small ? 6 : 4)
        if button, size != .small {
            RowLink(destination: WidgetLinks.url(WidgetLinks.settings)) {
                Text("פתח הגדרות").font(.caption.weight(.semibold)).padding(.horizontal, 10).padding(.vertical, 4)
                    .background(tint.opacity(0.15), in: Capsule()).foregroundStyle(tint)
            }
        }
    }
}

extension ContentState {
    var isMissing: Bool { if case .missing = self { return true } else { return false } }
}

/// Title with optional detail line and trailing meta, as in the Windows list rows.
struct ReadingRowView: View {
    var row: ReadingRow
    var dense = false
    var trailingShort = false

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 6) {
            VStack(alignment: .leading, spacing: 1) {
                Text(row.title).font(.subheadline.weight(.medium)).lineLimit(1)
                if !dense, !trailingShort, !row.sub.isEmpty {
                    Text(row.sub).font(.caption).foregroundStyle(.secondary).lineLimit(1)
                }
            }
            Spacer(minLength: 4)
            if trailingShort, !row.short.isEmpty {
                Text(row.short).font(.caption).foregroundStyle(.secondary).lineLimit(1)
            } else if dense, !row.sub.isEmpty {
                Text(row.sub).font(.caption).foregroundStyle(.secondary).lineLimit(1)
            } else if !row.meta.isEmpty {
                Text(row.meta).font(.caption2).foregroundStyle(.secondary).lineLimit(1)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

private struct FamilyOverrideKey: EnvironmentKey { static let defaultValue: WidgetFamily? = nil }

extension EnvironmentValues {
    /// `widgetFamily` is read-only; renders outside WidgetKit (tests, previews) set the size through this.
    var familyOverride: WidgetFamily? {
        get { self[FamilyOverrideKey.self] }
        set { self[FamilyOverrideKey.self] = newValue }
    }
}

private struct PlainLinksKey: EnvironmentKey { static let defaultValue = false }

extension EnvironmentValues {
    /// ImageRenderer cannot draw `Link`; renders outside WidgetKit turn links into plain content.
    var plainLinks: Bool {
        get { self[PlainLinksKey.self] }
        set { self[PlainLinksKey.self] = newValue }
    }
}

struct RowLink<Content: View>: View {
    var destination: URL
    @ViewBuilder var content: () -> Content
    @Environment(\.plainLinks) private var plain

    var body: some View {
        if plain { content() } else { Link(destination: destination, label: content) }
    }
}
