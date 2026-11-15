import Foundation

/// Resolves Otzaria's file locations on macOS (storage-and-links.md section 1, mirroring `lib/core/app_paths.dart`).
/// Read-only; never throws.
public struct OtzariaPathResolver: Sendable {
    public static let bundledLibraryFolderName = "אוצריא"
    public static let bundledLibraryMarker = ".otzaria_bundled_library"
    public static let systemWideRoot = "/Library/Application Support/otzaria"

    private let env: ResolverEnvironment
    private let readPreferences: @Sendable (String) -> OtzariaPreferences?

    /// - Parameters:
    ///   - environment: file system / environment abstraction (fakes in tests).
    ///   - preferencesReader: reads the Hive preferences; nil means "no prefs". Default: `PreferencesReader`.
    public init(
        environment: ResolverEnvironment = SystemResolverEnvironment(),
        preferencesReader: (@Sendable (String) -> OtzariaPreferences?)? = nil
    ) {
        env = environment
        readPreferences = preferencesReader ?? { PreferencesReader().read(path: $0) }
    }

    public func resolve(settings: WidgetSettings = WidgetSettings()) -> OtzariaPaths {
        var notes: [String] = []
        return resolveCore(settings, &notes)
    }

    // MARK: Core

    private struct Capabilities {
        var exePath: String?
        var portable: Bool
        var libraryDbPath: String?
    }

