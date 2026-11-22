import SwiftUI
import UniformTypeIdentifiers
import OtzariaCore
import OtzariaCalendar

struct MacSettingsView: View {
    @ObservedObject var model: SettingsModel
    @ObservedObject var agent: SnapshotAgent
    @ObservedObject var router: SettingsRouter

    var body: some View {
        NavigationSplitView {
            List(SettingsRouter.Page.allCases, selection: Binding(get: { router.page }, set: { router.page = $0 ?? .status })) { page in
                Label(page.title, systemImage: page.symbol).tag(page)
            }
            .navigationSplitViewColumnWidth(min: 180, ideal: 200)
        } detail: {
            switch router.page {
            case .status: StatusPage(model: model, agent: agent)
            case .location: LocationPage(model: model, agent: agent)
            case .calendar: CalendarSettingsView(model: model, defaults: defaults)
            case .behavior: BehaviorPage(model: model)
            case .about: AboutPage(model: model)
            }
        }
        .navigationTitle(router.page.title)
    }

    private var defaults: CalendarDefaults {
        let prefs = agent.snapshot?.preferences ?? OtzariaPreferences()
        return CalendarDefaults(
            city: prefs.selectedCity, zmanim: prefs.enabledZmanim.isEmpty ? ZmanimRegistry.defaultEnabled : prefs.enabledZmanim,
            twelveHour: prefs.twelveHourClock, dayTransition: prefs.dayTransition, followLabel: "כמו באוצריא")
    }
}

// MARK: Status

private struct StatusPage: View {
    @ObservedObject var model: SettingsModel
    @ObservedObject var agent: SnapshotAgent

    var body: some View {
        Form {
            if !model.location.isShared {
                Section {
                    Label("הבנייה אינה חתומה בידי צוות מפתחים של Apple (חתימה אד-הוק או ללא חתימה): ייתכן שהווידג'טים לא יראו את נתוני אוצריא", systemImage: "exclamationmark.triangle")
                        .foregroundStyle(.orange)
                }
            }
            Section("מה זוהה במחשב") {
                if let snap = agent.snapshot {
                    pathRow("תיקיית הנתונים", snap.paths.dataRoot)
                    pathRow("user_state.db", snap.paths.userStateDb)
                    pathRow("app_preferences.hive", snap.paths.preferencesHive)
                    pathRow("seforim.db", snap.paths.libraryDb)
                    pathRow("אפליקציית אוצריא", snap.paths.otzariaApp)
                    LabeledContent("גרסה ניידת", value: snap.paths.isPortable ? "כן" : "לא")
                } else {
                    ProgressView()
                }
            }
            if let snap = agent.snapshot {
                Section("כמה נמצאו") {
                    LabeledContent("ספרים אחרונים", value: "\(snap.recentBooks.count)")
                    LabeledContent("סימניות", value: "\(snap.bookmarks.count)")
                    LabeledContent("ספרים ב\"שמור וזכור\"", value: "\(snap.progress.count)")
                    LabeledContent("חיפושים", value: "\(snap.recentSearches.count)")
                }
                Section("מצב מקורות הנתונים") {
                    ForEach(snap.status, id: \.name) { source in
                        HStack(alignment: .top) {
                            Image(systemName: source.health == .ok ? "checkmark.circle.fill" : "exclamationmark.circle.fill")
                                .foregroundStyle(source.health == .ok ? .green : .orange)
                            VStack(alignment: .leading) {
                                Text(source.name).environment(\.layoutDirection, .leftToRight)
                                if let message = source.message { Text(message).font(.footnote).foregroundStyle(.secondary) }
                            }
                        }
                    }
                }
                Section {
                    DisclosureGroup("פרטי הזיהוי") {
                        ForEach(snap.paths.diagnostics, id: \.self) { Text($0).font(.footnote).textSelection(.enabled) }
                    }
                }
            }
            if let error = agent.lastError ?? model.saveError {
                Section { Label(error, systemImage: "xmark.octagon").foregroundStyle(.red) }
            }
            Section {
                HStack {
                    Button("רענן") { agent.refresh(force: true) }
                    Button("פתח את אוצריא") { OtzariaLauncher.openOtzaria(appPath: agent.snapshot?.paths.otzariaApp) }
                }
                if let last = agent.lastWrite {
                    Text("הווידג'טים עודכנו \(last.formatted(date: .omitted, time: .standard))")
                        .font(.footnote).foregroundStyle(.secondary)
                }
            }
            Section("איך מוסיפים ווידג'טים") {
                Text("לחצו לחיצה ימנית על שולחן העבודה או פתחו את מרכז ההודעות, בחרו \"ערוך ווידג'טים\" וחפשו \"אוצריא\".")
            }
        }
        .formStyle(.grouped)
    }

