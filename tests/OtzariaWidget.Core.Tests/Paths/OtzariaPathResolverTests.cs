using OtzariaWidget.Core.Contracts;
using OtzariaWidget.Core.Paths;

namespace OtzariaWidget.Core.Tests.Paths;

public class OtzariaPathResolverTests
{
    private const string AppData = @"C:\Users\u\AppData\Roaming";
    private const string Root = @"C:\Users\u\AppData\Roaming\otzaria";
    private const string Bom = "\uFEFF";

    private static FakeEnv BaseEnv() => new FakeEnv()
        .SetEnv("APPDATA", AppData)
        .SetEnv("LOCALAPPDATA", @"C:\Users\u\AppData\Local")
        .SetEnv("ProgramFiles", @"C:\Program Files")
        .SetEnv("ProgramW6432", @"C:\Program Files")
        .SetEnv("ProgramData", @"C:\ProgramData");

    private static OtzariaPathResolver Resolver(FakeEnv env, Func<string, OtzariaPreferences?>? prefs = null)
        => new(env, prefs ?? (_ => throw new NotImplementedException()));

    [Fact]
    public void Default_appdata_layout_is_resolved()
    {
        var env = BaseEnv()
            .AddFile(Root + @"\app_preferences.hive")
            .AddFile(Root + @"\databases\user_state.db")
            .AddFile(Root + @"\databases\personal_notes.db")
            .AddFile(Root + @"\library_database_path.txt", Bom + @"D:\lib")
            .AddFile(@"D:\lib\seforim.db");

        var p = Resolver(env).Resolve(new WidgetAppSettings());

        Assert.Equal(Root, p.DataRoot);
        Assert.Equal(Root + @"\databases\user_state.db", p.UserStateDb);
        Assert.Equal(Root + @"\databases\personal_notes.db", p.PersonalNotesDb);
        Assert.Equal(Root + @"\app_preferences.hive", p.PreferencesHive);
        Assert.Equal(@"D:\lib\seforim.db", p.LibraryDb);
        Assert.False(p.IsPortable);
        Assert.NotEmpty(p.Diagnostics);
    }

    [Fact]
    public void Nothing_found_returns_nulls_without_throwing()
    {
        var p = Resolver(BaseEnv()).Resolve(new WidgetAppSettings());
        Assert.Null(p.DataRoot);
        Assert.Null(p.UserStateDb);
        Assert.Null(p.LibraryDb);
        Assert.Null(p.OtzariaExe);
        Assert.NotEmpty(p.Diagnostics);
    }

    [Fact]
    public void Missing_env_variables_do_not_throw()
    {
        var p = Resolver(new FakeEnv()).Resolve(new WidgetAppSettings());
        Assert.Null(p.DataRoot);
    }

    [Fact]
    public void Data_root_override_wins()
    {
        var env = BaseEnv()
            .AddFile(Root + @"\app_preferences.hive")
            .AddFile(Root + @"\databases\user_state.db")
            .AddFile(@"E:\custom\databases\user_state.db")
            .AddFile(@"E:\custom\app_preferences.hive");

        var p = Resolver(env).Resolve(new WidgetAppSettings { DataRootOverride = @"E:\custom" });
        Assert.Equal(@"E:\custom", p.DataRoot);
        Assert.Equal(@"E:\custom\databases\user_state.db", p.UserStateDb);
        Assert.Equal(@"E:\custom\app_preferences.hive", p.PreferencesHive);
    }

    [Fact]
    public void Missing_data_root_override_falls_back_to_appdata()
    {
        var env = BaseEnv().AddFile(Root + @"\app_preferences.hive");
        var p = Resolver(env).Resolve(new WidgetAppSettings { DataRootOverride = @"E:\nope" });
        Assert.Equal(Root, p.DataRoot);
    }

    [Fact]
    public void Databases_path_from_prefs_has_priority_per_file()
    {
        var env = BaseEnv()
            .AddFile(Root + @"\app_preferences.hive")
            .AddFile(@"F:\dbs\user_state.db")                     // only user_state in the custom dir
            .AddFile(Root + @"\databases\user_state.db")
            .AddFile(Root + @"\databases\personal_notes.db");     // personal notes only in the legacy dir

        var p = Resolver(env, _ => new OtzariaPreferences { DatabasesPath = @"F:\dbs" }).Resolve(new WidgetAppSettings());
        Assert.Equal(@"F:\dbs\user_state.db", p.UserStateDb);
        Assert.Equal(Root + @"\databases\personal_notes.db", p.PersonalNotesDb);
    }

