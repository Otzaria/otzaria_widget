using System.Globalization;
using System.Text.Json;
using OtzariaWidget.Core.Contracts;
using OtzariaWidget.Core.Hive;

namespace OtzariaWidget.Core.Preferences;

/// <summary>
/// Loads the privacy-whitelisted subset of Otzaria's app_preferences.hive. Anything outside the whitelist
/// is never materialized. Never throws: missing/corrupt file or odd value types fall back to code defaults.
/// </summary>
public sealed class PreferencesReader : IPreferencesReader
{
    public const string ShamorZachorPrefix = "sz:";

    private static readonly HashSet<string> WhitelistedKeys = new(StringComparer.Ordinal)
    {
        "key-selected-city",
        "key-calendar-enabled-zmanim",
        "key-calendar-type",
        "key-calendar-day-transition",
        "key-calendar-twelve-hour-clock",
        "key-dark-mode",
        "key-swatch-color",
        "key-dark-swatch-color",
        "key-library-path",
        "key-library-folder-name",
        "key-databases-path",
    };

    private static readonly string[] CalendarTypes = ["hebrew", "gregorian", "combined"];
    private static readonly string[] DayTransitions = ["sunset", "tzais", "rabbeinuTam", "midnight"];

    private readonly IHiveReader _hive;

    public PreferencesReader() : this(new HiveReader()) { }

    public PreferencesReader(IHiveReader hive)
    {
        _hive = hive ?? throw new ArgumentNullException(nameof(hive));
    }

    /// <summary>The privacy whitelist used as Hive key filter.</summary>
    public static bool IsWhitelisted(string key)
        => key.StartsWith(ShamorZachorPrefix, StringComparison.Ordinal) || WhitelistedKeys.Contains(key);

    public OtzariaPreferences Read(string hiveFilePath)
    {
        IReadOnlyDictionary<string, object?> raw;
        try
        {
            raw = _hive.Read(hiveFilePath, IsWhitelisted);
        }
        catch (Exception)
        {
            return new OtzariaPreferences();
        }
        return Map(raw);
    }

    /// <summary>Maps an already-read key/value dictionary (only whitelisted keys are looked at).</summary>
    public static OtzariaPreferences Map(IReadOnlyDictionary<string, object?> raw)
    {
        var defaults = new OtzariaPreferences();

        var sz = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in raw)
        {
            if (k.StartsWith(ShamorZachorPrefix, StringComparison.Ordinal) && v is string s)
                sz[k] = s;
        }

        return new OtzariaPreferences
        {
            SelectedCity = NonEmptyString(Get(raw, "key-selected-city")) ?? defaults.SelectedCity,
            EnabledZmanim = ParseStringList(Get(raw, "key-calendar-enabled-zmanim")),
            CalendarType = OneOf(Get(raw, "key-calendar-type"), CalendarTypes) ?? defaults.CalendarType,
            DayTransition = OneOf(Get(raw, "key-calendar-day-transition"), DayTransitions) ?? defaults.DayTransition,
            TwelveHourClock = AsBool(Get(raw, "key-calendar-twelve-hour-clock")) ?? defaults.TwelveHourClock,
            DarkMode = AsBool(Get(raw, "key-dark-mode")) ?? defaults.DarkMode,
            LightSeedColorArgb = SeedColor(Get(raw, "key-swatch-color")),
            DarkSeedColorArgb = SeedColor(Get(raw, "key-dark-swatch-color")),
            LibraryPath = NonEmptyString(Get(raw, "key-library-path")),
            LibraryFolderName = NonEmptyString(Get(raw, "key-library-folder-name")),
            DatabasesPath = NonEmptyString(Get(raw, "key-databases-path")),
            ShamorZachorRaw = sz,
        };
    }

    private static object? Get(IReadOnlyDictionary<string, object?> raw, string key)
        => raw.TryGetValue(key, out var v) ? v : null;

    private static string? NonEmptyString(object? v)
        => v is string s && !string.IsNullOrWhiteSpace(s) ? s : null;

    // Otzaria reads every setting with Settings.getValue<T>, which returns the code default unless the stored
    // value `is T` (hive_data_provider.dart getValue). The helpers below reproduce exactly that: no coercion.

    /// <summary>Exact, case-sensitive match like Otzaria's _stringToCalendarType / calendarDayTransitionFromString.</summary>
    private static string? OneOf(object? v, string[] allowed)
    {
        if (v is not string s) return null;
        foreach (var a in allowed)
            if (string.Equals(a, s, StringComparison.Ordinal)) return a;
        return null;
    }

    private static bool? AsBool(object? v) => v is bool b ? b : null;

    /// <summary>
    /// Seed colours are read with getValue&lt;int&gt;: an int (stored as a Hive double) is used, anything else (e.g. the
    /// "#aarrggbb" strings an older Otzaria wrote) makes Otzaria fall back to its default theme colour, so we return null
    /// (= default) for those too. <c>Color(int)</c> keeps only the low 32 bits.
    /// </summary>
    private static uint? SeedColor(object? v) => v switch
    {
        long l => unchecked((uint)(l & 0xFFFFFFFFL)),
        _ => null,
    };

    /// <summary>
    /// Mirrors CalendarCubit._parseEnabledZmanim: only a JSON string holding an array counts (strings inside it are kept,
    /// everything else dropped); a stored non-string value, '' or invalid JSON means "use registry defaults" (= empty here).
    /// Note: a valid "[]" (user disabled every zman) is also returned as empty because the contract cannot tell them apart.
    /// </summary>
    private static IReadOnlyList<string> ParseStringList(object? v)
    {
        if (v is not string json || string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            var result = new List<string>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.String && e.GetString() is { Length: > 0 } s)
                    result.Add(s);
            }
            return result;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>Generic colour parser (int ARGB or "#aarrggbb" / "#rrggbb" / "0x..." strings). Not used for Otzaria's seed colours, see <see cref="SeedColor"/>.</summary>
    public static uint? ParseColor(object? v)
    {
        switch (v)
        {
            case long l:
                if (l >= 0 && l <= uint.MaxValue) return (uint)l;
                if (l >= int.MinValue && l < 0) return unchecked((uint)(int)l);
                return null;
            case double d:
                if (d == Math.Floor(d) && !double.IsInfinity(d)) return ParseColor((long)d);
                return null;
            case string s:
                s = s.Trim();
                if (s.StartsWith('#')) s = s[1..];
                else if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
                if (s.Length == 6 && uint.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb))
                    return 0xFF000000u | rgb;
                if (s.Length == 8 && uint.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var argb))
                    return argb;
                return null;
            default:
                return null;
        }
    }
}