    private func pathRow(_ title: String, _ path: String?) -> some View {
        LabeledContent(title) {
            Text(path ?? "לא נמצא")
                .textSelection(.enabled).lineLimit(2).truncationMode(.middle)
                .foregroundStyle(path == nil ? .secondary : .primary)
                .environment(\.layoutDirection, .leftToRight)
        }
    }
}

// MARK: Data location

private struct LocationPage: View {
    @ObservedObject var model: SettingsModel
    @ObservedObject var agent: SnapshotAgent

    var body: some View {
        Form {
            Section {
                Text("שדה ריק = זיהוי אוטומטי.").foregroundStyle(.secondary)
            }
            field("תיקיית הנתונים של אוצריא", \.dataRootOverride, effective: agent.snapshot?.paths.dataRoot, directory: true)
            field("תיקיית מסדי הנתונים", \.databasesDirOverride, effective: agent.snapshot?.paths.userStateDb.map { ($0 as NSString).deletingLastPathComponent }, directory: true)
            field("קובץ ספריית הספרים (seforim.db)", \.libraryDbOverride, effective: agent.snapshot?.paths.libraryDb, directory: false)
            field("אפליקציית אוצריא", \.otzariaAppOverride, effective: agent.snapshot?.paths.otzariaApp, directory: false, app: true)
            Section {
                Button("אפס את כל המיקומים לזיהוי אוטומטי", role: .destructive) {
                    model.update {
                        $0.dataRootOverride = nil
                        $0.databasesDirOverride = nil
                        $0.libraryDbOverride = nil
                        $0.otzariaAppOverride = nil
                    }
                }
            }
        }
        .formStyle(.grouped)
    }

    private func field(_ title: String, _ key: WritableKeyPath<WidgetSettings, String?>, effective: String?, directory: Bool, app: Bool = false) -> some View {
        Section(title) {
            HStack {
                PathField(title: title, key: key, model: model)
                Button("עיון…") {
                    if let path = Self.choose(directory: directory, app: app) { model.update { $0[keyPath: key] = path } }
                }
                Button("זיהוי אוטומטי") { model.update { $0[keyPath: key] = nil } }
            }
            if let effective {
                Text("בשימוש: \(effective)").font(.footnote).foregroundStyle(.secondary)
                    .textSelection(.enabled).environment(\.layoutDirection, .leftToRight)
            }
        }
    }

    /// Edits a path locally and saves it on Return or focus loss, not on every keystroke (each save reloads the widgets
    /// and re-reads Otzaria's files).
    private struct PathField: View {
        let title: String
        let key: WritableKeyPath<WidgetSettings, String?>
        @ObservedObject var model: SettingsModel
        @State private var text = ""
        @FocusState private var focused: Bool

        var body: some View {
            TextField(title, text: $text, prompt: Text("זיהוי אוטומטי"))
                .labelsHidden().environment(\.layoutDirection, .leftToRight)
                .focused($focused)
                .onAppear { text = saved }
                .onSubmit(commit)
                .onChange(of: focused) { _, isFocused in if !isFocused { commit() } }
                .onChange(of: saved) { _, value in text = value } // "עיון…" / "זיהוי אוטומטי" win over a pending edit
                .onDisappear(perform: commit)
        }

        private var saved: String { model.settings[keyPath: key] ?? "" }

        private func commit() {
            let value = text.trimmingCharacters(in: .whitespacesAndNewlines)
            guard value != saved else { return }
            model.update { $0[keyPath: key] = value.isEmpty ? nil : value }
        }
    }

    private static func choose(directory: Bool, app: Bool) -> String? {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = directory
        panel.canChooseFiles = !directory
        panel.allowsMultipleSelection = false
        if app { panel.allowedContentTypes = [.application] }
        return panel.runModal() == .OK ? panel.url?.path : nil
    }
}

// MARK: Behaviour

private struct BehaviorPage: View {
    @ObservedObject var model: SettingsModel
    @State private var loginEnabled = LoginItem.isEnabled
    @State private var loginError: String?

