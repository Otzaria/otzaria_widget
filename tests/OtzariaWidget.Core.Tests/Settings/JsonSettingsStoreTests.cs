using OtzariaWidget.Core.Contracts;
using OtzariaWidget.Core.Settings;

namespace OtzariaWidget.Core.Tests.Settings;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "otz-set-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
}

public class JsonSettingsStoreTests
{
    [Fact]
    public void Missing_file_loads_defaults()
    {
        using var d = new TempDir();
        using var s = new JsonSettingsStore(Path.Combine(d.Path, "sub"));
        var v = s.Load();
        Assert.Equal(LinkOpenMode.OpenUrl, v.LinkMode);
        Assert.Null(v.CityOverride);
        Assert.False(File.Exists(s.SettingsFilePath));
    }

    [Fact]
    public void Round_trip_all_fields_and_enums_as_strings()
    {
        using var d = new TempDir();
        using var s = new JsonSettingsStore(d.Path);
        var settings = new WidgetAppSettings
        {
            DataRootOverride = @"E:\data", DatabasesDirOverride = @"E:\dbs", LibraryDbOverride = @"E:\lib\seforim.db", OtzariaExeOverride = @"E:\o.exe",
            LinkMode = LinkOpenMode.ProviderLaunch, Theme = ThemeMode.Dark, AccentColor = "#112233",
            CityOverride = "ירושלים", ZmanimOverride = ["alos", "sunrise"], TwelveHourClockOverride = true,
            DayTransitionOverride = "tzais", ShowYerushalmiDaf = true, HideSearches = true,
        };
        s.Save(settings);

        var text = File.ReadAllText(s.SettingsFilePath);
        Assert.Contains("\"ProviderLaunch\"", text);
        Assert.Contains("\"Dark\"", text);
        Assert.StartsWith("{", text.TrimStart('\uFEFF'));

        var back = s.Load();
        Assert.Equal(settings.DataRootOverride, back.DataRootOverride);
        Assert.Equal(settings.DatabasesDirOverride, back.DatabasesDirOverride);
        Assert.Equal(settings.LibraryDbOverride, back.LibraryDbOverride);
        Assert.Equal(settings.OtzariaExeOverride, back.OtzariaExeOverride);
        Assert.Equal(LinkOpenMode.ProviderLaunch, back.LinkMode);
        Assert.Equal(ThemeMode.Dark, back.Theme);
        Assert.Equal("#112233", back.AccentColor);
        Assert.Equal("ירושלים", back.CityOverride);
        Assert.Equal(new[] { "alos", "sunrise" }, back.ZmanimOverride);
        Assert.True(back.TwelveHourClockOverride);
        Assert.Equal("tzais", back.DayTransitionOverride);
        Assert.True(back.ShowYerushalmiDaf);
        Assert.True(back.HideSearches);
        Assert.Equal(1, back.SchemaVersion);
    }

    [Fact]
    public void Corrupt_file_gives_defaults_and_keeps_backup()
    {
        using var d = new TempDir();
        using var s = new JsonSettingsStore(d.Path);
        File.WriteAllText(s.SettingsFilePath, "{ this is : not json");
        var v = s.Load();
        Assert.False(v.HideSearches);
        Assert.True(File.Exists(s.SettingsFilePath + ".bak"));
        Assert.Equal("{ this is : not json", File.ReadAllText(s.SettingsFilePath + ".bak"));
    }

    [Fact]
    public void Empty_file_and_unknown_enum_value_give_defaults()
    {
        using var d = new TempDir();
        using var s = new JsonSettingsStore(d.Path);
        File.WriteAllText(s.SettingsFilePath, "");
        Assert.Equal(ThemeMode.FollowBoard, s.Load().Theme);
        File.WriteAllText(s.SettingsFilePath, "{\"Theme\":\"Purple\"}");
        Assert.Equal(ThemeMode.FollowBoard, s.Load().Theme);
    }

    [Fact]
    public void Unknown_fields_are_ignored_and_known_kept()
    {
        using var d = new TempDir();
        using var s = new JsonSettingsStore(d.Path);
        File.WriteAllText(s.SettingsFilePath, "{\"FutureField\":{\"x\":1},\"hideSearches\":true,\"Theme\":\"Light\"}");
        var v = s.Load();
        Assert.True(v.HideSearches);
        Assert.Equal(ThemeMode.Light, v.Theme);
    }

