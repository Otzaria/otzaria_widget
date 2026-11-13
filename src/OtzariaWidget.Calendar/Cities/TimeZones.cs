using System.Collections.Concurrent;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.Calendar.Cities;

/// <summary>
/// Resolves a city's IANA time zone id. .NET 6+ on Windows maps IANA ids through ICU
/// (<c>TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem")</c> works on Windows 10 1903+).
/// Fallbacks: explicit IANA→Windows id conversion, then a fixed offset derived from the longitude
/// (never throws; a wrong-by-DST fallback is better than no zmanim).
/// </summary>
public static class TimeZones
{
    private static readonly ConcurrentDictionary<string, TimeZoneInfo> Cache = new();

    /// <summary>Windows time zone ids for every IANA zone used by Otzaria's city table (used only when ICU cannot map the IANA id).</summary>
    internal static readonly IReadOnlyDictionary<string, string> WindowsIds = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Africa/Cairo"] = "Egypt Standard Time",
        ["Africa/Casablanca"] = "Morocco Standard Time",
        ["Africa/Johannesburg"] = "South Africa Standard Time",
        ["America/Argentina/Buenos_Aires"] = "Argentina Standard Time",
        ["America/Caracas"] = "Venezuela Standard Time",
        ["America/Chicago"] = "Central Standard Time",
        ["America/Denver"] = "Mountain Standard Time",
        ["America/Detroit"] = "Eastern Standard Time",
        ["America/Edmonton"] = "Mountain Standard Time",
        ["America/Lima"] = "SA Pacific Standard Time",
        ["America/Los_Angeles"] = "Pacific Standard Time",
        ["America/Mexico_City"] = "Central Standard Time (Mexico)",
        ["America/New_York"] = "Eastern Standard Time",
        ["America/Phoenix"] = "US Mountain Standard Time",
        ["America/Santiago"] = "Pacific SA Standard Time",
        ["America/Sao_Paulo"] = "E. South America Standard Time",
        ["America/Toronto"] = "Eastern Standard Time",
        ["America/Vancouver"] = "Pacific Standard Time",
        ["Asia/Bangkok"] = "SE Asia Standard Time",
        ["Asia/Dubai"] = "Arabian Standard Time",
        ["Asia/Hong_Kong"] = "China Standard Time",
        ["Asia/Jerusalem"] = "Israel Standard Time",
        ["Asia/Kolkata"] = "India Standard Time",
        ["Asia/Kuwait"] = "Arab Standard Time",
        ["Asia/Seoul"] = "Korea Standard Time",
        ["Asia/Shanghai"] = "China Standard Time",
        ["Asia/Singapore"] = "Singapore Standard Time",
        ["Asia/Tokyo"] = "Tokyo Standard Time",
        ["Atlantic/Reykjavik"] = "Greenwich Standard Time",
        ["Australia/Brisbane"] = "E. Australia Standard Time",
        ["Australia/Melbourne"] = "AUS Eastern Standard Time",
        ["Australia/Perth"] = "W. Australia Standard Time",
        ["Australia/Sydney"] = "AUS Eastern Standard Time",
        ["Europe/Amsterdam"] = "W. Europe Standard Time",
        ["Europe/Berlin"] = "W. Europe Standard Time",
        ["Europe/Budapest"] = "Central Europe Standard Time",
        ["Europe/Copenhagen"] = "Romance Standard Time",
        ["Europe/Dublin"] = "GMT Standard Time",
        ["Europe/Helsinki"] = "FLE Standard Time",
        ["Europe/Istanbul"] = "Turkey Standard Time",
        ["Europe/Lisbon"] = "GMT Standard Time",
        ["Europe/London"] = "GMT Standard Time",
        ["Europe/Madrid"] = "Romance Standard Time",
        ["Europe/Moscow"] = "Russian Standard Time",
        ["Europe/Oslo"] = "W. Europe Standard Time",
        ["Europe/Paris"] = "Romance Standard Time",
        ["Europe/Prague"] = "Central Europe Standard Time",
        ["Europe/Rome"] = "W. Europe Standard Time",
        ["Europe/Stockholm"] = "W. Europe Standard Time",
        ["Europe/Vienna"] = "W. Europe Standard Time",
        ["Europe/Warsaw"] = "Central European Standard Time",
        ["Europe/Zurich"] = "W. Europe Standard Time",
    };

    public static TimeZoneInfo Resolve(CityInfo city) => Resolve(city.TimeZoneId, city.Longitude);

    public static TimeZoneInfo Resolve(string ianaId, double longitude = 0)
    {
        return Cache.GetOrAdd(ianaId + "|" + (string.IsNullOrEmpty(ianaId) ? longitude.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : ""), _ =>
        {
            if (!string.IsNullOrWhiteSpace(ianaId))
            {
                try { return Corrected(ianaId, TimeZoneInfo.FindSystemTimeZoneById(ianaId)); }
                catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { }

                try
                {
                    if (TimeZoneInfo.TryConvertIanaIdToWindowsId(ianaId, out var windowsId) && windowsId != null)
                        return Corrected(ianaId, TimeZoneInfo.FindSystemTimeZoneById(windowsId));
                }
                catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { }
            }

            // ICU unavailable (invariant globalization): fall back to the built-in IANA -> Windows id table for Otzaria's cities.
            if (WindowsIds.TryGetValue(ianaId ?? "", out var mapped))
            {
                try { return Corrected(ianaId!, TimeZoneInfo.FindSystemTimeZoneById(mapped)); }
                catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { }
            }

            var hours = Math.Round(longitude / 15.0);
            var offset = TimeSpan.FromHours(Math.Clamp(hours, -12, 14));
            return TimeZoneInfo.CreateCustomTimeZone("fixed:" + ianaId, offset, ianaId, ianaId);
        });
    }

    // ── tzdata drift ────────────────────────────────────────────────────────────────────────────────────────
    // Otzaria computes with the Dart `timezone` package (IANA tzdata 2026e). Windows' own time zone table can lag behind.
    // tzdata 2026 made Alberta (UTC-6) and British Columbia (UTC-7) permanent from 2026-11-01. A stale Windows zone keeps
    // switching to winter time (UTC-7 / UTC-8) which would shift every zman of Edmonton / Calgary / Vancouver by one hour.
    // The correction is applied only while the system zone disagrees with tzdata, so it disappears once Windows catches up.
    private static readonly DateTime ProbeUtc = new(2027, 1, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly PermanentFrom = new(2026, 11, 1);

    private static TimeZoneInfo Corrected(string ianaId, TimeZoneInfo system)
    {
        // (winter offset stored by a stale Windows zone → permanent offset per tzdata 2026e)
        int? permanentHours = ianaId switch { "America/Edmonton" => -6, "America/Vancouver" => -7, _ => null };
        if (permanentHours == null) return system;
        try
        {
            if (system.GetUtcOffset(ProbeUtc) == TimeSpan.FromHours(permanentHours.Value)) return system; // already up to date
            var baseOffset = TimeSpan.FromHours(permanentHours.Value - 1); // MST (Edmonton -7) / PST (Vancouver -8)
            var rules = new List<TimeZoneInfo.AdjustmentRule>
            {
                // until 2026-10-31: ordinary North-American DST (2nd Sunday of March → 1st Sunday of November)
                TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                    DateTime.MinValue.Date, new DateTime(2026, 10, 31), TimeSpan.FromHours(1),
                    TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday),
                    TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday)),
                // from 2026-11-01: base offset +1 h, no DST (UTC-6 / UTC-7 all year; no offset change at the switch itself)
                TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                    PermanentFrom.ToDateTime(TimeOnly.MinValue), DateTime.MaxValue.Date, TimeSpan.Zero,
                    TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 1, 1),
                    TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 1), 1, 1),
                    TimeSpan.FromHours(1)),
            };
            return TimeZoneInfo.CreateCustomTimeZone(ianaId, baseOffset, ianaId, ianaId, ianaId, rules.ToArray());
        }
        catch (Exception e) when (e is ArgumentException or InvalidTimeZoneException) { return system; }
    }
}