    var body: some View {
        Form {
            Section("הפעלה") {
                Toggle("הפעל את שירות הווידג'טים בכניסה למחשב", isOn: Binding(
                    get: { loginEnabled },
                    set: { on in
                        do {
                            try LoginItem.set(on)
                            loginError = nil
                        } catch {
                            loginError = "לא ניתן היה לשנות את ההגדרה"
                        }
                        loginEnabled = LoginItem.isEnabled
                    }))
                Text("השירות רץ בשורת התפריטים וקורא את נתוני אוצריא כדי לעדכן את הווידג'טים.")
                    .font(.footnote).foregroundStyle(.secondary)
                if LoginItem.needsApproval {
                    Text("נדרש אישור ב: הגדרות המערכת ← כללי ← פריטי התחברות.").font(.footnote).foregroundStyle(.orange)
                }
                if let loginError { Text(loginError).font(.footnote).foregroundStyle(.red) }
            }
            Section("עיצוב") {
                Picker("ערכת נושא", selection: model.binding(\.theme)) {
                    Text("לפי המערכת").tag(ThemeMode.followSystem)
                    Text("בהיר").tag(ThemeMode.light)
                    Text("כהה").tag(ThemeMode.dark)
                }
                Toggle("צבע הדגשה מותאם", isOn: Binding(
                    get: { model.settings.accentColor != nil },
                    set: { on in model.update { $0.accentColor = on ? "#B8860B" : nil } }))
                if model.settings.accentColor != nil {
                    ColorPicker("צבע הדגשה", selection: Binding(
                        get: { Color(hex: model.settings.accentColor) },
                        set: { color in model.update { $0.accentColor = color.hexString } }), supportsOpacity: false)
                }
            }
            Section("פרטיות") {
                Toggle("הסתר חיפושים מהווידג'טים", isOn: model.binding(\.hideSearches))
                Text("חיפושים אחרונים לא יוצגו באף ווידג'ט.").font(.footnote).foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
    }
}

private extension Color {
    init(hex: String?) {
        let digits = (hex ?? "").trimmingCharacters(in: CharacterSet(charactersIn: "#"))
        guard digits.count == 6, let value = UInt32(digits, radix: 16) else { self = .accentColor; return }
        self.init(red: Double(value >> 16 & 0xFF) / 255, green: Double(value >> 8 & 0xFF) / 255, blue: Double(value & 0xFF) / 255)
    }

    var hexString: String {
        let c = NSColor(self).usingColorSpace(.sRGB) ?? .black
        return String(format: "#%02X%02X%02X", Int((c.redComponent * 255).rounded()), Int((c.greenComponent * 255).rounded()),
                      Int((c.blueComponent * 255).rounded()))
    }
}

// MARK: About

private struct AboutPage: View {
    @ObservedObject var model: SettingsModel

    private var version: String {
        let info = Bundle.main.infoDictionary
        return "\(info?["CFBundleShortVersionString"] as? String ?? "?") (\(info?["CFBundleVersion"] as? String ?? "?"))"
    }

    var body: some View {
        Form {
            Section {
                LabeledContent("גרסה", value: version)
            }
            Section("פרטיות") {
                Text("הווידג'ט קורא בלבד, ואינו כותב לקבצי אוצריא.")
                Text("שום מידע אינו נשלח לשום מקום: אין שרתים, אין איסוף נתונים ואין חשבון.")
                Text("מתוך העדפות אוצריא נקרא רק מספר קטן של מפתחות (עיר, הגדרות זמנים ושעון, צבעים, נתיבי ספרייה, והתקדמות \"שמור וזכור\"). בקובצי הלוג אין תוכן אישי.")
            }
            Section {
                Button("פתח את תיקיית הלוגים") {
                    try? FileManager.default.createDirectory(at: AppLog.directory, withIntermediateDirectories: true)
                    NSWorkspace.shared.open(AppLog.directory)
                }
                Button("בדוק קישור") { OtzariaLauncher.open(link: "otzaria://open/history") }
                LabeledContent("קובץ ההגדרות") {
                    Text(model.store.fileURL.path).textSelection(.enabled).lineLimit(2).truncationMode(.middle)
                        .environment(\.layoutDirection, .leftToRight)
                }
            }
        }
        .formStyle(.grouped)
    }
}
