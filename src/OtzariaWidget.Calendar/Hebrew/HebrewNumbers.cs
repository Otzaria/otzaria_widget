using System.Text;

namespace OtzariaWidget.Calendar.Hebrew;

/// <summary>
/// Gematria exactly as kosher_dart / KosherJava <c>formatHebrewNumber</c> (what Otzaria shows):
/// 15 = ט״ו, 16 = ט״ז, single letter = letter + geresh (ג׳), several letters = gershayim before the last (תשפ״ז).
/// Uses U+05F3 (׳) and U+05F4 (״) like Otzaria; no final-form letters; the thousands are dropped for years.
/// </summary>
public static class HebrewNumbers
{
    public const char Geresh = '׳';
    public const char Gershayim = '״';

    private static readonly string[] Ones = ["", "א", "ב", "ג", "ד", "ה", "ו", "ז", "ח", "ט"];
    private static readonly string[] Tens = ["", "י", "כ", "ל", "מ", "נ", "ס", "ע", "פ", "צ"];
    private static readonly string[] Hundreds = ["", "ק", "ר", "ש", "ת"];

    /// <summary>Letters only, no geresh/gershayim (e.g. 56 = נו, 15 = טו). Numbers ≥ 1000 lose their thousands.</summary>
    public static string Letters(int number)
    {
        if (number <= 0) throw new ArgumentOutOfRangeException(nameof(number));
        var n = number >= 1000 ? number % 1000 : number;
        if (n == 0) return Ones[0]; // 1000, 2000 ... → "" (callers never pass these)
        var sb = new StringBuilder();
        while (n >= 400) { sb.Append('ת'); n -= 400; }
        if (n >= 100) { sb.Append(Hundreds[n / 100]); n %= 100; }
        if (n == 15) sb.Append("טו");
        else if (n == 16) sb.Append("טז");
        else
        {
            if (n >= 10) { sb.Append(Tens[n / 10]); n %= 10; }
            if (n > 0) sb.Append(Ones[n]);
        }
        return sb.ToString();
    }

    /// <summary>With geresh/gershayim: 3 = ג׳, 15 = ט״ו, 30 = ל׳, 5787 = תשפ״ז.</summary>
    public static string Format(int number)
    {
        var letters = Letters(number);
        return letters.Length switch
        {
            0 => letters,
            1 => letters + Geresh,
            _ => letters[..^1] + Gershayim + letters[^1],
        };
    }

    /// <summary>Hebrew year without the thousands, e.g. 5787 → תשפ״ז (what Otzaria's date chip shows).</summary>
    public static string Year(int year) => Format(year);

    /// <summary>Gematria text → number (ignores geresh/gershayim/ASCII quotes); 0 when not a Hebrew numeral.</summary>
    public static int Parse(string text)
    {
        var total = 0;
        foreach (var ch in text)
        {
            if (ch is '\'' or '"' or Geresh or Gershayim) continue;
            var v = ch switch
            {
                'א' => 1, 'ב' => 2, 'ג' => 3, 'ד' => 4, 'ה' => 5, 'ו' => 6, 'ז' => 7, 'ח' => 8, 'ט' => 9,
                'י' => 10, 'כ' or 'ך' => 20, 'ל' => 30, 'מ' or 'ם' => 40, 'נ' or 'ן' => 50, 'ס' => 60, 'ע' => 70, 'פ' or 'ף' => 80, 'צ' or 'ץ' => 90,
                'ק' => 100, 'ר' => 200, 'ש' => 300, 'ת' => 400,
                _ => -1,
            };
            if (v < 0) return 0;
            total += v;
        }
        return total;
    }
}
