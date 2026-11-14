using System.Globalization;
using System.Windows;

namespace OtzariaWidget.App.SettingsUi.Localization;

/// <summary>
/// A language of the settings window. Hebrew is the source language: UI text is written in Hebrew in the code and doubles as
/// the catalog key, so it has no catalog. To add a language: a value here + <c>settings_&lt;code&gt;.json</c> + a case in <see cref="Loc"/>.
/// </summary>
public sealed record UiLanguage(string Code, string Label, FlowDirection Direction, string CultureName)
{
    /// <summary>Stored in <c>WidgetAppSettings.UiLanguage</c>; never change a code, saved settings depend on it.</summary>
    public static readonly UiLanguage Hebrew = new("he", "עברית", FlowDirection.RightToLeft, "he-IL");
    public static readonly UiLanguage English = new("en", "English", FlowDirection.LeftToRight, "en-US");

    public static UiLanguage Source => Hebrew;
    public static IReadOnlyList<UiLanguage> All { get; } = [Hebrew, English];

    /// <summary>The stored value meaning "follow the Windows display language" (the default).</summary>
    public const string SystemCode = "system";

    /// <summary>A stored code to a language; unknown or missing codes return null.</summary>
    public static UiLanguage? FromCode(string? code)
    {
        var c = code?.Trim();
        return All.FirstOrDefault(l => string.Equals(l.Code, c, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The language the window shows: the user's choice, or for <see cref="SystemCode"/> / an unknown code the Windows display
    /// language (<paramref name="systemUiCulture"/> is passed explicitly so the logic is testable).
    /// </summary>
    public static UiLanguage Resolve(string? code, CultureInfo systemUiCulture)
        => FromCode(code) ?? ForCulture(systemUiCulture);

    /// <summary>Hebrew Windows shows Hebrew; any other display language falls back to English. "iw" is the legacy ISO code.</summary>
    public static UiLanguage ForCulture(CultureInfo culture)
        => culture.TwoLetterISOLanguageName.ToLowerInvariant() is "he" or "iw" ? Hebrew : English;
}
