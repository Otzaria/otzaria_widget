using OtzariaWidget.Core.Contracts;
using OtzariaWidget.Core.Paths;

namespace OtzariaWidget.Core.Tests.Paths;

/// <summary>QA regression tests for path / exe resolution (storage-and-links.md section 1 and 5.5).</summary>
public class OtzariaPathResolverQaTests
{
    private const string AppData = @"C:\Users\u\AppData\Roaming";
    private const string Root = @"C:\Users\u\AppData\Roaming\otzaria";

    private static FakeEnv BaseEnv() => new FakeEnv()
        .SetEnv("APPDATA", AppData)
        .SetEnv("LOCALAPPDATA", @"C:\Users\u\AppData\Local")
        .SetEnv("ProgramFiles", @"C:\Program Files")
        .SetEnv("ProgramW6432", @"C:\Program Files")
        .SetEnv("ProgramData", @"C:\ProgramData");

    private static OtzariaPathResolver Resolver(FakeEnv env, Func<string, OtzariaPreferences?>? prefs = null)
        => new(env, prefs ?? (_ => null));

    // ---- portable ------------------------------------------------------------------------------------
    [Fact]
    public void Portable_marker_that_is_a_directory_does_not_make_a_portable_install()
    {
        // main.cpp requires portable.marker to be a FILE; a folder with that name must be ignored.
        var env = BaseEnv()
            .AddDir(@"P:\Otz\portable.marker")
            .AddDir(@"P:\Otz\otzaria_data")
            .AddFile(@"P:\Otz\otzaria.exe");
        var p = Resolver(env).Resolve(new WidgetAppSettings { OtzariaExeOverride = @"P:\Otz\otzaria.exe" });
        Assert.Null(p.DataRoot);
        Assert.False(p.IsPortable);
    }

    [Fact]
    public void Portable_root_needs_the_otzaria_data_folder_too()
    {
        var env = BaseEnv().AddFile(@"P:\Otz\portable.marker").AddFile(@"P:\Otz\otzaria.exe");
        var p = Resolver(env).Resolve(new WidgetAppSettings { OtzariaExeOverride = @"P:\Otz\otzaria.exe" });
        Assert.Null(p.DataRoot);
    }

    [Fact]
    public void Portable_exe_override_that_is_a_folder_is_accepted_for_exe_but_not_for_data_root_discovery()
    {
        var env = BaseEnv().AddFile(@"P:\Otz\portable.marker").AddFile(@"P:\Otz\otzaria.exe").AddDir(@"P:\Otz\otzaria_data");
        var p = Resolver(env).Resolve(new WidgetAppSettings { OtzariaExeOverride = @"P:\Otz" });
        Assert.Equal(@"P:\Otz\otzaria.exe", p.OtzariaExe);
    }

    [Fact]
    public void Portable_discovered_through_capabilities_exe_of_the_default_root_is_not_mixed_up_with_its_files()
    {
        // %APPDATA%\otzaria only holds a stale capabilities file (no hive, no databases): the portable root wins and the
        // stale file's libraryDbPath must not leak into it.
        var env = BaseEnv()
            .AddFile(Root + @"\otzaria_capabilities.json", "{\"exePath\":\"P:\\\\Otz\\\\otzaria.exe\",\"libraryDbPath\":\"S:\\\\stale\\\\seforim.db\"}")
            .AddFile(@"P:\Otz\otzaria.exe").AddFile(@"P:\Otz\portable.marker")
            .AddFile(@"P:\Otz\otzaria_data\app_preferences.hive")
            .AddFile(@"P:\Otz\otzaria_data\books\seforim.db")
            .AddFile(@"S:\stale\seforim.db");
        var p = Resolver(env).Resolve(new WidgetAppSettings());
        Assert.Equal(@"P:\Otz\otzaria_data", p.DataRoot);
        Assert.True(p.IsPortable);
        Assert.Equal(@"P:\Otz\otzaria_data\books\seforim.db", p.LibraryDb);
        Assert.Equal(@"P:\Otz\otzaria.exe", p.OtzariaExe);
    }