    [Fact]
    public void Bom_prefixed_file_is_accepted()
    {
        using var d = new TempDir();
        using var s = new JsonSettingsStore(d.Path);
        File.WriteAllText(s.SettingsFilePath, "{\"HideSearches\":true}", new System.Text.UTF8Encoding(true));
        Assert.True(s.Load().HideSearches);
    }

    [Fact]
    public void Save_is_atomic_overwrites_and_leaves_no_temp_files()
    {
        using var d = new TempDir();
        using var s = new JsonSettingsStore(d.Path);
        for (var i = 0; i < 5; i++) s.Save(new WidgetAppSettings { CityOverride = "עיר" + i });
        Assert.Equal("עיר4", s.Load().CityOverride);
        Assert.Equal(new[] { "settings.json" }, Directory.GetFiles(d.Path).Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public void Save_creates_missing_directory()
    {
        using var d = new TempDir();
        var dir = Path.Combine(d.Path, "a", "b");
        using var s = new JsonSettingsStore(dir);
        s.Save(new WidgetAppSettings { HideSearches = true });
        Assert.True(s.Load().HideSearches);
    }

    [Fact]
    public void Default_path_is_under_localappdata()
    {
        using var s = new JsonSettingsStore();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.Equal(Path.Combine(local, "OtzariaWidget", "settings.json"), s.SettingsFilePath);
    }

    [Fact]
    public async Task Changed_fires_once_debounced_after_external_write()
    {
        using var d = new TempDir();
        using var s = new JsonSettingsStore(d.Path, TimeSpan.FromMilliseconds(150));
        var count = 0;
        var tcs = new TaskCompletionSource();
        s.Changed += (_, _) => { Interlocked.Increment(ref count); tcs.TrySetResult(); };

        // simulate "another process" writing: several quick writes
        for (var i = 0; i < 4; i++)
        {
            File.WriteAllText(s.SettingsFilePath, "{\"CityOverride\":\"x" + i + "\"}");
            await Task.Delay(20);
        }
        var done = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        Assert.Same(tcs.Task, done);
        await Task.Delay(500);
        Assert.Equal(1, count);
        Assert.Equal("x3", s.Load().CityOverride);
    }

    [Fact]
    public async Task Changed_fires_after_Save()
    {
        using var d = new TempDir();
        using var s = new JsonSettingsStore(d.Path, TimeSpan.FromMilliseconds(100));
        var tcs = new TaskCompletionSource();
        s.Changed += (_, _) => tcs.TrySetResult();
        s.Save(new WidgetAppSettings { HideSearches = true });
        Assert.Same(tcs.Task, await Task.WhenAny(tcs.Task, Task.Delay(5000)));
    }

    [Fact]
    public async Task Dispose_stops_events_and_unsubscribe_works()
    {
        using var d = new TempDir();
        var s = new JsonSettingsStore(d.Path, TimeSpan.FromMilliseconds(50));
        var count = 0;
        EventHandler h = (_, _) => Interlocked.Increment(ref count);
        s.Changed += h;
        s.Changed -= h;
        File.WriteAllText(s.SettingsFilePath, "{}");
        await Task.Delay(500);
        Assert.Equal(0, count);
        s.Dispose();
        s.Dispose(); // idempotent
    }

    [Fact]
    public async Task Concurrent_save_and_load_never_return_garbage()
    {
        using var d = new TempDir();
        using var s = new JsonSettingsStore(d.Path);
        s.Save(new WidgetAppSettings { CityOverride = "start" });
        var stop = false;
        var failures = 0;
        var writer = Task.Run(() => { for (var i = 0; i < 60 && !Volatile.Read(ref stop); i++) { try { s.Save(new WidgetAppSettings { CityOverride = "v" + i }); } catch { Interlocked.Increment(ref failures); } } });
        var seenDefault = 0;
        for (var i = 0; i < 100; i++)
        {
            var v = s.Load();
            if (v.CityOverride is null) seenDefault++;
        }
        await writer;
        Assert.Equal(0, seenDefault);
        Assert.Equal(0, failures);
    }
}
