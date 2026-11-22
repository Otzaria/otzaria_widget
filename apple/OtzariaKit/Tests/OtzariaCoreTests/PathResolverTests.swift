import XCTest
@testable import OtzariaCore

final class PathResolverTests: XCTestCase {
    private let home = "/Users/test"
    private var root: String { home + "/Library/Application Support/otzaria" }

    private func base() -> FakeEnv {
        FakeEnv(homeDirectory: home).addingFile(root + "/app_preferences.hive")
    }

    private func resolve(_ env: FakeEnv, _ settings: WidgetSettings = WidgetSettings(),
                         prefs: OtzariaPreferences? = nil) -> OtzariaPaths {
        OtzariaPathResolver(environment: env, preferencesReader: { _ in prefs }).resolve(settings: settings)
    }

    func testDefaultMacOSLayout() {
        let env = base()
            .addingFile(root + "/databases/user_state.db")
            .addingFile(root + "/databases/personal_notes.db")
            .addingFile(root + "/databases/user_books.db")
            .addingFile(root + "/books/seforim.db")
        let p = resolve(env, prefs: OtzariaPreferences())
        XCTAssertEqual(p.dataRoot, root)
        XCTAssertEqual(p.userStateDb, root + "/databases/user_state.db")
        XCTAssertEqual(p.personalNotesDb, root + "/databases/personal_notes.db")
        XCTAssertEqual(p.userBooksDb, root + "/databases/user_books.db")
        XCTAssertEqual(p.preferencesHive, root + "/app_preferences.hive")
        XCTAssertEqual(p.libraryDb, root + "/books/seforim.db")
        XCTAssertFalse(p.isPortable)
        XCTAssertNil(p.otzariaApp)
    }

    func testNothingInstalled() {
        let p = resolve(FakeEnv(homeDirectory: home))
        XCTAssertNil(p.dataRoot)
        XCTAssertNil(p.userStateDb)
        XCTAssertNil(p.libraryDb)
        XCTAssertFalse(p.diagnostics.isEmpty)
    }

    func testOverrideBeatsDefaultAndMissingOverrideFallsBack() {
        let env = base().addingFile("/custom/app_preferences.hive")
        var s = WidgetSettings()
        s.dataRootOverride = "/custom"
        XCTAssertEqual(resolve(env, s).dataRoot, "/custom")
        s.dataRootOverride = "/missing"
        XCTAssertEqual(resolve(env, s).dataRoot, root)
    }

    func testEmptyDefaultRootStillUsedWhenItExists() {
        let env = FakeEnv(homeDirectory: home).addingDir(root)
        XCTAssertEqual(resolve(env).dataRoot, root)
    }

    func testPortableLayoutFromBundleOverride() {
        let exeDir = "/Volumes/X/Otzaria.app/Contents/MacOS"
        let env = FakeEnv(homeDirectory: home)
            .addingFile(exeDir + "/portable.marker")
            .addingFile(exeDir + "/otzaria_data/app_preferences.hive")
            .addingFile(exeDir + "/otzaria_data/databases/user_state.db")
        var s = WidgetSettings()
        s.otzariaAppOverride = "/Volumes/X/Otzaria.app"
        let p = resolve(env.addingDir("/Volumes/X/Otzaria.app"), s)
        XCTAssertEqual(p.dataRoot, exeDir + "/otzaria_data")
        XCTAssertTrue(p.isPortable)
        XCTAssertEqual(p.userStateDb, exeDir + "/otzaria_data/databases/user_state.db")
        XCTAssertEqual(p.otzariaApp, "/Volumes/X/Otzaria.app")
    }

    func testPortableNeedsMarkerFile() {
        let exeDir = "/Volumes/X/Otzaria.app/Contents/MacOS"
        let env = FakeEnv(homeDirectory: home).addingFile(exeDir + "/otzaria_data/app_preferences.hive")
        var s = WidgetSettings()
        s.otzariaAppOverride = "/Volumes/X/Otzaria.app"
        XCTAssertNil(resolve(env, s).dataRoot)
    }

    func testDatabasesDirPerFileFallbackOrder() {
        let env = base()
            .addingFile("/pref-dbs/user_state.db")
            .addingFile(root + "/databases/personal_notes.db")
        var prefs = OtzariaPreferences()
        prefs.databasesPath = "/pref-dbs"
        let p = resolve(env, prefs: prefs)
        XCTAssertEqual(p.userStateDb, "/pref-dbs/user_state.db")
        XCTAssertEqual(p.personalNotesDb, root + "/databases/personal_notes.db")
    }

    func testDatabasesNextToLibraryWhenNotInDataRoot() {
        let env = base().addingFile("/Volumes/Lib/databases/user_state.db")
        var prefs = OtzariaPreferences()
        prefs.libraryPath = "/Volumes/Lib/books"
        XCTAssertEqual(resolve(env, prefs: prefs).userStateDb, "/Volumes/Lib/databases/user_state.db")
    }

