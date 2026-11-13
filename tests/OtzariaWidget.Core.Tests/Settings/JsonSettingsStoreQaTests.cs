using OtzariaWidget.Core.Contracts;
using OtzariaWidget.Core.Settings;

namespace OtzariaWidget.Core.Tests.Settings;

/// <summary>QA regression tests for the settings store: one bad value must not reset everything; no flicker to defaults; no leaks.</summary>
public class JsonSettingsStoreQaTests
{
    [Fact]
    public void One_invalid_value_does_not_reset_the_other_settings()
    {
        using var d = new TempDir();
        using var s = new JsonSettingsStore(d.Path);
        File.WriteAllText(s.SettingsFilePath,
            "{\"Theme\":\"Purple\",\"HideSearches\":true,\"CityOverride\":\"חיפה\",\"LinkMode\":\"ProviderLaunch\",\"ShowYerushalmiDaf\":\"yes\",\"ZmanimOverride\":[\"alos\",5]}");
        var v = s.Load();
        Assert.Equal(ThemeMode.FollowBoard, v.Theme);          // bad enum => default for that field only
        Assert.True(v.HideSearches);
        Assert.Equal("חיפה", v.CityOverride);
        Assert.Equal(LinkOpenMode.ProviderLaunch, v.LinkMode);
        Assert.False(v.ShowYerushalmiDaf);                     // wrong type => default for that field only
        Assert.Null(v.ZmanimOverride);
        Assert.True(File.Exists(s.SettingsFilePath + ".bak")); // the original is kept for inspection
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"x\"")]
    [InlineData("{\"HideSearches\":null,\"SchemaVersion\":null,\"CityOverride\":null}")]
    [InlineData("{\"LinkMode\":99,\"Theme\":-1}")]
    public void Odd_documents_give_usable_defaults_without_throwing(string json)
    {
        using var d = new TempDir();
        using var s = new JsonSettingsStore(d.Path);
        File.WriteAllText(s.SettingsFilePath, json);
        var v = s.Load();
        Assert.NotNull(v);
        Assert.Equal(1, v.SchemaVersion);
        Assert.False(v.HideSearches);
        Assert.Null(v.CityOverride);
    }

    [Fact]
    public void Temporarily_locked_file_keeps_serving_the_last_good_settings()
    {
        using var d = new TempDir();
        using var s = new JsonSettingsStore(d.Path);
        s.Save(new WidgetAppSettings { CityOverride = "צפת", HideSearches = true });
        Assert.Equal("צפת", s.Load().CityOverride);

        using var locked = new FileStream(s.SettingsFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var v = s.Load();                                        // cannot be read right now
        Assert.Equal("צפת", v.CityOverride);
        Assert.True(v.HideSearches);
    }

    [Fact]
    public async Task Concurrent_save_and_load_never_flicker_to_defaults_even_with_a_second_store_instance()
    {
        using var d = new TempDir();
        using var writerStore = new JsonSettingsStore(d.Path);
        using var readerStore = new JsonSettingsStore(d.Path);   // like the provider process vs the settings window
        writerStore.Save(new WidgetAppSettings { CityOverride = "start" });
        Assert.Equal("start", readerStore.Load().CityOverride);

        var failures = 0;
        var writer = Task.Run(() =>
        {
            for (var i = 0; i < 150; i++)
            {
                try { writerStore.Save(new WidgetAppSettings { CityOverride = "v" + i }); } catch { Interlocked.Increment(ref failures); }
            }
        });
        var defaults = 0;
        for (var i = 0; i < 300; i++)
            if (readerStore.Load().CityOverride is null) defaults++;
        await writer;
        Assert.Equal(0, defaults);
        Assert.Equal(0, failures);
    }

    [Fact]
    public void Really_deleted_file_returns_defaults_after_a_short_wait()
    {
        using var d = new TempDir();
        using var s = new JsonSettingsStore(d.Path);
        s.Save(new WidgetAppSettings { CityOverride = "x" });
        Assert.Equal("x", s.Load().CityOverride);
        File.Delete(s.SettingsFilePath);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(s.Load().CityOverride);
        Assert.True(sw.ElapsedMilliseconds < 1000);
        Assert.Null(s.Load().CityOverride);                      // and stays default
    }

    [Fact]
    public void Saved_file_contains_only_widget_settings_and_no_secrets_or_otzaria_data()
    {
        using var d = new TempDir();
        using var s = new JsonSettingsStore(d.Path);
        s.Save(new WidgetAppSettings { CityOverride = "ירושלים" });
        var text = File.ReadAllText(s.SettingsFilePath);
        var names = System.Text.Json.JsonDocument.Parse(text).RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
        var allowed = typeof(WidgetAppSettings).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.True(names.IsSubsetOf(allowed));
        foreach (var banned in new[] { "password", "token", "email", "calendar", "history", "bookmark", "query" })
            Assert.DoesNotContain(banned, text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "settings.json" }, Directory.GetFiles(d.Path).Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public void Save_while_the_old_file_is_open_for_reading_by_another_process_succeeds_eventually()
    {
        using var d = new TempDir();
        using var s = new JsonSettingsStore(d.Path);
        s.Save(new WidgetAppSettings { CityOverride = "a" });
        // a reader that shares ReadWrite|Delete (what Load uses) must not block Save
        using var reader = new FileStream(s.SettingsFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        s.Save(new WidgetAppSettings { CityOverride = "b" });
        Assert.Equal("b", s.Load().CityOverride);
    }

    [Fact]
    public async Task Dispose_during_event_burst_does_not_throw_or_raise_after_dispose()
    {
        using var d = new TempDir();
        var s = new JsonSettingsStore(d.Path, TimeSpan.FromMilliseconds(30));
        var after = 0;
        var disposed = false;
        s.Changed += (_, _) => { if (Volatile.Read(ref disposed)) Interlocked.Increment(ref after); };
        for (var i = 0; i < 20; i++) File.WriteAllText(s.SettingsFilePath, "{\"CityOverride\":\"" + i + "\"}");
        s.Dispose();
        Volatile.Write(ref disposed, true);
        await Task.Delay(400);
        Assert.Equal(0, after);
    }

    [Fact]
    public void Subscriber_exceptions_do_not_break_the_store()
    {
        using var d = new TempDir();
        using var s = new JsonSettingsStore(d.Path, TimeSpan.FromMilliseconds(20));
        s.Changed += (_, _) => throw new InvalidOperationException("subscriber bug");
        s.Save(new WidgetAppSettings { HideSearches = true });
        Thread.Sleep(300);
        Assert.True(s.Load().HideSearches);
    }
}
