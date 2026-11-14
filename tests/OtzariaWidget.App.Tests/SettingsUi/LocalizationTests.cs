using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Xml.Linq;
using OtzariaWidget.App.SettingsUi.Infrastructure;
using OtzariaWidget.App.SettingsUi.Localization;
using OtzariaWidget.App.SettingsUi.ViewModels;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.Tests.SettingsUi;

public class UiLanguageResolutionTests
{
    [Theory]
    [InlineData("system", "he-IL", "he")]
    [InlineData("system", "en-US", "en")]
    [InlineData("system", "fr-FR", "en")]   // any non-Hebrew Windows falls back to English
    [InlineData("system", "ar-SA", "en")]
    [InlineData(null, "he-IL", "he")]       // missing value = automatic
    [InlineData("", "en-GB", "en")]
    [InlineData("xx", "he-IL", "he")]       // unknown (e.g. a removed language) = automatic
    [InlineData("he", "en-US", "he")]       // an explicit choice wins over Windows
    [InlineData("en", "he-IL", "en")]
    [InlineData(" EN ", "he-IL", "en")]
    public void Choice_or_windows_display_language_decides(string? stored, string windows, string expected)
        => Assert.Equal(expected, UiLanguage.Resolve(stored, new CultureInfo(windows)).Code);

    [Fact]
    public void Invariant_culture_resolves_to_english()
        => Assert.Same(UiLanguage.English, UiLanguage.Resolve(UiLanguage.SystemCode, CultureInfo.InvariantCulture));

    [Fact]
    public void Hebrew_is_rtl_english_is_ltr()
    {
        Assert.Equal(FlowDirection.RightToLeft, UiLanguage.Hebrew.Direction);
        Assert.Equal(FlowDirection.LeftToRight, UiLanguage.English.Direction);
        Assert.Same(UiLanguage.Hebrew, UiLanguage.Source);
    }

    [Fact]
    public void Default_setting_is_automatic()
        => Assert.Equal(UiLanguage.SystemCode, new WidgetAppSettings().UiLanguage);
}

public class LocTranslationTests
{
    [Fact]
    public void Hebrew_shows_the_source_and_english_the_catalog()
    {
        Assert.Equal("מצב", Loc.Translate("מצב", UiLanguage.Hebrew));
        Assert.Equal("Status", Loc.Translate("מצב", UiLanguage.English));
    }

    [Fact]
    public void Missing_translation_falls_back_to_isolated_hebrew()
    {
        var text = Loc.Translate("טקסט שאינו בקטלוג", UiLanguage.English);
        Assert.Equal("‫טקסט שאינו בקטלוג‬", text);
        Assert.False(Loc.TryTranslate("טקסט שאינו בקטלוג", UiLanguage.English, out _));
    }

    [Fact]
    public void Context_is_never_shown_and_separates_identical_texts()
    {
        Assert.Equal("ווידג'ט אוצריא", Loc.Translate("ווידג'ט אוצריא|widgets-board", UiLanguage.Hebrew));
        Assert.Equal("Otzaria Widget", Loc.Translate("ווידג'ט אוצריא", UiLanguage.English));
        Assert.NotEqual(Loc.Translate("ווידג'ט אוצריא", UiLanguage.English), Loc.Translate("ווידג'ט אוצריא|widgets-board", UiLanguage.English));
    }

    [Fact]
    public void Format_fills_placeholders_and_isolates_hebrew_values_only_in_english()
    {
        Assert.Equal("נבחרו 3 מתוך 7 זמנים.", Loc.Format("נבחרו {0} מתוך {1} זמנים.", UiLanguage.Hebrew, 3, 7));
        Assert.Equal("3 of 7 Zmanim selected.", Loc.Format("נבחרו {0} מתוך {1} זמנים.", UiLanguage.English, 3, 7));
        Assert.Equal("The city \"‫צפת‬\" is not in the list.", Loc.Format("העיר \"{0}\" אינה ברשימה.", UiLanguage.English, "צפת"));
        Assert.Equal("העיר \"צפת\" אינה ברשימה.", Loc.Format("העיר \"{0}\" אינה ברשימה.", UiLanguage.Hebrew, "צפת"));
    }
}