    private func resolveCore(_ settings: WidgetSettings, _ notes: inout [String]) -> OtzariaPaths {
        let home = clean(env.homeDirectory)
        let defaultRoot = home.map { join($0, "Library/Application Support/otzaria") }

        // ---- 1. dataRoot ----
        var dataRoot: String?
        var portable = false
        var portableExe: String?

        if let ov = clean(settings.dataRootOverride) {
            if env.directoryExists(ov) {
                dataRoot = ov
                notes.append("תיקיית הנתונים נקבעה ידנית בהגדרות: \(ov)")
            } else {
                notes.append("תיקיית הנתונים שהוגדרה ידנית אינה קיימת: \(ov)")
            }
        }
        if dataRoot == nil, let root = defaultRoot, looksLikeDataRoot(root) {
            dataRoot = root
            notes.append("תיקיית הנתונים: \(root) (Application Support/otzaria)")
        }

        let capsFromDefault = defaultRoot.flatMap { readCapabilities(join($0, "otzaria_capabilities.json")) }
        if dataRoot == nil {
            for (candidate, source) in appCandidates(settings, capsFromDefault, home) {
                guard let exeDir = executableDirectory(of: candidate),
                      env.fileExists(join(exeDir, "portable.marker")) else { continue }
                let data = join(exeDir, "otzaria_data")
                if env.directoryExists(data) {
                    dataRoot = data
                    portable = true
                    portableExe = candidate
                    notes.append("מצב נייד: תיקיית הנתונים \(data) (נמצא portable.marker ליד \(candidate), מקור: \(source))")
                    break
                }
            }
        }
        if dataRoot == nil, let root = defaultRoot, env.directoryExists(root) {
            dataRoot = root
            notes.append("תיקיית הנתונים: \(root) (קיימת אך ריקה מקובצי אוצריא מוכרים)")
        }
        if dataRoot == nil {
            notes.append("לא נמצאה תיקיית נתונים של אוצריא (Application Support/otzaria או תיקיית otzaria_data של גרסה ניידת)")
        }

        // ---- 2. capabilities and library_*.txt ----
        var caps: Capabilities?
        var libraryPathTxt: String?
        var libraryDbDirTxt: String?
        if let root = dataRoot {
            caps = readCapabilities(join(root, "otzaria_capabilities.json"))
            libraryPathTxt = readPathFile(join(root, "library_path.txt"))
            libraryDbDirTxt = readPathFile(join(root, "library_database_path.txt"))
            if !portable, caps?.portable == true { portable = true }
        }
        // Capabilities of ANOTHER data root must not leak in.
        if dataRoot == nil || samePath(dataRoot, defaultRoot) { caps = caps ?? capsFromDefault }

        // ---- 3. preferences ----
        var hive: String?
        var prefs: OtzariaPreferences?
        if let root = dataRoot {
            let hivePath = join(root, "app_preferences.hive")
            if env.fileExists(hivePath) {
                hive = hivePath
                prefs = readPreferences(hivePath)
                if prefs == nil { notes.append("לא ניתן היה לקרוא את app_preferences.hive; משתמש בקובצי הטקסט ובברירות המחדל") }
            } else {
                notes.append("לא נמצא app_preferences.hive בתיקיית הנתונים")
            }
        }

        // ---- 4. libraryPath (bundled FULL package wins unless the user chose another valid library) ----
        let bundled = detectBundledLibrary(appCandidates(settings, caps ?? capsFromDefault, home).map(\.0))
        let prefFolder = clean(prefs?.libraryFolderName)
        var libraryPath = clean(prefs?.libraryPath)
        if let bundled {
            if let p = libraryPath, isUserChosenLibrary(p, folderName: prefFolder) {
                notes.append("נתיב הספרייה מתוך ההגדרות של אוצריא (בחירה ידנית)")
            } else {
                libraryPath = bundled
                notes.append("ספרייה מצורפת ליד האפליקציה: \(bundled)")
            }
        } else if libraryPath != nil {
            notes.append("נתיב הספרייה מתוך ההגדרות של אוצריא (key-library-path)")
        } else if let txt = libraryPathTxt {
            libraryPath = txt
            notes.append("נתיב הספרייה מתוך library_path.txt")
        } else if let root = dataRoot {
            libraryPath = join(root, "books")
            notes.append("נתיב הספרייה: ברירת מחדל (<תיקיית נתונים>/books)")
        }

        // ---- 5. databases directory, per file ----
        var dbDirs: [(String, String)] = []
        append(&dbDirs, clean(settings.databasesDirOverride), "הגדרות הווידג'ט")
        append(&dbDirs, clean(prefs?.databasesPath), "key-databases-path")
        if let root = dataRoot { dbDirs.append((join(root, "databases"), "<תיקיית נתונים>/databases")) }
        if let lib = libraryPath, let parent = dirName(lib) { dbDirs.append((join(parent, "databases"), "<תיקיית הספרייה>/../databases")) }
        dbDirs.append((join(Self.systemWideRoot, "databases"), "התקנה מערכתית"))

        let userState = findInDirs("user_state.db", dbDirs, &notes)
        let personalNotes = findInDirs("personal_notes.db", dbDirs, &notes)
        let userBooks = findInDirs("user_books.db", dbDirs, &notes)

        // ---- 6. seforim.db ----
        let libraryDb = resolveLibraryDb(settings, caps, libraryDbDirTxt, libraryPath, prefFolder, libraryPathTxt, dataRoot, bundled, &notes)

        // ---- 7. Otzaria.app ----
        let app = resolveApp(settings, caps, portableExe, home, &notes)

        return OtzariaPaths(dataRoot: dataRoot, userStateDb: userState, personalNotesDb: personalNotes, userBooksDb: userBooks,
                            preferencesHive: hive, libraryDb: libraryDb, otzariaApp: app, isPortable: portable, diagnostics: notes)
    }

    // MARK: Candidates

    private func findInDirs(_ file: String, _ dirs: [(String, String)], _ notes: inout [String]) -> String? {
        for (dir, source) in dirs {
            let p = join(dir, file)
            if env.fileExists(p) {
                notes.append("\(file): \(p) (מקור: \(source))")
                return p
            }
        }
        notes.append("לא נמצא קובץ \(file)")
        return nil
    }