    // ---- databases dir ---------------------------------------------------------------------------------
    [Fact]
    public void Each_database_file_is_resolved_independently()
    {
        // preferred folder (key-databases-path) has user_state.db only; personal_notes.db was copied to <dataRoot>\databases
        var env = BaseEnv()
            .AddFile(Root + @"\app_preferences.hive")
            .AddFile(@"E:\custom\user_state.db")
            .AddFile(Root + @"\databases\personal_notes.db");
        var p = Resolver(env, _ => new OtzariaPreferences { DatabasesPath = @"E:\custom" }).Resolve(new WidgetAppSettings());
        Assert.Equal(@"E:\custom\user_state.db", p.UserStateDb);
        Assert.Equal(Root + @"\databases\personal_notes.db", p.PersonalNotesDb);
    }

    [Fact]
    public void Databases_path_that_does_not_exist_falls_through_to_the_data_root_databases_folder()
    {
        var env = BaseEnv().AddFile(Root + @"\app_preferences.hive").AddFile(Root + @"\databases\user_state.db");
        var p = Resolver(env, _ => new OtzariaPreferences { DatabasesPath = @"Z:\gone" }).Resolve(new WidgetAppSettings());
        Assert.Equal(Root + @"\databases\user_state.db", p.UserStateDb);
    }

    [Fact]
    public void Whitespace_only_settings_and_prefs_are_treated_as_absent()
    {
        var env = BaseEnv().AddFile(Root + @"\app_preferences.hive").AddFile(Root + @"\databases\user_state.db");
        var p = Resolver(env, _ => new OtzariaPreferences { DatabasesPath = "  ", LibraryPath = "\t" })
            .Resolve(new WidgetAppSettings { DataRootOverride = " ", DatabasesDirOverride = "", LibraryDbOverride = "  ", OtzariaExeOverride = " " });
        Assert.Equal(Root, p.DataRoot);
        Assert.Equal(Root + @"\databases\user_state.db", p.UserStateDb);
    }

    // ---- seforim.db ------------------------------------------------------------------------------------
    [Fact]
    public void Library_folder_name_is_part_of_the_hive_derived_path()
    {
        var env = BaseEnv().AddFile(Root + @"\app_preferences.hive").AddFile(@"D:\lib\books\seforim.db");
        var p = Resolver(env, _ => new OtzariaPreferences { LibraryPath = @"D:\lib", LibraryFolderName = "books" }).Resolve(new WidgetAppSettings());
        Assert.Equal(@"D:\lib\books\seforim.db", p.LibraryDb);
    }

    [Fact]
    public void Capabilities_library_path_beats_hive_but_a_stale_one_falls_back_to_the_hive()
    {
        var env = BaseEnv()
            .AddFile(Root + @"\app_preferences.hive")
            .AddFile(Root + @"\otzaria_capabilities.json", "{\"libraryDbPath\":\"D:\\\\gone\\\\seforim.db\"}")
            .AddFile(@"D:\lib\seforim.db");
        var p = Resolver(env, _ => new OtzariaPreferences { LibraryPath = @"D:\lib" }).Resolve(new WidgetAppSettings());
        Assert.Equal(@"D:\lib\seforim.db", p.LibraryDb);
    }

    [Fact]
    public void Program_data_default_library_is_probed_for_system_wide_installs()
    {
        var env = BaseEnv().AddFile(Root + @"\app_preferences.hive").AddFile(@"C:\ProgramData\otzaria\books\seforim.db");
        Assert.Equal(@"C:\ProgramData\otzaria\books\seforim.db", Resolver(env).Resolve(new WidgetAppSettings()).LibraryDb);
    }