/// <summary>Scans the settings UI sources: every visible Hebrew text goes through the catalog, and the catalog has no gaps or leftovers.</summary>
public class SettingsCatalogCoverageTests
{
    private static readonly Regex XamlKey = new(@"^\{l:T '((?:[^'\\]|\\.)*)'\}$", RegexOptions.Compiled);
    private static readonly Regex Placeholder = new(@"\{\d+\}", RegexOptions.Compiled);
    // A translation may keep a Hebrew name on purpose: the Widgets Board picker shows the provider under its Hebrew name.
    private static readonly HashSet<string> HebrewValueAllowed = ["ווידג'ט אוצריא|widgets-board"];

    private static string SettingsUiDir => Path.Combine(DisplayViewModelTests.FindRepoRoot(), "src", "OtzariaWidget.App", "SettingsUi");

    private static IEnumerable<string> XamlFiles => Directory.EnumerateFiles(Path.Combine(SettingsUiDir, "Views"), "*.xaml");

    /// <summary>Everything but the Localization folder itself (language names are shown in their own language).</summary>
    private static IEnumerable<string> CodeFiles => Directory.EnumerateFiles(SettingsUiDir, "*.cs", SearchOption.AllDirectories)
        .Where(f => !f.Contains(Path.DirectorySeparatorChar + "Localization" + Path.DirectorySeparatorChar));

    private static bool HasHebrew(string s) => Loc.ContainsHebrew(s);

