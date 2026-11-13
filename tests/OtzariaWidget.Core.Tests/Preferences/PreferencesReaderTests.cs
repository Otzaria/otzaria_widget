using OtzariaWidget.Core.Contracts;
using OtzariaWidget.Core.Hive;
using OtzariaWidget.Core.Preferences;
using OtzariaWidget.Core.Tests.Hive;
using Xunit.Abstractions;
using W = OtzariaWidget.Core.Tests.Hive.HiveTestWriter;

namespace OtzariaWidget.Core.Tests.Preferences;

public class PreferencesReaderTests(ITestOutputHelper output)
{
    private static OtzariaPreferences ReadFrom(HiveTestWriter w)
    {
        using var dir = new HiveTempDir();
        var path = dir.Write("app_preferences.hive", w.ToArray());
        return new PreferencesReader().Read(path);
    }

    private sealed class FakeHive(Dictionary<string, object?> data) : IHiveReader
    {
        public Func<string, bool>? Filter;
        public IReadOnlyDictionary<string, object?> Read(string hiveFilePath, Func<string, bool> keyFilter)
        {
            Filter = keyFilter;
            return data.Where(kv => keyFilter(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        }
    }

    private sealed class ThrowingHive : IHiveReader
    {
        public IReadOnlyDictionary<string, object?> Read(string hiveFilePath, Func<string, bool> keyFilter)
            => throw new IOException("boom");
    }

    [Fact]
    public void Missing_file_gives_code_defaults()
    {
        using var dir = new HiveTempDir();
        var p = new PreferencesReader().Read(dir.File("nope.hive"));
        AssertDefaults(p);
    }

    [Fact]
    public void Empty_file_gives_code_defaults() => AssertDefaults(ReadFrom(new HiveTestWriter()));

    [Fact]
    public void Throwing_hive_reader_gives_defaults()
        => AssertDefaults(new PreferencesReader(new ThrowingHive()).Read("x"));

    private static void AssertDefaults(OtzariaPreferences p)
    {
        Assert.Equal("ירושלים", p.SelectedCity);
        Assert.Empty(p.EnabledZmanim);
        Assert.Equal("combined", p.CalendarType);
        Assert.Equal("sunset", p.DayTransition);
        Assert.False(p.TwelveHourClock);
        Assert.False(p.DarkMode);
        Assert.Null(p.LightSeedColorArgb);
        Assert.Null(p.DarkSeedColorArgb);
        Assert.Null(p.LibraryPath);
        Assert.Null(p.LibraryFolderName);
        Assert.Null(p.DatabasesPath);
        Assert.Empty(p.ShamorZachorRaw);
    }

    [Fact]
    public void Full_mapping_from_a_real_hive_file()
    {
        var w = new HiveTestWriter()
            .Put("key-selected-city", W.Str("תל אביב"))
            .Put("key-calendar-enabled-zmanim", W.Str("[\"sunrise\",\"sunset\",\"chatzos\"]"))
            .Put("key-calendar-type", W.Str("hebrew"))
            .Put("key-calendar-day-transition", W.Str("midnight"))
            .Put("key-calendar-twelve-hour-clock", W.Bool(true))
            .Put("key-dark-mode", W.Bool(true))
            .Put("key-swatch-color", W.Int(0xFF2C1B02))
            .Put("key-dark-swatch-color", W.Str("#ffce93d8"))
            .Put("key-library-path", W.Str(@"D:\lib"))
            .Put("key-library-folder-name", W.Str("אוצריא"))
            .Put("key-databases-path", W.Str(@"D:\dbs"))
            .Put("sz:tracked_books", W.Str("[1,2]"))
            .Put("sz:progress_by_id", W.Str("{}"))
            .Put("sz:sidebar_visible", W.Bool(true));
        var p = ReadFrom(w);
        Assert.Equal("תל אביב", p.SelectedCity);
        Assert.Equal(["sunrise", "sunset", "chatzos"], p.EnabledZmanim);
        Assert.Equal("hebrew", p.CalendarType);
        Assert.Equal("midnight", p.DayTransition);
        Assert.True(p.TwelveHourClock);
        Assert.True(p.DarkMode);
        Assert.Equal(0xFF2C1B02u, p.LightSeedColorArgb);
        Assert.Null(p.DarkSeedColorArgb); // Otzaria: getValue<int> on a "#aarrggbb" string => default colour
        Assert.Equal(@"D:\lib", p.LibraryPath);
        Assert.Equal("אוצריא", p.LibraryFolderName);
        Assert.Equal(@"D:\dbs", p.DatabasesPath);
        Assert.Equal(2, p.ShamorZachorRaw.Count); // bool marker is not a JSON string
        Assert.Equal("[1,2]", p.ShamorZachorRaw["sz:tracked_books"]);
    }

    [Fact]
    public void Privacy_only_whitelisted_keys_are_requested_and_exposed()
    {
        var data = new Dictionary<string, object?>
        {
            ["key-google-calendar-client-secret"] = "SECRET",
            ["key-google-calendar-credentials-json"] = "{}",
            ["key-error-report-sender-email"] = "a@b.c",
            ["key-protected-mode-password"] = "hash",
            ["key-font-family"] = "x",
            ["key-selected-city"] = "חיפה",
            ["sz:last_accessed"] = "2026-01-01T00:00:00",
        };
        var fake = new FakeHive(data);
        var p = new PreferencesReader(fake).Read("whatever");
        Assert.Equal("חיפה", p.SelectedCity);
        Assert.Single(p.ShamorZachorRaw);

        Assert.NotNull(fake.Filter);
        foreach (var bad in new[] { "key-google-calendar-client-secret", "key-google-calendar-enabled", "key-error-report-sender-email",
                                    "key-font-family", "key-calendar-events", "key-calendar-ics-subscriptions", "szx", "SZ:abc" })
            Assert.False(fake.Filter!(bad), bad);
        foreach (var good in new[] { "sz:anything", "sz:", "key-selected-city", "key-calendar-enabled-zmanim", "key-calendar-type",
                                     "key-calendar-day-transition", "key-calendar-twelve-hour-clock", "key-dark-mode",
                                     "key-swatch-color", "key-dark-swatch-color", "key-library-path", "key-library-folder-name",
                                     "key-databases-path" })
            Assert.True(fake.Filter!(good), good);
    }

    [Theory]
    [InlineData("#ffce93d8", 0xFFCE93D8u)]
    [InlineData("#80112233", 0x80112233u)]
    [InlineData("#112233", 0xFF112233u)]
    [InlineData("ff112233", 0xFF112233u)]
    [InlineData("0xFF0000FF", 0xFF0000FFu)]
    [InlineData("  #FFAABBCC ", 0xFFAABBCCu)]
    public void Color_strings(string input, uint expected)
        => Assert.Equal(expected, PreferencesReader.ParseColor(input));

    [Fact]
    public void Color_numbers()
    {
        Assert.Equal(0xFF2C1B02u, PreferencesReader.ParseColor(0xFF2C1B02L));
        Assert.Equal(0xFF2C1B02u, PreferencesReader.ParseColor((double)0xFF2C1B02));
        Assert.Equal(0xFF9C27B0u, PreferencesReader.ParseColor(unchecked((long)(int)0xFF9C27B0u))); // signed 32-bit
        Assert.Null(PreferencesReader.ParseColor(1L << 40));
        Assert.Null(PreferencesReader.ParseColor(1.5));
    }

    [Theory]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#gg112233")]
    [InlineData("#1234567890")]
    public void Bad_colors_are_null(string input) => Assert.Null(PreferencesReader.ParseColor(input));

    [Fact]
    public void Bad_color_types_are_null_not_exceptions()
    {
        var p = new PreferencesReader(new FakeHive(new()
        {
            ["key-swatch-color"] = true,
            ["key-dark-swatch-color"] = new List<object?> { 1L },
        })).Read("x");
        Assert.Null(p.LightSeedColorArgb);
        Assert.Null(p.DarkSeedColorArgb);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"a\":1}")]
    [InlineData("\"sunrise\"")]
    [InlineData("")]
    [InlineData("[\"a\",")]
    public void Bad_enabled_zmanim_json_gives_empty_list(string json)
    {
        var p = new PreferencesReader(new FakeHive(new() { ["key-calendar-enabled-zmanim"] = json })).Read("x");
        Assert.Empty(p.EnabledZmanim);
    }