    func testLibraryDbCandidateOrder() {
        var prefs = OtzariaPreferences()
        prefs.libraryPath = "/lib"
        // prefs path with folder name
        var p = resolve(base().addingFile("/lib/sub/seforim.db").addingFile("/lib/seforim.db"), prefs: { var x = prefs; x.libraryFolderName = "sub"; return x }())
        XCTAssertEqual(p.libraryDb, "/lib/sub/seforim.db")
        // stale folder name falls back to the library path itself
        p = resolve(base().addingFile("/lib/seforim.db"), prefs: { var x = prefs; x.libraryFolderName = "gone"; return x }())
        XCTAssertEqual(p.libraryDb, "/lib/seforim.db")
        // capabilities libraryDbPath wins over prefs
        let env = base()
            .addingFile(root + "/otzaria_capabilities.json", text: "{\"libraryDbPath\":\"/caps/seforim.db\"}")
            .addingFile("/caps/seforim.db")
            .addingFile("/lib/seforim.db")
        XCTAssertEqual(resolve(env, prefs: prefs).libraryDb, "/caps/seforim.db")
        // widget override (folder) wins over everything
        var s = WidgetSettings()
        s.libraryDbOverride = "/mine"
        XCTAssertEqual(resolve(env.addingFile("/mine/seforim.db"), s, prefs: prefs).libraryDb, "/mine/seforim.db")
    }

    func testLibraryPathTxtWithBomWhenNoPrefs() {
        let env = base()
            .addingFile(root + "/library_path.txt", text: "\u{FEFF}\"/txt/lib\"\n")
            .addingFile("/txt/lib/seforim.db")
        XCTAssertEqual(resolve(env, prefs: nil).libraryDb, "/txt/lib/seforim.db")
        let env2 = base()
            .addingFile(root + "/library_database_path.txt", text: "\u{FEFF}/dbdir")
            .addingFile("/dbdir/seforim.db")
        XCTAssertEqual(resolve(env2, prefs: nil).libraryDb, "/dbdir/seforim.db")
    }

    func testBundledLibraryBeatsStalePrefsButNotUserChoice() {
        let bundle = "/Volumes/Pkg/אוצריא.app/Contents/MacOS/otzaria"
        let lib = "/Volumes/Pkg/אוצריא"
        let env = base()
            .addingFile(lib + "/.otzaria_bundled_library")
            .addingFile(lib + "/seforim.db")
            .addingFile(bundle)
        var s = WidgetSettings()
        s.otzariaAppOverride = "/Volumes/Pkg/אוצריא.app"
        var prefs = OtzariaPreferences()
        prefs.libraryPath = "/old/gone"
        XCTAssertEqual(resolve(env, s, prefs: prefs).libraryDb, lib + "/seforim.db")

        prefs.libraryPath = "/mine"
        let chosen = env.addingFile("/mine/seforim.db")
        XCTAssertEqual(resolve(chosen, s, prefs: prefs).libraryDb, "/mine/seforim.db")

        // a prefs path that is the bundle itself (marker present) is not a manual choice
        prefs.libraryPath = lib
        XCTAssertEqual(resolve(env, s, prefs: prefs).libraryDb, lib + "/seforim.db")
    }

    func testSystemWideLibraryCandidate() {
        let env = base().addingFile("/Library/Application Support/otzaria/books/seforim.db")
        XCTAssertEqual(resolve(env, prefs: OtzariaPreferences()).libraryDb, "/Library/Application Support/otzaria/books/seforim.db")
    }

    func testAppDetectionStandardLocationsAndCapabilitiesGuard() {
        var env = base().addingDir("/Applications/אוצריא.app")
        XCTAssertEqual(resolve(env).otzariaApp, "/Applications/אוצריא.app")

        // exePath from capabilities must look like Otzaria
        env = base().addingFile(root + "/otzaria_capabilities.json", text: "{\"exePath\":\"/usr/bin/evil\"}").addingFile("/usr/bin/evil")
        XCTAssertNil(resolve(env).otzariaApp)
        env = base()
            .addingFile(root + "/otzaria_capabilities.json", text: "{\"exePath\":\"/Apps/Otzaria.app/Contents/MacOS/otzaria\"}")
            .addingFile("/Apps/Otzaria.app/Contents/MacOS/otzaria")
        XCTAssertEqual(resolve(env).otzariaApp, "/Apps/Otzaria.app")
        // Only an Otzaria bundle or its Contents/MacOS executable, not any file whose name starts with "otzaria".
        for bad in ["/tmp/otzaria-evil.sh", "/Apps/Evil.app/Contents/MacOS/otzaria", "/Apps/Otzaria.app/Contents/Resources/otzaria-x"] {
            env = base().addingFile(root + "/otzaria_capabilities.json", text: "{\"exePath\":\"\(bad)\"}").addingFile(bad)
            XCTAssertNil(resolve(env).otzariaApp, bad)
        }
        env = base().addingFile(root + "/otzaria_capabilities.json", text: "{\"exePath\":\"/Apps/אוצריא.app/\"}").addingDir("/Apps/אוצריא.app/")
        XCTAssertNotNil(resolve(env).otzariaApp)
    }

    func testCorruptCapabilitiesIgnored() {
        let env = base().addingFile(root + "/otzaria_capabilities.json", text: "{nope")
        XCTAssertEqual(resolve(env).dataRoot, root)
    }

    func testSystemEnvironmentBasics() {
        let env = SystemResolverEnvironment()
        let dir = TempDir()
        let file = dir.write("a.txt", "hi")
        XCTAssertTrue(env.fileExists(file))
        XCTAssertFalse(env.directoryExists(file))
        XCTAssertTrue(env.directoryExists(dir.path))
        XCTAssertEqual(env.readText(file), "hi")
        XCTAssertNil(env.readText(dir.file("missing")))
    }
}