    private func resolveLibraryDb(
        _ settings: WidgetSettings, _ caps: Capabilities?, _ dbDirTxt: String?, _ libraryPath: String?, _ folder: String?,
        _ libraryPathTxt: String?, _ dataRoot: String?, _ bundled: String?, _ notes: inout [String]
    ) -> String? {
        var candidates: [(String, String)] = []
        if let ov = clean(settings.libraryDbOverride) {
            // Accept either the full path of seforim.db or its folder.
            candidates.append(env.directoryExists(ov) ? (join(ov, "seforim.db"), "הגדרות הווידג'ט (תיקייה)") : (ov, "הגדרות הווידג'ט"))
        }
        append(&candidates, clean(caps?.libraryDbPath), "otzaria_capabilities.json (libraryDbPath)")
        if let d = dbDirTxt { candidates.append((join(d, "seforim.db"), "library_database_path.txt")) }
        if let lib = libraryPath {
            // Otzaria tries the saved folder name first, then none (_libraryDbFolderName).
            if let folder { candidates.append((join(join(lib, folder), "seforim.db"), "key-library-path")) }
            candidates.append((join(lib, "seforim.db"), "key-library-path"))
        }
        if let b = bundled { candidates.append((join(b, "seforim.db"), "ספרייה מצורפת")) }
        if let t = libraryPathTxt { candidates.append((join(t, "seforim.db"), "library_path.txt")) }
        if let root = dataRoot { candidates.append((join(join(root, "books"), "seforim.db"), "ברירת מחדל (<תיקיית נתונים>/books)")) }
        candidates.append((join(join(Self.systemWideRoot, "books"), "seforim.db"), "התקנה מערכתית"))

        for (path, source) in candidates where env.fileExists(path) {
            notes.append("seforim.db: \(path) (מקור: \(source))")
            return path
        }
        notes.append("לא נמצא קובץ seforim.db")
        return nil
    }

    /// Candidate Otzaria executables or bundles with a label for the diagnostics: widget setting, capabilities, standard places.
    private func appCandidates(_ settings: WidgetSettings, _ caps: Capabilities?, _ home: String?) -> [(String, String)] {
        var list: [(String, String)] = []
        append(&list, clean(settings.otzariaAppOverride), "הגדרות הווידג'ט")
        append(&list, clean(caps?.exePath), "otzaria_capabilities.json")
        var roots = ["/Applications"]
        if let home { roots.append(join(home, "Applications")) }
        for root in roots {
            for name in ["אוצריא.app", "Otzaria.app", "otzaria.app"] {
                list.append((join(root, name), "תיקיית יישומים סטנדרטית"))
            }
        }
        return list
    }

    private func resolveApp(_ settings: WidgetSettings, _ caps: Capabilities?, _ portableExe: String?, _ home: String?,
                            _ notes: inout [String]) -> String? {
        if let ov = clean(settings.otzariaAppOverride) {
            if env.directoryExists(ov) || env.fileExists(ov) {
                notes.append("Otzaria: \(ov) (נקבע ידנית בהגדרות)")
                return bundlePath(of: ov)
            }
            notes.append("הנתיב שהוגדר ידנית עבור Otzaria אינו קיים: \(ov)")
        }
        // Paths from data files must at least look like Otzaria, so a tampered file cannot start an arbitrary program.
        if let cp = caps?.exePath, looksLikeOtzaria(cp), env.fileExists(cp) || env.directoryExists(cp) {
            notes.append("Otzaria: \(cp) (otzaria_capabilities.json)")
            return bundlePath(of: cp)
        }
        if let p = portableExe, env.fileExists(p) || env.directoryExists(p) {
            notes.append("Otzaria: \(p) (גרסה ניידת)")
            return bundlePath(of: p)
        }
        for (candidate, source) in appCandidates(WidgetSettings(), nil, home) where env.directoryExists(candidate) {
            notes.append("Otzaria: \(candidate) (מקור: \(source))")
            return candidate
        }
        notes.append("לא נמצאה אפליקציית אוצריא")
        return nil
    }

    // MARK: Bundled library