    [Fact]
    public void Enabled_zmanim_ignores_non_string_entries()
    {
        var p = new PreferencesReader(new FakeHive(new() { ["key-calendar-enabled-zmanim"] = "[\"a\", 3, null, \"b\", \"\"]" })).Read("x");
        Assert.Equal(["a", "b"], p.EnabledZmanim);
    }

    [Fact]
    public void Enabled_zmanim_native_hive_list_is_ignored_like_otzaria()
    {
        // Otzaria reads this key with getValue<String>; a List value => default ('' => registry defaults)
        var p = new PreferencesReader(new FakeHive(new() { ["key-calendar-enabled-zmanim"] = new List<object?> { "x", "y" } })).Read("x");
        Assert.Empty(p.EnabledZmanim);
    }

    [Fact]
    public void Wrong_types_and_unknown_enum_values_fall_back_to_defaults()
    {
        var p = new PreferencesReader(new FakeHive(new()
        {
            ["key-selected-city"] = 5L,
            ["key-calendar-type"] = "lunar",
            ["key-calendar-day-transition"] = 3L,
            ["key-dark-mode"] = "maybe",
            ["key-calendar-twelve-hour-clock"] = new List<object?>(),
            ["key-library-path"] = "",
            ["key-library-folder-name"] = "   ",
        })).Read("x");
        AssertDefaults(p);
    }

