using System.Globalization;

namespace OtzariaWidget.Calendar.Zmanim;

/// <summary>
/// Otzaria's zman display format (zmanim_helpers.dart formatZmanTime / zmanTimeForDisplay):
/// minutes are TRUNCATED (never rounded), followed by a "Luach Itim LaBina" seconds marker
/// ('.' when the seconds are 0-29, ':' when 30-59), and the whole token is wrapped in a
/// LEFT-TO-RIGHT ISOLATE (U+2066 ... U+2069) so the marker stays on the correct side in RTL text.
/// </summary>
public static class ZmanTimeFormatter
{
    public const char LtrIsolateStart = '⁦';
    public const char LtrIsolateEnd = '⁩';
    public const char Lrm = '‎';
    public const char Nbsp = ' ';

    /// <summary>"." for seconds 0-29, ":" for 30-59.</summary>
    public static char SecondsMarker(DateTimeOffset local) => local.Second < 30 ? '.' : ':';

    /// <summary>Otzaria token for a time that is already expressed in the city's offset (the service always returns such values).
    /// 24-hour: <c>⁦HH:mm.⁩</c>; 12-hour: <c>⁦h:mm.‎ PM⁩</c>.</summary>
    public static string Format(DateTimeOffset local, bool twelveHour = false)
    {
        var marker = SecondsMarker(local);
        var min = local.Minute.ToString("00", CultureInfo.InvariantCulture);
        if (!twelveHour)
            return $"{LtrIsolateStart}{local.Hour.ToString("00", CultureInfo.InvariantCulture)}:{min}{marker}{LtrIsolateEnd}";
        var h12 = local.Hour % 12 == 0 ? 12 : local.Hour % 12;
        var period = local.Hour < 12 ? "AM" : "PM";
        return $"{LtrIsolateStart}{h12.ToString(CultureInfo.InvariantCulture)}:{min}{marker}{Lrm}{Nbsp}{period}{LtrIsolateEnd}";
    }

    /// <summary>Same as <see cref="Format(DateTimeOffset,bool)"/> after converting an arbitrary instant to <paramref name="zone"/>.</summary>
    public static string Format(DateTimeOffset instant, TimeZoneInfo zone, bool twelveHour = false)
        => Format(TimeZoneInfo.ConvertTime(instant, zone), twelveHour);

    /// <summary>Plain "HH:mm" (truncated, no marker, no isolates) — for sorting / compact UI.</summary>
    public static string FormatPlain(DateTimeOffset local)
        => local.Hour.ToString("00", CultureInfo.InvariantCulture) + ":" + local.Minute.ToString("00", CultureInfo.InvariantCulture);
}
