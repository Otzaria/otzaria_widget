using System.Text.RegularExpressions;

namespace OtzariaWidget.App.SettingsUi.Localization;

/// <summary>
/// Translates the Hebrew texts that Core writes (path-resolver notes, source-status messages) for the settings window.
/// Core keeps writing Hebrew because the cards show the same messages. Fixed texts are plain catalog keys; texts with
/// variable parts are matched against <see cref="Templates"/> and the captured parts are put back into the translation.
/// </summary>
public static class CoreText
{
    private const char Lre = '‪';
    private const char Pdf = '‬';

    /// <summary>
    /// Every Core message with variable parts, written exactly as Core formats it ({0}, {1}... in order of appearance).
    /// The English text is the catalog value of the same key. A test fails when Core gains a message that is not covered.
    /// </summary>
    public static readonly string[] Templates =
    [
        "שגיאה בלתי צפויה באיתור הנתיבים: {0}",
        "איתור הנתיבים נכשל: {0}",
        "תיקיית הנתונים נקבעה ידנית בהגדרות: {0}",
        "תיקיית הנתונים שהוגדרה ידנית אינה קיימת: {0}",
        "תיקיית הנתונים: {0} (%APPDATA%\\otzaria)",
        "תיקיית הנתונים: {0} (קיימת אך ריקה מקובצי אוצריא מוכרים)",
        "מצב נייד: תיקיית הנתונים {0} (נמצא portable.marker ליד {1}, מקור: {2})",
        "otzaria.exe: {0} (נקבע ידנית בהגדרות)",
        "otzaria.exe: {0} (תיקייה שנקבעה ידנית בהגדרות)",
        "otzaria.exe: {0} (גרסה ניידת)",
        "otzaria.exe: {0} (ליד otzaria_data)",
        "otzaria.exe: {0} (מקור: {1})",
        "seforim.db: {0} (מקור: {1})",
        "{0}: {1} (מקור: {2})",
        "הקובץ שהוגדר ידנית עבור otzaria.exe אינו קיים: {0}",
        "לא נמצא קובץ {0}",
        "לא ניתן לקרוא את user_state.db: {0}",
        "גרסת הסכמה לא מוכרת של user_state.db ({0}); ייתכן שנדרש עדכון של הווידג'ט",
    ];

    private static readonly Lazy<(string Key, Regex Pattern)[]> Patterns = new(() =>
        Templates.Select(t => (t, new Regex("^" + Regex.Replace(Regex.Escape(t), @"\\\{(\d)}", "(?<p$1>.+?)") + "$",
            RegexOptions.CultureInvariant | RegexOptions.Singleline))).ToArray());

    /// <summary>The translation, or the original text when it is not recognised (or the language is the source).</summary>
    public static string Translate(string text) => TryTranslate(text, Loc.Current, out var t) ? t : text;

    public static bool TryTranslate(string text, UiLanguage language, out string translated)
    {
        translated = text;
        if (language == UiLanguage.Source) return true;
        if (Loc.TryTranslate(text, language, out translated)) return true;

        foreach (var (key, pattern) in Patterns.Value)
        {
            var m = pattern.Match(text);
            if (!m.Success || !Loc.TryTranslate(key, language, out var format)) continue;
            int count = m.Groups.Keys.Count(k => k.StartsWith('p'));
            var values = new object[count];
            for (int i = 0; i < count; i++) values[i] = Part(m.Groups["p" + i].Value, language);
            translated = string.Format(System.Globalization.CultureInfo.InvariantCulture, format, values);
            return true;
        }
        translated = text;
        return false;
    }

    /// <summary>A known label (e.g. a Hebrew source name) is translated; anything else is a path or a name and stays LTR.</summary>
    private static string Part(string value, UiLanguage language)
    {
        if (!Loc.ContainsHebrew(value)) return value;
        return Loc.TryTranslate(value, language, out var t) ? t : Lre + value + Pdf;
    }
}