    [Fact]
    public void Bools_follow_dart_is_bool_semantics_no_coercion()
    {
        // hive getValue<bool>: value is bool ? value : default. "true" / 1 are NOT booleans.
        var p = new PreferencesReader(new FakeHive(new()
        {
            ["key-dark-mode"] = "true",
            ["key-calendar-twelve-hour-clock"] = 1L,
        })).Read("x");
        Assert.False(p.DarkMode);
        Assert.False(p.TwelveHourClock);

        var q = new PreferencesReader(new FakeHive(new()
        {
            ["key-dark-mode"] = true,
            ["key-calendar-twelve-hour-clock"] = true,
        })).Read("x");
        Assert.True(q.DarkMode);
        Assert.True(q.TwelveHourClock);
    }

    [Fact]
    public void Enum_values_are_exact_and_case_sensitive_like_otzaria()
    {
        var p = new PreferencesReader(new FakeHive(new()
        {
            ["key-calendar-type"] = "GREGORIAN",
            ["key-calendar-day-transition"] = "rabbeinutam",
        })).Read("x");
        Assert.Equal("combined", p.CalendarType);   // _stringToCalendarType default branch
        Assert.Equal("sunset", p.DayTransition);    // calendarDayTransitionFromString default branch

        var q = new PreferencesReader(new FakeHive(new()
        {
            ["key-calendar-type"] = "gregorian",
            ["key-calendar-day-transition"] = "rabbeinuTam",
        })).Read("x");
        Assert.Equal("gregorian", q.CalendarType);
        Assert.Equal("rabbeinuTam", q.DayTransition);
    }

    [Fact]
    public void Seed_colors_only_come_from_ints_and_keep_the_low_32_bits()
    {
        var p = new PreferencesReader(new FakeHive(new()
        {
            ["key-swatch-color"] = 0xFF2C1B02L,
            ["key-dark-swatch-color"] = -1L, // Color(-1) == 0xFFFFFFFF
        })).Read("x");
        Assert.Equal(0xFF2C1B02u, p.LightSeedColorArgb);
        Assert.Equal(0xFFFFFFFFu, p.DarkSeedColorArgb);

        var q = new PreferencesReader(new FakeHive(new()
        {
            ["key-swatch-color"] = "#ffce93d8",   // older Otzaria wrote strings; current one ignores them
            ["key-dark-swatch-color"] = 1.5,
        })).Read("x");
        Assert.Null(q.LightSeedColorArgb);
        Assert.Null(q.DarkSeedColorArgb);
    }

    [Fact]
    public void Enabled_zmanim_empty_array_and_defaults_semantics()
    {
        Assert.Empty(new PreferencesReader(new FakeHive(new() { ["key-calendar-enabled-zmanim"] = "[]" })).Read("x").EnabledZmanim);
        Assert.Empty(new PreferencesReader(new FakeHive(new() { ["key-calendar-enabled-zmanim"] = "" })).Read("x").EnabledZmanim);
    }

    [Fact]
    public void Non_whitelisted_values_are_never_decoded_from_a_real_hive_file()
    {
        // A frame whose value would blow up the decoder if it were materialized: with the whitelist filter the
        // value bytes are never touched (SkippedValueCount stays 0), with an accept-all filter it is counted.
        var w = new HiveTestWriter()
            .Put("key-google-calendar-credentials-json", W.Ext(999, 1, 2, 3))
            .Put("key-selected-city", W.Str("חיפה"));
        using var dir = new HiveTempDir();
        var path = dir.Write("p.hive", w.ToArray());
        var filtered = new HiveReader().ReadDetailed(path, PreferencesReader.IsWhitelisted);
        Assert.Equal(0, filtered.SkippedValueCount);
        Assert.Equal(["key-selected-city"], filtered.Values.Keys.ToArray());
        Assert.Equal(1, new HiveReader().ReadDetailed(path, _ => true).SkippedValueCount);
    }

    [Fact]
    public void Real_data_smoke_test_counts_only()
    {
        var appData = Environment.GetEnvironmentVariable("APPDATA");
        if (string.IsNullOrEmpty(appData)) return;
        var path = Path.Combine(appData, "otzaria", "app_preferences.hive");
        if (!File.Exists(path)) return;

        var p = new PreferencesReader().Read(path);
        output.WriteLine($"cityLen={p.SelectedCity.Length} zmanim={p.EnabledZmanim.Count} sz={p.ShamorZachorRaw.Count} " +
                         $"light={(p.LightSeedColorArgb.HasValue ? "set" : "null")} dark={(p.DarkSeedColorArgb.HasValue ? "set" : "null")} " +
                         $"libPath={(p.LibraryPath is null ? "null" : "set")} calType={p.CalendarType} transition={p.DayTransition}");
        Assert.False(string.IsNullOrWhiteSpace(p.SelectedCity));
        Assert.Contains(p.CalendarType, new[] { "hebrew", "gregorian", "combined" });
        Assert.Contains(p.DayTransition, new[] { "sunset", "tzais", "rabbeinuTam", "midnight" });
        Assert.All(p.ShamorZachorRaw.Keys, k => Assert.StartsWith("sz:", k));
    }
}
