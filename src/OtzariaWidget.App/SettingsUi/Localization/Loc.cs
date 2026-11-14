using System.Globalization;
using System.Text.Json;

namespace OtzariaWidget.App.SettingsUi.Localization;

/// <summary>
/// Translates settings-window text. The Hebrew source text is also the catalog key, so searching the code for what the
/// screen says still finds the place that shows it. A missing translation falls back to the Hebrew text (the tests catch it).
/// </summary>
public static class Loc
{
    /// <summary>Logical name of the embedded English catalog (same name in the app and the test assembly).</summary>
    public const string EnglishCatalogResource = "OtzariaWidget.SettingsUi.settings_en.json";

    // WPF honours the embedding controls (RLE/PDF); a Hebrew value inside an English sentence keeps its punctuation in place.
    private const char Rle = '‫';
    private const char Pdf = '‬';

    private static readonly Lazy<IReadOnlyDictionary<string, string>> English = new(() => LoadCatalog(EnglishCatalogResource));

    /// <summary>The language of the window being built. Set once at startup and again before the window is rebuilt.</summary>
    public static UiLanguage Current { get; set; } = UiLanguage.Source;

    public static IReadOnlyDictionary<string, string> EnglishCatalog => English.Value;

    public static string T(string hebrew) => Translate(hebrew, Current);

    /// <summary><paramref name="hebrew"/> is a composite format (<c>{0}</c>, <c>{1}</c>...) with the same placeholders in every catalog.</summary>
    public static string F(string hebrew, params object?[] args) => Format(hebrew, Current, args);

    public static string Translate(string hebrew, UiLanguage language)
        => TryTranslate(hebrew, language, out var text) ? text : Isolate(SourceText(hebrew), language);

    /// <summary>
    /// True when the text is shown as-is (source language) or the catalog has it. A key may carry a context after '|'
    /// ("טקסט|context") to separate identical Hebrew texts that translate differently; the context is never shown.
    /// </summary>
    public static bool TryTranslate(string hebrew, UiLanguage language, out string text)
    {
        var source = SourceText(hebrew);
        text = source;
        if (language == UiLanguage.Source) return true;
        var catalog = CatalogFor(language);
        if ((catalog.TryGetValue(hebrew, out var t) || catalog.TryGetValue(source, out t)) && !string.IsNullOrWhiteSpace(t))
        {
            text = t;
            return true;
        }
        return false;
    }

    public static string SourceText(string key)
    {
        int bar = key.IndexOf('|');
        return bar < 0 ? key : key[..bar];
    }

    public static string Format(string hebrew, UiLanguage language, params object?[] args)
    {
        var values = args.Select(a => (object?)Isolate(Convert.ToString(a, CultureInfo.InvariantCulture) ?? "", language)).ToArray();
        return string.Format(CultureInfo.InvariantCulture, Translate(hebrew, language), values);
    }

    public static IReadOnlyDictionary<string, string> CatalogFor(UiLanguage language)
        => language == UiLanguage.English ? EnglishCatalog : new Dictionary<string, string>();

    private static string Isolate(string text, UiLanguage language)
        => language.Direction == System.Windows.FlowDirection.LeftToRight && ContainsHebrew(text) ? Rle + text + Pdf : text;

    public static bool ContainsHebrew(string text) => text.Any(c => c is >= '֐' and <= '׿');

    /// <summary>Reads a flat JSON object of "hebrew": "translation"; keys starting with '@' are comments.</summary>
    internal static IReadOnlyDictionary<string, string> LoadCatalog(string resourceName)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var stream = typeof(Loc).Assembly.GetManifestResourceStream(resourceName);
            if (stream is null) return result;
            using var doc = JsonDocument.Parse(stream);
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (p.Name.StartsWith('@') || p.Value.ValueKind != JsonValueKind.String) continue;
                result[p.Name] = p.Value.GetString() ?? "";
            }
        }
        catch (Exception ex)
        {
            Infrastructure.SettingsLog.Error("Loading the translation catalog failed", ex);
        }
        return result;
    }
}
