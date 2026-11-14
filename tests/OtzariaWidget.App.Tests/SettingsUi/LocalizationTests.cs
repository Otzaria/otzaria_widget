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

/// <summary>The path-resolver notes and source-status messages that Core writes in Hebrew, translated in the settings UI.</summary>
public class CoreTextTests
{
    private static readonly string[] CoreFiles =
    [
        Path.Combine("Paths", "OtzariaPathResolver.cs"),
        Path.Combine("Services", "OtzariaDataService.cs"),
        Path.Combine("UserState", "UserStateReader.cs"),
    ];

    /// <summary>
    /// Every Hebrew text in the files that produce notes and statuses must be a catalog key (fixed text) or a template
    /// (interpolated holes become {0}, {1}...; a literal followed by "+ value" is the template's text before its last {n}).
    /// </summary>
    [Fact]
    public void Every_core_note_and_status_message_has_an_english_translation()
    {
        var core = Path.Combine(DisplayViewModelTests.FindRepoRoot(), "src", "OtzariaWidget.Core");
        var problems = new List<string>();
        int checkedCount = 0;
        foreach (var rel in CoreFiles)
        {
            foreach (var lit in CSharpLiterals.Find(File.ReadAllText(Path.Combine(core, rel))).Where(l => Loc.ContainsHebrew(l.Value)))
            {
                checkedCount++;
                var text = lit.Value;
                if (lit.IsInterpolated)
                {
                    int n = 0;
                    text = Regex.Replace(text, @"\{[^}]*\}", _ => "{" + (n++).ToString(CultureInfo.InvariantCulture) + "}");
                }
                bool covered = Loc.TryTranslate(text, UiLanguage.English, out _) && (CoreText.Templates.Contains(text) || !Regex.IsMatch(text, @"\{\d+\}"))
                    || CoreText.Templates.Any(t => Regex.IsMatch(t, "^" + Regex.Escape(text) + @"\{\d+\}$"));
                if (!covered) problems.Add($"{rel}:{lit.Line + 1}: {text}");
            }
        }
        Assert.True(checkedCount > 30);
        Assert.True(problems.Count == 0, "Core texts without an English translation (add a catalog entry or a CoreText template):"
            + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Every_template_has_a_translation_with_the_same_placeholders()
    {
        foreach (var t in CoreText.Templates)
            Assert.True(Loc.TryTranslate(t, UiLanguage.English, out _), "missing in catalog: " + t);
    }

    [Theory]
    [InlineData("תיקיית הנתונים נקבעה ידנית בהגדרות: D:\\otzaria_data", "Data folder set manually in the settings: D:\\otzaria_data")]
    [InlineData("user_state.db: C:\\data\\databases\\user_state.db (מקור: <תיקיית נתונים>\\databases)",
        "user_state.db: C:\\data\\databases\\user_state.db (source: <data folder>\\databases)")]
    [InlineData("otzaria.exe: C:\\Program Files (x86)\\Otzaria\\otzaria.exe (מקור: תיקיית התקנה סטנדרטית (Program Files))",
        "otzaria.exe: C:\\Program Files (x86)\\Otzaria\\otzaria.exe (source: standard install folder (Program Files))")]
    [InlineData("otzaria.exe: C:\\o\\otzaria.exe (מקור: HKCU Uninstall\\InstallLocation)", "otzaria.exe: C:\\o\\otzaria.exe (source: HKCU Uninstall\\InstallLocation)")]
    [InlineData("מצב נייד: תיקיית הנתונים E:\\o\\otzaria_data (נמצא portable.marker ליד E:\\o\\otzaria.exe, מקור: הגדרות הווידג'ט)",
        "Portable mode: data folder E:\\o\\otzaria_data (portable.marker found next to E:\\o\\otzaria.exe, source: Widget settings)")]
    [InlineData("לא נמצא קובץ app_preferences.hive", "app_preferences.hive was not found")]
    [InlineData("לא נמצא קובץ user_state.db", "user_state.db was not found")]
    [InlineData("לא ניתן לקרוא את user_state.db: SQLite 11", "Cannot read user_state.db: SQLite 11")]
    [InlineData("גרסת הסכמה לא מוכרת של user_state.db (7); ייתכן שנדרש עדכון של הווידג'ט", "Unknown user_state.db schema version (7); the widget may need an update")]
    [InlineData("לא נמצא otzaria.exe", "otzaria.exe was not found")]
    public void Notes_translate_with_their_variable_parts(string hebrew, string english)
    {
        Assert.True(CoreText.TryTranslate(hebrew, UiLanguage.English, out var t));
        Assert.Equal(english, t);
        Assert.True(CoreText.TryTranslate(hebrew, UiLanguage.Hebrew, out var same));
        Assert.Equal(hebrew, same);
    }

    [Fact]
    public void A_hebrew_folder_name_inside_a_path_stays_ltr_and_unknown_notes_stay_hebrew()
    {
        Assert.True(CoreText.TryTranslate("תיקיית הנתונים: C:\\משתמש\\otzaria (%APPDATA%\\otzaria)", UiLanguage.English, out var t));
        Assert.Equal("Data folder: \u202AC:\\משתמש\\otzaria\u202C (%APPDATA%\\otzaria)", t);
        Assert.False(CoreText.TryTranslate("הערה חדשה שעוד לא תורגמה", UiLanguage.English, out var raw));
        Assert.Equal("הערה חדשה שעוד לא תורגמה", raw);
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
    public sealed record Literal(string Value, int Line, bool IsLocKey, bool IsInterpolated = false);

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
                bool interpolated = i > 0 && (src[i - 1] == '$' || (i > 1 && src[i - 1] == '@' && src[i - 2] == '$'));
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
                result.Add(new Literal(sb.ToString(), startLine, isLocKey, interpolated));
                continue;
            }
            i++;
        }
        return result;
    }
}