    [Fact]
    public void Databases_dir_falls_back_to_parent_of_library_path()
    {
        var env = BaseEnv()
            .AddFile(Root + @"\app_preferences.hive")
            .AddFile(@"D:\otz\databases\user_state.db");
        var prefs = new OtzariaPreferences { LibraryPath = @"D:\otz\books" };

        var p = Resolver(env, _ => prefs).Resolve(new WidgetAppSettings());
        Assert.Equal(@"D:\otz\databases\user_state.db", p.UserStateDb);
    }

    [Fact]
    public void Databases_dir_override_from_widget_settings()
    {
        var env = BaseEnv()
            .AddFile(Root + @"\app_preferences.hive")
            .AddFile(Root + @"\databases\user_state.db")
            .AddFile(@"G:\x\user_state.db");
        var p = Resolver(env).Resolve(new WidgetAppSettings { DatabasesDirOverride = @"G:\x" });
        Assert.Equal(@"G:\x\user_state.db", p.UserStateDb);
    }

    [Fact]
    public void Prefs_reader_failure_falls_back_to_text_files()
    {
        var env = BaseEnv()
            .AddFile(Root + @"\app_preferences.hive")
            .AddFile(Root + @"\library_path.txt", Bom + @"D:\books")
            .AddFile(@"D:\books\seforim.db")
            .AddFile(@"D:\databases\user_state.db");

        var p = Resolver(env, _ => throw new InvalidOperationException("boom")).Resolve(new WidgetAppSettings());
        Assert.Equal(@"D:\books\seforim.db", p.LibraryDb);
        Assert.Equal(@"D:\databases\user_state.db", p.UserStateDb); // dirname(libraryPath)\databases
    }

    [Fact]
    public void Library_db_resolution_order_capabilities_first()
    {
        var caps = "{\"libraryDbPath\":\"D:\\\\fromcaps\\\\seforim.db\",\"exePath\":\"X:\\\\no.exe\"}";
        var env = BaseEnv()
            .AddFile(Root + @"\app_preferences.hive")
            .AddFile(Root + @"\otzaria_capabilities.json", caps)
            .AddFile(Root + @"\library_database_path.txt", Bom + @"D:\fromtxt")
            .AddFile(@"D:\fromcaps\seforim.db")
            .AddFile(@"D:\fromtxt\seforim.db")
            .AddFile(@"D:\fromprefs\sub\seforim.db");

        var prefs = new OtzariaPreferences { LibraryPath = @"D:\fromprefs", LibraryFolderName = "sub" };
        Assert.Equal(@"D:\fromcaps\seforim.db", Resolver(env, _ => prefs).Resolve(new WidgetAppSettings()).LibraryDb);
    }

    [Fact]
    public void Library_db_skips_candidates_that_do_not_exist()
    {
        var caps = "{\"libraryDbPath\":\"D:\\\\gone\\\\seforim.db\"}";
        var env = BaseEnv()
            .AddFile(Root + @"\app_preferences.hive")
            .AddFile(Root + @"\otzaria_capabilities.json", caps)
            .AddFile(Root + @"\library_database_path.txt", Bom + @"D:\gone")
            .AddFile(@"D:\fromprefs\sub\seforim.db");
        var prefs = new OtzariaPreferences { LibraryPath = @"D:\fromprefs", LibraryFolderName = "sub" };
        Assert.Equal(@"D:\fromprefs\sub\seforim.db", Resolver(env, _ => prefs).Resolve(new WidgetAppSettings()).LibraryDb);
    }

    [Fact]
    public void Library_db_override_accepts_file_or_folder()
    {
        var env = BaseEnv().AddFile(Root + @"\app_preferences.hive").AddFile(@"H:\lib\seforim.db");
        Assert.Equal(@"H:\lib\seforim.db", Resolver(env).Resolve(new WidgetAppSettings { LibraryDbOverride = @"H:\lib\seforim.db" }).LibraryDb);
        Assert.Equal(@"H:\lib\seforim.db", Resolver(env).Resolve(new WidgetAppSettings { LibraryDbOverride = @"H:\lib" }).LibraryDb);
    }

    [Fact]
    public void Default_books_folder_is_probed()
    {
        var env = BaseEnv().AddFile(Root + @"\app_preferences.hive").AddFile(Root + @"\books\seforim.db");
        Assert.Equal(Root + @"\books\seforim.db", Resolver(env).Resolve(new WidgetAppSettings()).LibraryDb);
    }

