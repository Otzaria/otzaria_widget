using Kosher.Zmanim.HebrewCalendar;

namespace OtzariaWidget.Calendar.Hebrew;

/// <summary>Daf Yomi labels. Bavli exactly as Otzaria (calendar_date_helpers.dart formatAmud): masechta + page in gematria
/// WITHOUT geresh/gershayim ("בכורות נו"). Yerushalmi is not shown by Otzaria; same style, null when unavailable.</summary>
public static class DafYomiText
{
    public static string? Bavli(JewishCalendar jc)
    {
        try
        {
            var daf = YomiCalculator.GetDafYomiBavli(jc);
            return $"{daf.GetMasechta()} {HebrewNumbers.Letters(daf.DafNumber)}";
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            return null; // before the first cycle (11 Sep 1923): Otzaria shows "דף היומי בבלי" + an error toast
        }
    }

    public static string? Yerushalmi(JewishCalendar jc)
    {
        try
        {
            var daf = YerushalmiYomiCalculator.GetDafYomiYerushalmi(jc);
            return daf == null ? null : $"{daf.GetYerushalmiMasechta()} {HebrewNumbers.Letters(daf.DafNumber)}";
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