    [Fact]
    public void Catalog_is_valid_json_without_duplicates_or_empty_values()
    {
        var path = Path.Combine(SettingsUiDir, "Localization", "settings_en.json");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var reader = new Utf8JsonReader(File.ReadAllBytes(path), new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow });
        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            var key = reader.GetString()!;
            Assert.True(seen.Add(key), "duplicate catalog key: " + key);
            reader.Read();
            Assert.Equal(JsonTokenType.String, reader.TokenType);
            Assert.False(string.IsNullOrWhiteSpace(reader.GetString()), "empty translation: " + key);
        }
        Assert.Equal(seen.Count(k => !k.StartsWith('@')), Loc.EnglishCatalog.Count);   // the embedded copy is this file
        Assert.True(Loc.EnglishCatalog.Count > 100);
    }

    [Fact]
    public void Translations_keep_placeholders_and_contain_no_hebrew()
    {
        foreach (var (key, value) in Loc.EnglishCatalog)
        {
            Assert.True(Placeholders(key).SequenceEqual(Placeholders(value)), "placeholders differ: " + key);
            if (!HebrewValueAllowed.Contains(key)) Assert.False(HasHebrew(value), "Hebrew left in the translation of: " + key);
        }

        static IEnumerable<string> Placeholders(string s) => Placeholder.Matches(s).Select(m => m.Value).Order();
    }

    [Fact]
    public void Xaml_has_no_hebrew_outside_the_catalog_and_every_key_exists()
    {
        var problems = new List<string>();
        foreach (var file in XamlFiles)
        {
            var doc = XDocument.Load(file);
            var values = doc.Descendants().SelectMany(e => e.Attributes().Select(a => a.Value))
                .Concat(doc.DescendantNodes().OfType<XText>().Select(t => t.Value));
            foreach (var v in values.Where(HasHebrew))
            {
                var m = XamlKey.Match(v.Trim());
                if (!m.Success) { problems.Add($"{Path.GetFileName(file)}: not translated: {v}"); continue; }
                var key = Regex.Replace(m.Groups[1].Value, @"\\(.)", "$1");
                if (!Loc.TryTranslate(key, UiLanguage.English, out _)) problems.Add($"{Path.GetFileName(file)}: missing in catalog: {key}");
            }
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Code_has_no_hebrew_literal_outside_the_catalog_and_every_key_exists()
    {
        var problems = new List<string>();
        foreach (var file in CodeFiles)
        {
            var src = File.ReadAllText(file);
            var lines = src.Split('\n');
            foreach (var lit in CSharpLiterals.Find(src).Where(l => HasHebrew(l.Value)))
            {
                var line = lines[lit.Line];
                if (line.Contains("// l10n: keep")) continue;   // card content in the Times preview, city data
                if (!lit.IsLocKey) { problems.Add($"{Path.GetFileName(file)}:{lit.Line + 1}: not translated: {lit.Value}"); continue; }
                if (!Loc.TryTranslate(lit.Value, UiLanguage.English, out _)) problems.Add($"{Path.GetFileName(file)}:{lit.Line + 1}: missing in catalog: {lit.Value}");
            }
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Every_catalog_key_is_still_used()
    {
        var src = Path.Combine(DisplayViewModelTests.FindRepoRoot(), "src");
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
            foreach (var lit in CSharpLiterals.Find(File.ReadAllText(file))) used.Add(lit.Value);   // Loc keys + the data service's messages
        foreach (var file in XamlFiles)
        {
            var doc = XDocument.Load(file);
            foreach (var v in doc.Descendants().SelectMany(e => e.Attributes().Select(a => a.Value)))
                if (XamlKey.Match(v.Trim()) is { Success: true } m) used.Add(Regex.Replace(m.Groups[1].Value, @"\\(.)", "$1"));
        }
        var stale = Loc.EnglishCatalog.Keys.Where(k => !used.Contains(k)).ToList();
        Assert.True(stale.Count == 0, "unused catalog keys:" + Environment.NewLine + string.Join(Environment.NewLine, stale));
    }
}

/// <summary>Switches the global language, so it must not run alongside tests that expect Hebrew texts.</summary>
[CollectionDefinition(nameof(GlobalLanguageCollection), DisableParallelization = true)]
public class GlobalLanguageCollection;

[Collection(nameof(GlobalLanguageCollection))]
public class EnglishViewModelTests
{
    [Fact]
    public void View_models_build_their_texts_in_the_current_language()
    {
        var store = new FakeSettingsStore();
        Loc.Current = UiLanguage.English;
        try
        {
            var main = UiTestHelpers.WithoutSyncContext(() => new MainViewModel(UiTestHelpers.Services(store)));
            using var _ = main.Session;
            Assert.Equal(new[] { "Status", "Data locations", "Zmanim and calendar", "Display and behavior", "About" }, main.Pages.Select(p => p.Title));
            Assert.Equal("Same as in Otzaria", main.Times.TimeFormatLabels[0]);
            Assert.Equal("Sunset", main.Times.DayTransitionOptions[1]);
            Assert.All(main.DataLocations.Items, i => Assert.False(Loc.ContainsHebrew(i.Title + i.Description + i.Validation)));
            Assert.Equal(new[] { "Automatic (Windows language)", "עברית", "English" }, main.Display.LanguageOptions);
        }
        finally { Loc.Current = UiLanguage.Source; }
    }

    [Fact]
    public void Known_data_service_messages_are_translated_and_unknown_ones_fall_back_to_the_health_text()
    {
        var data = new FakeDataService
        {
            Status =
            [
                new SourceStatus("user_state.db", DataHealth.NotFound, null),
                new SourceStatus("seforim.db", DataHealth.Unreadable, "לא ניתן לפתוח את ספריית אוצריא (seforim.db)"),
                new SourceStatus("app_preferences.hive", DataHealth.UnknownSchema, "הודעה עם חלק משתנה 42"),
            ],
        };
        Loc.Current = UiLanguage.English;
        try
        {
            var vm = UiTestHelpers.WithoutSyncContext(() =>
            {
                var session = new SettingsSession(new FakeSettingsStore());
                var hub = new SnapshotHub(data);
                hub.RefreshAsync().Wait();
                return new StatusViewModel(session, UiTestHelpers.Services(new FakeSettingsStore(), data), hub);
            });
            Assert.Equal(new[] { "Not found", "Cannot open the Otzaria library (seforim.db)", "Unknown data format" }, vm.Sources.Select(s => s.Message));
            Assert.All(vm.Rows, r => Assert.False(Loc.ContainsHebrew(r.Label + r.Note), r.Label));
        }
        finally { Loc.Current = UiLanguage.Source; }
    }
}

public class LanguagePickerTests
{
    [Fact]
    public void Picker_stores_the_code_and_asks_for_a_rebuild()
    {
        var store = new FakeSettingsStore();
        int rebuilds = 0;
        var session = UiTestHelpers.WithoutSyncContext(() => new SettingsSession(store));
        using var _ = session;
        var vm = new DisplayViewModel(session, () => rebuilds++);

        Assert.Equal(0, vm.LanguageIndex);                 // automatic by default
        vm.LanguageIndex = 2;
        Assert.Equal("en", session.Settings.UiLanguage);
        Assert.Equal(1, rebuilds);
        vm.LanguageIndex = 2;                              // same choice: nothing to rebuild
        Assert.Equal(1, rebuilds);
        vm.LanguageIndex = 1;
        Assert.Equal("he", session.Settings.UiLanguage);
        vm.LanguageIndex = 0;
        Assert.Equal(UiLanguage.SystemCode, session.Settings.UiLanguage);
        Assert.Equal(3, rebuilds);
        session.Flush();
        Assert.Equal(UiLanguage.SystemCode, store.Current.UiLanguage);
    }

    [Fact]
    public void Unknown_stored_code_shows_automatic()
    {
        var store = new FakeSettingsStore { Current = new WidgetAppSettings { UiLanguage = "fr" } };
        var session = UiTestHelpers.WithoutSyncContext(() => new SettingsSession(store));
        using var _ = session;
        Assert.Equal(0, new DisplayViewModel(session).LanguageIndex);
    }
}

/// <summary>A small C# lexer: string literals with their line, skipping comments and char literals.</summary>
internal static class CSharpLiterals
{
    public sealed record Literal(string Value, int Line, bool IsLocKey);

    public static List<Literal> Find(string src)
    {
        var result = new List<Literal>();
        int i = 0, line = 0;
        while (i < src.Length)
        {
            char c = src[i];
            if (c == '\n') { line++; i++; continue; }
            if (c == '/' && i + 1 < src.Length && src[i + 1] == '/') { while (i < src.Length && src[i] != '\n') i++; continue; }
            if (c == '/' && i + 1 < src.Length && src[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < src.Length && !(src[i] == '*' && src[i + 1] == '/')) { if (src[i] == '\n') line++; i++; }
                i += 2;
                continue;
            }
            if (c == '\'')
            {
                i++;
                while (i < src.Length && src[i] != '\'') { if (src[i] == '\\') i++; i++; }
                i++;
                continue;
            }
            if (c == '"')
            {
                bool verbatim = i > 0 && (src[i - 1] == '@' || (i > 1 && src[i - 1] == '$' && src[i - 2] == '@'));
                int startLine = line;
                var prefix = src[Math.Max(0, i - 24)..i].TrimEnd('@', '$').TrimEnd();
                bool isLocKey = prefix.EndsWith("Loc.T(", StringComparison.Ordinal) || prefix.EndsWith("Loc.F(", StringComparison.Ordinal);
                var sb = new StringBuilder();
                i++;
                while (i < src.Length)
                {
                    char d = src[i];
                    if (d == '\n') line++;
                    if (verbatim && d == '"' && i + 1 < src.Length && src[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                    if (d == '"') { i++; break; }
                    if (!verbatim && d == '\\' && i + 1 < src.Length)
                    {
                        char e = src[i + 1];
                        sb.Append(e switch { 'n' => '\n', 'r' => '\r', 't' => '\t', '0' => '\0', _ => e });
                        i += 2;
                        continue;
                    }
                    sb.Append(d);
                    i++;
                }
                result.Add(new Literal(sb.ToString(), startLine, isLocKey));
                continue;
            }
            i++;
        }
        return result;
    }
}