    // ---- portable -------------------------------------------------------------------------------
    [Fact]
    public void Portable_discovered_via_exe_override_and_marker()
    {
        var env = BaseEnv() // no %APPDATA%\otzaria at all
            .AddFile(@"P:\Otz\otzaria.exe")
            .AddFile(@"P:\Otz\portable.marker")
            .AddFile(@"P:\Otz\otzaria_data\app_preferences.hive")
            .AddFile(@"P:\Otz\otzaria_data\databases\user_state.db");

        var p = Resolver(env).Resolve(new WidgetAppSettings { OtzariaExeOverride = @"P:\Otz\otzaria.exe" });
        Assert.True(p.IsPortable);
        Assert.Equal(@"P:\Otz\otzaria_data", p.DataRoot);
        Assert.Equal(@"P:\Otz\otzaria_data\databases\user_state.db", p.UserStateDb);
        Assert.Equal(@"P:\Otz\otzaria.exe", p.OtzariaExe);
    }

    [Fact]
    public void Portable_discovered_via_registry_command()
    {
        var env = BaseEnv()
            .AddFile(@"P:\Otz\otzaria.exe")
            .AddFile(@"P:\Otz\portable.marker")
            .AddFile(@"P:\Otz\otzaria_data\databases\user_state.db")
            .SetReg(RegistryRoot.CurrentUser, OtzariaPathResolver.ProtocolCommandKey, null, "\"P:\\Otz\\otzaria.exe\" \"%1\"");

        var p = Resolver(env).Resolve(new WidgetAppSettings());
        Assert.True(p.IsPortable);
        Assert.Equal(@"P:\Otz\otzaria_data", p.DataRoot);
        Assert.Equal(@"P:\Otz\otzaria.exe", p.OtzariaExe);
    }

    [Fact]
    public void Exe_dir_without_portable_marker_is_not_portable()
    {
        var env = BaseEnv()
            .AddFile(@"P:\Otz\otzaria.exe")
            .AddFile(@"P:\Otz\otzaria_data\databases\user_state.db");
        var p = Resolver(env).Resolve(new WidgetAppSettings { OtzariaExeOverride = @"P:\Otz\otzaria.exe" });
        Assert.False(p.IsPortable);
        Assert.Null(p.DataRoot);
    }

    [Fact]
    public void Appdata_beats_portable_candidates()
    {
        var env = BaseEnv()
            .AddFile(Root + @"\app_preferences.hive")
            .AddFile(@"P:\Otz\otzaria.exe")
            .AddFile(@"P:\Otz\portable.marker")
            .AddFile(@"P:\Otz\otzaria_data\app_preferences.hive");
        var p = Resolver(env).Resolve(new WidgetAppSettings { OtzariaExeOverride = @"P:\Otz\otzaria.exe" });
        Assert.Equal(Root, p.DataRoot);
        Assert.False(p.IsPortable);
    }

    // ---- exe -------------------------------------------------------------------------------------
    [Fact]
    public void Exe_from_capabilities()
    {
        var env = BaseEnv()
            .AddFile(Root + @"\app_preferences.hive")
            .AddFile(Root + @"\otzaria_capabilities.json", "{\"exePath\":\"C:\\\\Apps\\\\otzaria.exe\"}")
            .AddFile(@"C:\Apps\otzaria.exe");
        Assert.Equal(@"C:\Apps\otzaria.exe", Resolver(env).Resolve(new WidgetAppSettings()).OtzariaExe);
    }

    [Fact]
    public void Exe_override_beats_everything_and_missing_override_is_ignored()
    {
        var env = BaseEnv()
            .AddFile(Root + @"\app_preferences.hive")
            .AddFile(Root + @"\otzaria_capabilities.json", "{\"exePath\":\"C:\\\\Apps\\\\otzaria.exe\"}")
            .AddFile(@"C:\Apps\otzaria.exe")
            .AddFile(@"C:\Mine\otzaria.exe");
        Assert.Equal(@"C:\Mine\otzaria.exe", Resolver(env).Resolve(new WidgetAppSettings { OtzariaExeOverride = @"C:\Mine\otzaria.exe" }).OtzariaExe);
        Assert.Equal(@"C:\Apps\otzaria.exe", Resolver(env).Resolve(new WidgetAppSettings { OtzariaExeOverride = @"C:\Missing\otzaria.exe" }).OtzariaExe);
    }