    // ---- capabilities json ------------------------------------------------------------------------------
    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"x\"")]
    [InlineData("{\"exePath\":5,\"portable\":\"true\",\"libraryDbPath\":[]}")]
    [InlineData("{\"exePath\":null}")]
    [InlineData("\uFEFF{\"exePath\":\"C:\\\\nowhere\\\\otzaria.exe\"}")]
    public void Odd_capabilities_content_never_throws_and_is_ignored(string json)
    {
        var env = BaseEnv().AddFile(Root + @"\app_preferences.hive").AddFile(Root + @"\otzaria_capabilities.json", json);
        var p = Resolver(env).Resolve(new WidgetAppSettings());
        Assert.Equal(Root, p.DataRoot);
        Assert.Null(p.OtzariaExe);
        Assert.False(p.IsPortable);
    }

    // ---- exe discovery -----------------------------------------------------------------------------------
    [Fact]
    public void Exe_from_capabilities_must_be_named_otzaria_exe()
    {
        var env = BaseEnv()
            .AddFile(Root + @"\app_preferences.hive")
            .AddFile(Root + @"\otzaria_capabilities.json", "{\"exePath\":\"C:\\\\Windows\\\\System32\\\\cmd.exe\"}")
            .AddFile(@"C:\Windows\System32\cmd.exe");
        Assert.Null(Resolver(env).Resolve(new WidgetAppSettings()).OtzariaExe);
    }

    [Fact]
    public void Exe_from_registry_must_be_named_otzaria_exe()
    {
        var env = BaseEnv()
            .AddFile(Root + @"\app_preferences.hive")
            .AddFile(@"C:\Windows\notepad.exe")
            .SetReg(RegistryRoot.CurrentUser, OtzariaPathResolver.ProtocolCommandKey, null, "\"C:\\Windows\\notepad.exe\" \"%1\"");
        Assert.Null(Resolver(env).Resolve(new WidgetAppSettings()).OtzariaExe);
    }

    [Fact]
    public void User_chosen_exe_override_may_have_any_name()
    {
        var env = BaseEnv().AddFile(Root + @"\app_preferences.hive").AddFile(@"C:\Mine\my-otzaria-build.exe");
        Assert.Equal(@"C:\Mine\my-otzaria-build.exe", Resolver(env).Resolve(new WidgetAppSettings { OtzariaExeOverride = @"C:\Mine\my-otzaria-build.exe" }).OtzariaExe);
    }

    [Fact]
    public void Registry_command_with_environment_variables_is_expanded()
    {
        var env = BaseEnv()
            .SetEnv("MYAPPS", @"C:\Apps")
            .AddFile(Root + @"\app_preferences.hive")
            .AddFile(@"C:\Apps\Otzaria\otzaria.exe")
            .SetReg(RegistryRoot.CurrentUser, OtzariaPathResolver.ProtocolCommandKey, null, "\"%MYAPPS%\\Otzaria\\otzaria.exe\" \"%1\"");
        Assert.Equal(@"C:\Apps\Otzaria\otzaria.exe", Resolver(env).Resolve(new WidgetAppSettings()).OtzariaExe);
    }

    [Fact]
    public void Inno_install_location_with_quotes_and_variables_is_handled()
    {
        var env = BaseEnv()
            .AddFile(Root + @"\app_preferences.hive")
            .AddFile(@"C:\Program Files\Otzaria\otzaria.exe")
            .SetReg(RegistryRoot.LocalMachine, OtzariaPathResolver.InnoUninstallKey, "InstallLocation", "\"%ProgramFiles%\\Otzaria\\\"");
        Assert.Equal(@"C:\Program Files\Otzaria\otzaria.exe", Resolver(env).Resolve(new WidgetAppSettings()).OtzariaExe);
    }

    [Fact]
    public void Inno_display_icon_is_a_last_resort_for_the_exe()
    {
        var env = BaseEnv()
            .AddFile(Root + @"\app_preferences.hive")
            .AddFile(@"C:\Inno\otzaria.exe")
            .SetReg(RegistryRoot.CurrentUser, OtzariaPathResolver.InnoUninstallKey, "DisplayIcon", @"C:\Inno\otzaria.exe,0");
        Assert.Equal(@"C:\Inno\otzaria.exe", Resolver(env).Resolve(new WidgetAppSettings()).OtzariaExe);
    }

    [Fact]
    public void Hkcu_protocol_registration_wins_over_hklm()
    {
        var env = BaseEnv()
            .AddFile(Root + @"\app_preferences.hive")
            .AddFile(@"C:\U\otzaria.exe").AddFile(@"C:\M\otzaria.exe")
            .SetReg(RegistryRoot.LocalMachine, OtzariaPathResolver.ProtocolCommandKey, null, "\"C:\\M\\otzaria.exe\" \"%1\"")
            .SetReg(RegistryRoot.CurrentUser, OtzariaPathResolver.ProtocolCommandKey, null, "\"C:\\U\\otzaria.exe\" \"%1\"");
        Assert.Equal(@"C:\U\otzaria.exe", Resolver(env).Resolve(new WidgetAppSettings()).OtzariaExe);
    }

    [Theory]
    [InlineData("\"C:\\a b\\otzaria.exe\"", @"C:\a b\otzaria.exe")]
    [InlineData("  \"C:\\a\\otzaria.exe\"   \"%1\"  ", @"C:\a\otzaria.exe")]
    [InlineData("C:\\Program Files\\Otzaria\\otzaria.exe \"%1\"", @"C:\Program Files\Otzaria\otzaria.exe")]
    [InlineData("C:\\x\\OTZARIA.EXE %1", @"C:\x\OTZARIA.EXE")]
    [InlineData("\"\"", null)]
    [InlineData("\"C:\\x\\otzaria.bat\" \"%1\"", null)]
    [InlineData("rundll32.exe shell32.dll,Foo", "rundll32.exe")]
    [InlineData("   ", null)]
    public void ParseCommandExe_more_cases(string cmd, string? expected)
        => Assert.Equal(expected, OtzariaPathResolver.ParseCommandExe(cmd));

    // ---- robustness --------------------------------------------------------------------------------------
    [Fact]
    public void Throwing_environment_pieces_never_escape()
    {
        var p = new OtzariaPathResolver(new ThrowingEnv(), _ => throw new InvalidOperationException()).Resolve(new WidgetAppSettings());
        Assert.NotNull(p);
        Assert.NotEmpty(p.Diagnostics);
        Assert.Null(p.UserStateDb);
    }

    [Fact]
    public void Null_settings_object_is_tolerated()
    {
        var p = Resolver(BaseEnv()).Resolve(null!);
        Assert.NotNull(p);
    }

    [Fact]
    public void Invalid_path_characters_in_settings_do_not_throw()
    {
        var env = BaseEnv().AddFile(Root + @"\app_preferences.hive");
        var p = Resolver(env).Resolve(new WidgetAppSettings { DataRootOverride = "C:\\bad|path<>\0", OtzariaExeOverride = "\"\"", LibraryDbOverride = "?*", DatabasesDirOverride = "::" });
        Assert.Equal(Root, p.DataRoot);
    }

    [Fact]
    public void Resolver_never_writes_anything_on_the_real_file_system()
    {
        // run against the real environment with a hive-less temp root: nothing may be created
        var dir = Path.Combine(Path.GetTempPath(), "otz-resolver-" + Guid.NewGuid().ToString("N"));
        var before = Directory.Exists(dir);
        var p = new OtzariaPathResolver().Resolve(new WidgetAppSettings { DataRootOverride = dir });
        Assert.False(before);
        Assert.False(Directory.Exists(dir));
        Assert.NotNull(p);
    }

    private sealed class ThrowingEnv : IResolverEnvironment
    {
        public bool FileExists(string path) => throw new IOException();
        public bool DirectoryExists(string path) => throw new IOException();
        public string? ReadAllText(string path) => throw new IOException();
        public string? GetEnvironmentVariable(string name) => throw new IOException();
        public string? GetRegistryString(RegistryRoot root, string subKey, string? valueName) => throw new IOException();
    }
}
