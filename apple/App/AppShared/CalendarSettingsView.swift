import SwiftUI
import OtzariaCore
import OtzariaCalendar

/// What applies when the user has not chosen anything: Otzaria's own values on the Mac, built-in defaults on iOS.
struct CalendarDefaults {
    var city: String
    var zmanim: [String]
    var twelveHour: Bool
    var dayTransition: String
    /// Label of the "no override" choice ("כמו באוצריא" / "ברירת מחדל").
    var followLabel: String

    static let builtIn = CalendarDefaults(
        city: CityTable.defaultCityName, zmanim: ZmanimRegistry.defaultEnabled, twelveHour: false,
        dayTransition: CalendarService.dayTransitionSunset, followLabel: "ברירת מחדל")
}

/// City, zmanim selection, clock format, day switch and Yerushalmi, with a live preview (the "זמנים ולוח שנה" page).
struct CalendarSettingsView: View {
    @ObservedObject var model: SettingsModel
    let defaults: CalendarDefaults

    @State private var cityFilter = ""
    @State private var zmanFilter = ""
    private let service = CalendarService()

    private static let transitions: [(id: String, label: String)] = [
        (CalendarService.dayTransitionSunset, "שקיעה"),
        (CalendarService.dayTransitionTzais, "צאת הכוכבים"),
        (CalendarService.dayTransitionRabbeinuTam, "רבנו תם"),
        (CalendarService.dayTransitionMidnight, "חצות"),
    ]

    var body: some View {
        Form {
            Section("עיר") { cityPicker }
            Section("הזמנים להצגה") { zmanimSection }
            Section("פורמט שעה") {
                Picker("פורמט שעה", selection: clockBinding) {
                    Text(defaults.followLabel).tag(0)
                    Text("24 שעות").tag(1)
                    Text("12 שעות").tag(2)
                }
                .labelsHidden()
            }
            Section("מעבר היום העברי") {
                Picker("מעבר היום העברי", selection: transitionBinding) {
                    Text(defaults.followLabel).tag("")
                    ForEach(Self.transitions, id: \.id) { Text($0.label).tag($0.id) }
                }
                .labelsHidden()
            }
            Section {
                Toggle("הצג דף יומי ירושלמי", isOn: model.binding(\.showYerushalmiDaf))
            }
            Section("תצוגה מקדימה להיום") { preview }
        }
        .formStyle(.grouped)
    }

    // MARK: Effective values

    private var effectiveCityName: String { model.settings.cityOverride.flatMap(nonBlank) ?? defaults.city }
    private var effectiveCity: CityInfo { service.findCity(effectiveCityName) ?? CityTable.default }
    private var effectiveTwelveHour: Bool { model.settings.twelveHourClockOverride ?? defaults.twelveHour }
    private var effectiveTransition: String { model.settings.dayTransitionOverride.flatMap(nonBlank) ?? defaults.dayTransition }
    private var effectiveZmanim: [String] { model.settings.zmanimOverride ?? defaults.zmanim }

    private func nonBlank(_ s: String) -> String? { s.trimmingCharacters(in: .whitespaces).isEmpty ? nil : s }

    // MARK: City

    private var cityPicker: some View {
        let selected = model.settings.cityOverride.flatMap(nonBlank) ?? ""
        let needle = cityFilter.trimmingCharacters(in: .whitespaces)
        let cities = service.cities.filter { needle.isEmpty || $0.name.contains(needle) || $0.name == selected }
        return VStack(alignment: .leading, spacing: 8) {
            TextField("סינון ערים", text: $cityFilter)
            Picker("עיר", selection: Binding(
                get: { selected },
                set: { value in model.update { $0.cityOverride = value.isEmpty ? nil : value } })
            ) {
                Text("\(defaults.followLabel) (\(defaults.city))").tag("")
                ForEach(cities, id: \.name) { Text($0.name).tag($0.name) }
            }
            .labelsHidden()
        }
    }

    // MARK: Zmanim

    private var zmanimSection: some View {
        let custom = model.settings.zmanimOverride != nil
        return VStack(alignment: .leading, spacing: 8) {
            Picker("הזמנים להצגה", selection: Binding(
                get: { custom },
                set: { on in model.update { $0.zmanimOverride = on ? effectiveZmanim : nil } })
            ) {
                Text(defaults.followLabel).tag(false)
                Text("בחירה ידנית").tag(true)
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            if custom {
                TextField("חיפוש זמן", text: $zmanFilter)
                HStack {
                    Button("בחר הכול") { model.update { $0.zmanimOverride = ZmanimRegistry.all.map(\.id) } }
                    Button("נקה הכול") { model.update { $0.zmanimOverride = [] } }
                    Button("ברירת מחדל") { model.update { $0.zmanimOverride = ZmanimRegistry.defaultEnabled } }
                }
                ForEach(filteredZmanim, id: \.id) { entry in
                    Toggle(entry.fullName, isOn: zmanBinding(entry.id))
                }
            } else {
                Text("מוצגים \(effectiveZmanim.count) זמנים")
                    .font(.footnote).foregroundStyle(.secondary)
            }
        }
    }

    private var filteredZmanim: [ZmanRegistryEntry] {
        let needle = zmanFilter.trimmingCharacters(in: .whitespaces)
        return ZmanimRegistry.all.filter { needle.isEmpty || $0.fullName.contains(needle) || $0.category.contains(needle) }
    }

    private func zmanBinding(_ id: String) -> Binding<Bool> {
        Binding(
            get: { effectiveZmanim.contains(id) },
            set: { on in
                model.update { s in
                    var list = s.zmanimOverride ?? defaults.zmanim
                    list.removeAll { $0 == id }
                    if on { list.append(id) }
                    // keep Otzaria's order
                    s.zmanimOverride = list.sorted { ZmanimRegistry.indexOf($0) < ZmanimRegistry.indexOf($1) }
                }
            })
    }

    // MARK: Other bindings

    private var clockBinding: Binding<Int> {
        Binding(
            get: { model.settings.twelveHourClockOverride.map { $0 ? 2 : 1 } ?? 0 },
            set: { value in model.update { $0.twelveHourClockOverride = value == 0 ? nil : value == 2 } })
    }

    private var transitionBinding: Binding<String> {
        Binding(
            get: { model.settings.dayTransitionOverride.flatMap(nonBlank) ?? "" },
            set: { value in model.update { $0.dayTransitionOverride = value.isEmpty ? nil : value } })
    }

    // MARK: Preview

    @ViewBuilder private var preview: some View {
        let city = effectiveCity
        let now = Date()
        let day = service.dayInfo(for: service.effectiveDate(now: now, city: city, dayTransition: effectiveTransition), city: city)
        let times = service.zmanim(for: day.date, city: city, ids: effectiveZmanim).prefix(6)
        VStack(alignment: .leading, spacing: 4) {
            Text(city.name).font(.headline)
            Text("\(day.hebrewDayOfWeek) · \(day.hebrewDate)")
            Text(day.gregorianDate).foregroundStyle(.secondary)
            if let parasha = day.parasha { Text(parasha) }
            ForEach(day.holidays, id: \.self) { Text($0) }
        }
        ForEach(Array(times), id: \.id) { zman in
            HStack {
                Text(zman.title)
                Spacer()
                Text(service.displayText(for: zman, city: city, twelveHour: effectiveTwelveHour) ?? "—")
                    .monospacedDigit().environment(\.layoutDirection, .leftToRight)
            }
        }
    }
}
