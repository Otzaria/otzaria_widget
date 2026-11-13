namespace OtzariaWidget.Calendar.Hebrew;

/// <summary>Omer counting text exactly as Otzaria builds it (calendar_times_panel.dart:276-373). Note Otzaria's
/// spellings: שלשה / חמשה / ששה (no extra vav) — arrays copied verbatim.</summary>
public static class OmerText
{
    private static readonly string[] Ones =
    [
        "", "יום אחד", "שני ימים", "שלשה ימים", "ארבעה ימים", "חמשה ימים", "ששה ימים", "שבעה ימים", "שמונה ימים", "תשעה ימים", "עשרה ימים",
        "אחד עשר יום", "שנים עשר יום", "שלשה עשר יום", "ארבעה עשר יום", "חמשה עשר יום", "ששה עשר יום", "שבעה עשר יום", "שמונה עשר יום", "תשעה עשר יום",
    ];
    private static readonly string[] Tens = ["", "", "עשרים", "שלשים", "ארבעים"];
    private static readonly string[] OnesSimple = ["", "אחד", "שנים", "שלשה", "ארבעה", "חמשה", "ששה", "שבעה", "שמונה", "תשעה"];
    private static readonly string[] OneToNine = ["", "אחד", "שני", "שלשה", "ארבעה", "חמשה", "ששה", "שבעה", "שמונה", "תשעה"];

    /// <summary>"היום שלשה ושלשים יום שהם ארבעה שבועות וחמשה ימים בעומר" for day 1..49; null otherwise.</summary>
    public static string? Build(int day)
    {
        if (day is < 1 or > 49) return null;
        var total = DayCount(day);
        var weeks = day / 7;
        var extra = day % 7;
        if (weeks == 0) return $"היום {total} בעומר";
        var weeksText = WeekCount(weeks);
        return extra == 0
            ? $"היום {total} שהם {weeksText} בעומר"
            : $"היום {total} שהם {weeksText} ו{DayCount(extra)} בעומר";
    }

    private static string DayCount(int day)
    {
        if (day <= 0 || day > 49) return $"יום {day}";
        if (day < 20) return Ones[day];
        var tens = Tens[day / 10];
        var o = day % 10;
        return o == 0 ? $"{tens} יום" : $"{OnesSimple[o]} ו{tens} יום";
    }

    private static string WeekCount(int weeks) => weeks switch
    {
        <= 0 => "",
        1 => "שבוע אחד",
        2 => "שני שבועות",
        _ => $"{OneToNine[weeks]} שבועות",
    };
}