    [Fact]
    public void Exe_from_registry_when_capabilities_exe_is_stale()
    {
        var env = BaseEnv()
            .AddFile(Root + @"\app_preferences.hive")
            .AddFile(Root + @"\otzaria_capabilities.json", "{\"exePath\":\"C:\\\\Stale\\\\otzaria.exe\"}")
            .AddFile(@"C:\Reg\otzaria.exe")
            .SetReg(RegistryRoot.LocalMachine, OtzariaPathResolver.ProtocolCommandKey, null, "\"C:\\Reg\\otzaria.exe\" \"%1\"");
        Assert.Equal(@"C:\Reg\otzaria.exe", Resolver(env).Resolve(new WidgetAppSettings()).OtzariaExe);
    }

    [Fact]
    public void Exe_from_inno_uninstall_key()
    {
        var env = BaseEnv()
            .AddFile(Root + @"\app_preferences.hive")
            .AddFile(@"C:\Inno\Otzaria\otzaria.exe")
            .SetReg(RegistryRoot.CurrentUser, OtzariaPathResolver.InnoUninstallKey, "InstallLocation", @"C:\Inno\Otzaria\");
        Assert.Equal(@"C:\Inno\Otzaria\otzaria.exe", Resolver(env).Resolve(new WidgetAppSettings()).OtzariaExe);
    }

    [Fact]
    public void Exe_from_default_install_dirs()
    {
        var env = BaseEnv()
            .AddFile(Root + @"\app_preferences.hive")
            .AddFile(@"C:\Users\u\AppData\Local\Programs\Otzaria\otzaria.exe");
        Assert.Equal(@"C:\Users\u\AppData\Local\Programs\Otzaria\otzaria.exe", Resolver(env).Resolve(new WidgetAppSettings()).OtzariaExe);

        var env2 = BaseEnv().AddFile(Root + @"\app_preferences.hive").AddFile(@"C:\Program Files\Otzaria\otzaria.exe");
        Assert.Equal(@"C:\Program Files\Otzaria\otzaria.exe", Resolver(env2).Resolve(new WidgetAppSettings()).OtzariaExe);
    }

    [Theory]
    [InlineData("\"C:\\a b\\otzaria.exe\" \"%1\"", @"C:\a b\otzaria.exe")]
    [InlineData(@"C:\x\otzaria.exe ""%1""", @"C:\x\otzaria.exe")]
    [InlineData(@"C:\x\otzaria.exe,0", @"C:\x\otzaria.exe")]
    [InlineData("\"C:\\x\\otzaria.exe\",0", @"C:\x\otzaria.exe")]
    [InlineData("", null)]
    [InlineData("notepad", null)]
    [InlineData("\"unterminated", null)]
    public void ParseCommandExe_cases(string cmd, string? expected)
        => Assert.Equal(expected, OtzariaPathResolver.ParseCommandExe(cmd));

    [Fact]
    public void Capabilities_corrupt_json_is_ignored()
    {
        var env = BaseEnv()
            .AddFile(Root + @"\app_preferences.hive")
            .AddFile(Root + @"\otzaria_capabilities.json", "{ not json");
        var p = Resolver(env).Resolve(new WidgetAppSettings());
        Assert.Equal(Root, p.DataRoot);
    }

    [Fact]
    public void Capabilities_portable_flag_sets_IsPortable()
    {
        var env = BaseEnv()
            .AddFile(Root + @"\app_preferences.hive")
            .AddFile(Root + @"\otzaria_capabilities.json", "{\"portable\":true}");
        Assert.True(Resolver(env).Resolve(new WidgetAppSettings()).IsPortable);
    }

    [Fact]
    public void Bom_and_quotes_in_txt_files_are_stripped()
    {
        var env = BaseEnv()
            .AddFile(Root + @"\app_preferences.hive")
            .AddFile(Root + @"\library_database_path.txt", Bom + "\"D:\\q\"\r\n")
            .AddFile(@"D:\q\seforim.db");
        Assert.Equal(@"D:\q\seforim.db", Resolver(env).Resolve(new WidgetAppSettings()).LibraryDb);
    }

    [Fact]
    public void Hive_missing_gives_null_hive_path()
    {
        var env = BaseEnv().AddFile(Root + @"\databases\user_state.db");
        var p = Resolver(env).Resolve(new WidgetAppSettings());
        Assert.Equal(Root, p.DataRoot);
        Assert.Null(p.PreferencesHive);
        Assert.NotNull(p.UserStateDb);
    }
}