    /// FULL package layout: `<folder>/אוצריא.app/Contents/MacOS/<exe>` next to `<folder>/אוצריא/` with a marker and seforim.db.
    private func detectBundledLibrary(_ candidates: [String]) -> String? {
        for candidate in candidates {
            guard let bundle = bundleRoot(of: candidate), let parent = dirName(bundle) else { continue }
            let dir = join(parent, Self.bundledLibraryFolderName)
            if env.fileExists(join(dir, Self.bundledLibraryMarker)) && env.fileExists(join(dir, "seforim.db")) { return dir }
        }
        return nil
    }

    /// A path that holds seforim.db and is not the bundled library (no marker) counts as a manual choice.
    private func isUserChosenLibrary(_ path: String, folderName: String?) -> Bool {
        let dbDir = folderName.map { join(path, $0) } ?? path
        return env.fileExists(join(dbDir, "seforim.db")) && !env.fileExists(join(dbDir, Self.bundledLibraryMarker))
    }

    // MARK: App path helpers

    /// Directory of the executable: `<app>/Contents/MacOS` for a bundle, else the parent of the given file.
    private func executableDirectory(of path: String) -> String? {
        if path.lowercased().hasSuffix(".app") { return join(path, "Contents/MacOS") }
        return dirName(path)
    }

    /// The `.app` bundle containing `path` (or `path` itself when it is one), nil when there is none.
    private func bundleRoot(of path: String) -> String? {
        var current = path
        while true {
            if current.lowercased().hasSuffix(".app") { return current }
            guard let parent = dirName(current), parent != current, parent != "/" else { return nil }
            current = parent
        }
    }

    private func bundlePath(of path: String) -> String { bundleRoot(of: path) ?? path }

    private func looksLikeOtzaria(_ path: String) -> Bool {
        func named(_ c: String) -> Bool { c.hasPrefix("otzaria") || c.hasPrefix("אוצריא") }
        let components = path.split(separator: "/").map { $0.lowercased() }
        guard let last = components.last else { return false }
        return named(last) || components.contains { $0.hasSuffix(".app") && named($0) }
    }

    // MARK: Files

    private func looksLikeDataRoot(_ dir: String) -> Bool {
        env.directoryExists(dir) && (env.fileExists(join(dir, "app_preferences.hive")) || env.directoryExists(join(dir, "databases")))
    }

    private func readPathFile(_ file: String) -> String? {
        guard let text = env.readText(file) else { return nil }
        var s = text
        while s.hasPrefix("\u{FEFF}") { s.removeFirst() }
        s = s.trimmingCharacters(in: .whitespacesAndNewlines).trimmingCharacters(in: CharacterSet(charactersIn: "\""))
        return s.isEmpty ? nil : s
    }

    private func readCapabilities(_ file: String) -> Capabilities? {
        guard var text = env.readText(file), !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return nil }
        while text.hasPrefix("\u{FEFF}") { text.removeFirst() }
        guard let obj = try? JSONSerialization.jsonObject(with: Data(text.utf8)) as? [String: Any] else { return nil }
        return Capabilities(exePath: clean(obj["exePath"] as? String), portable: (obj["portable"] as? Bool) == true,
                            libraryDbPath: clean(obj["libraryDbPath"] as? String))
    }

    // MARK: String helpers

    private func append(_ list: inout [(String, String)], _ value: String?, _ source: String) {
        if let value { list.append((value, source)) }
    }

    private func clean(_ s: String?) -> String? {
        guard let s, !s.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return nil }
        return s.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private func join(_ a: String, _ b: String) -> String { (a as NSString).appendingPathComponent(b) }

    private func dirName(_ path: String) -> String? {
        var p = path
        while p.count > 1 && p.hasSuffix("/") { p.removeLast() }
        let parent = (p as NSString).deletingLastPathComponent
        return parent.isEmpty ? nil : parent
    }

    private func samePath(_ a: String?, _ b: String?) -> Bool {
        guard let a, let b else { return false }
        func norm(_ s: String) -> String { var t = s; while t.count > 1 && t.hasSuffix("/") { t.removeLast() }; return t.lowercased() }
        return norm(a) == norm(b)
    }
}
