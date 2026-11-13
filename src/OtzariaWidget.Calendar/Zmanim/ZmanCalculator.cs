using Kosher.Zmanim;
using Kosher.Zmanim.HebrewCalendar;
using OtzariaWidget.Calendar.Hebrew;

namespace OtzariaWidget.Calendar.Zmanim;

/// <summary>Result of computing one zman: the instant plus an optional Otzaria label (kidush levana only).</summary>
public readonly record struct ZmanValue(DateTimeOffset? Time, string? Label = null);

/// <summary>
/// One computation per Otzaria registry id (lib/tools/calendar/helpers/zmanim_helpers.dart kZmanimRegistry),
/// translated to Kosher.Zmanim 3.1.1 (see docs/research/calendar-and-zmanim.md §3 for the Dart → .NET name table).
/// </summary>
public static class ZmanCalculator
{
    private static readonly TimeSpan Tikochinsky = new(0, 13, 30);

    public static bool TryCompute(string id, ZmanContext c, out ZmanValue value)
    {
        value = default;
        if (!Table.TryGetValue(id, out var f)) return false;
        try { value = f(c); }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            // Otzaria: "some computations throw on unusual dates (kidush levana)" → treated as null. Never let a single zman break the list.
            value = default;
        }
        return true;
    }

    /// <summary>Otzaria's per-day relevance predicates (zmanim_helpers.dart:376-395). null predicate = always relevant.</summary>
    public static bool IsRelevant(string id, JewishCalendar jc) => id switch
    {
        "candleLighting" => IsErevShabbosOrYomTov(jc),
        "motzeiShabbos" or "motzeiShabbosChazonIsh" => IsMotzeiShabbos(jc),
        "motzeiYomTov" or "motzeiYomTovChazonIsh" => IsMotzeiYomTov(jc),
        "achilasChametzMGADegrees" or "achilasChametzGRA" or "achilasChametzMGAShavos"
            or "biurChametzMGADegrees" or "biurChametzGRA" or "biurChametzMGAShavos" => IsErevPesach(jc),
        "fastEndTikLenient" or "fastEndItimLenient" or "fastEndTikStringent" or "fastEndItimStringent" => IsTaanisNotYomKippur(jc),
        _ => true,
    };

    // JewishCalendar.DayOfWeek: 1 = Sunday ... 7 = Shabbat (same numbering as kosher_dart).
    public static bool IsErevShabbosOrYomTov(JewishCalendar jc) => jc.DayOfWeek == 6 || (jc.IsErevYomTov() && jc.DayOfWeek != 7);
    public static bool IsMotzeiShabbos(JewishCalendar jc) => jc.IsAssurBemelacha() && jc.DayOfWeek == 7 && !jc.IsTomorrowShabbosOrYomTov();
    public static bool IsMotzeiYomTov(JewishCalendar jc) => jc.IsAssurBemelacha() && jc.IsYomTov() && jc.DayOfWeek != 7 && !jc.IsTomorrowShabbosOrYomTov();
    public static bool IsErevPesach(JewishCalendar jc) => jc.GetYomTovIndex() == JewishCalendar.ErevPesach;
    public static bool IsTaanisNotYomKippur(JewishCalendar jc) => jc.IsTaanis() && jc.GetYomTovIndex() != JewishCalendar.YomKippur;

    private static DateTimeOffset? Plus(DateTimeOffset? t, TimeSpan d) => t?.Add(d);

    private static ZmanValue T(DateTimeOffset? t) => new(t);

    /// <summary>chatzos layla (zmanim_helpers.dart calculateSolarMidnight): midpoint of today's getSunset() and tomorrow's
    /// getSunrise() (both elevation adjusted); falls back to chatzos + 12h; null when there is no sunset.</summary>
    public static DateTimeOffset? ChatzosLayla(ZmanContext c)
    {
        var sunsetToday = c.Calendar.GetSunset();
        if (sunsetToday == null) return null;
        var sunriseTomorrow = c.Tomorrow.Calendar.GetSunrise();
        if (sunriseTomorrow == null) return c.Calendar.GetChatzosHayom()?.AddHours(12);
        var half = (sunriseTomorrow.Value - sunsetToday.Value).Ticks / 2;
        return sunsetToday.Value.AddTicks(half);
    }

    private static readonly Dictionary<string, Func<ZmanContext, ZmanValue>> Table = Build();

    private static Dictionary<string, Func<ZmanContext, ZmanValue>> Build()
    {
        var d = new Dictionary<string, Func<ZmanContext, ZmanValue>>(StringComparer.Ordinal);

        // ── עלות השחר ───────────────────────────────────────────────────────────
        d["alos72Degrees"] = c => T(c.Calendar.GetAlos16Point1Degrees());
        d["alos90Degrees"] = c => T(c.Calendar.GetAlos19Point8Degrees());
        d["alos120Degrees"] = c => T(c.Calendar.GetAlos26Degrees());
        d["alos72Shavos"] = c => T(c.Calendar.GetAlos72Minutes());
        d["alos90Shavos"] = c => T(c.Calendar.GetAlos90Minutes());
        d["alos120Shavos"] = c => T(c.Calendar.GetAlos120Minutes());
        d["alos72Zmanis"] = c => T(c.Calendar.GetAlos72Zmanis());
        d["alos90Zmanis"] = c => T(c.Calendar.GetAlos90Zmanis());
        d["alos120Zmanis"] = c => T(c.Calendar.GetAlos120Zmanis());
        // ── ציצית ותפילין / זריחה ───────────────────────────────────────────────
        d["misheyakir10point2"] = c => T(c.Calendar.GetMisheyakir10Point2Degrees());
        d["misheyakir11"] = c => T(c.Calendar.GetMisheyakir11Degrees());
        d["misheyakir11point5"] = c => T(c.Calendar.GetMisheyakir11Point5Degrees());
        d["sunriseAstronomical"] = c => T(c.Calendar.GetSunrise());
        d["sunrise"] = c => T(c.Calendar.GetSeaLevelSunrise()); // "הנץ הנראה" deliberately uses sea level
        // ── קריאת שמע ───────────────────────────────────────────────────────────
        d["sofZmanShmaMGA90Degrees"] = c => T(c.Calendar.GetSofZmanShmaMGA19Point8Degrees());
        d["sofZmanShmaGRA"] = c => T(c.Calendar.GetSofZmanShmaGRA());
        d["sofZmanShmaMGA72Degrees"] = c => T(c.Calendar.GetSofZmanShmaMGA16Point1Degrees());
        d["sofZmanShmaMGA72Shavos"] = c => T(c.Calendar.GetSofZmanShmaMGA72Minutes());
        d["sofZmanShmaMGA72Zmanis"] = c => T(c.Calendar.GetSofZmanShmaMGA72MinutesZmanis());
        d["sofZmanShmaMGA90Shavos"] = c => T(c.Calendar.GetSofZmanShmaMGA90Minutes());
        d["sofZmanShmaMGA90Zmanis"] = c => T(c.Calendar.GetSofZmanShmaMGA90MinutesZmanis());
        d["sofZmanShmaMGA120Shavos"] = c => T(c.Calendar.GetSofZmanShmaMGA120Minutes());
        // ── תפילה ───────────────────────────────────────────────────────────────
        d["sofZmanTfilaMGA90Degrees"] = c => T(c.Calendar.GetSofZmanTfilaMGA19Point8Degrees());
        d["sofZmanTfilaGRA"] = c => T(c.Calendar.GetSofZmanTfilaGRA());
        d["sofZmanTfilaMGA72Degrees"] = c => T(c.Calendar.GetSofZmanTfilaMGA16Point1Degrees());
        d["sofZmanTfilaMGA72Shavos"] = c => T(c.Calendar.GetSofZmanTfilaMGA72Minutes());
        d["sofZmanTfilaMGA72Zmanis"] = c => T(c.Calendar.GetSofZmanTfilaMGA72MinutesZmanis());
        d["sofZmanTfilaMGA90Shavos"] = c => T(c.Calendar.GetSofZmanTfilaMGA90Minutes());
        d["sofZmanTfilaMGA90Zmanis"] = c => T(c.Calendar.GetSofZmanTfilaMGA90MinutesZmanis());
        d["sofZmanTfilaMGA120Shavos"] = c => T(c.Calendar.GetSofZmanTfilaMGA120Minutes());
        // ── חצות ────────────────────────────────────────────────────────────────
        d["chatzos"] = c => T(c.Calendar.GetChatzosHayom()); // UseAstronomicalChatzos=false → half-day midpoint (sea level), like kosher_dart
        d["chatzosFixedLocal"] = c => T(c.Calendar.GetFixedLocalChatzosHayom());
        d["chatzosLayla"] = c => T(ChatzosLayla(c));
        // ── מנחה ────────────────────────────────────────────────────────────────
        d["minchaGedolaGreater30"] = c => T(c.Calendar.GetMinchaGedolaGRAGreaterThan30());
        d["minchaGedolaGRA"] = c => T(c.Calendar.GetMinchaGedolaGRA());
        d["minchaGedola30"] = c => T(c.Calendar.GetMinchaGedola30Minutes());
        d["minchaGedola16point1"] = c => T(c.Calendar.GetMinchaGedola16Point1Degrees());
        d["minchaKetanaGRA"] = c => T(c.Calendar.GetMinchaKetanaGRA());
        d["minchaKetana16point1"] = c => T(c.Calendar.GetMinchaKetana16Point1Degrees());
        d["minchaKetana90Degrees"] = c =>
        {
            // Otzaria: getMinchaKetana(getAlos19Point8Degrees(), getTzais19Point8Degrees()), null if either is null
            var dawn = c.Calendar.GetAlos19Point8Degrees();
            var dusk = c.Calendar.GetTzais19Point8Degrees();
            return T(dawn == null || dusk == null ? null : c.Calendar.GetMinchaKetana(dawn, dusk));
        };
        d["minchaKetana72"] = c => T(c.Calendar.GetMinchaKetana72Minutes());
        // ── פלג המנחה ───────────────────────────────────────────────────────────
        d["plagGRA"] = c => T(c.Calendar.GetPlagHaminchaGRA());
        d["plag72Shavos"] = c => T(c.Calendar.GetPlagHamincha72Minutes());
        d["plag90Shavos"] = c => T(c.Calendar.GetPlagHamincha90Minutes());
        d["plag120Shavos"] = c => T(c.Calendar.GetPlagHamincha120Minutes());
        d["plag72Zmanis"] = c => T(c.Calendar.GetPlagHamincha72MinutesZmanis());
        d["plag90Zmanis"] = c => T(c.Calendar.GetPlagHamincha90MinutesZmanis());
        d["plag120Zmanis"] = c => T(c.Calendar.GetPlagHamincha120MinutesZmanis());
        d["plag72Degrees"] = c => T(c.Calendar.GetPlagHamincha16Point1Degrees());
        d["plag90Degrees"] = c => T(c.Calendar.GetPlagHamincha19Point8Degrees());
        d["plag120Degrees"] = c => T(c.Calendar.GetPlagHamincha26Degrees());
        d["plagAlosToSunset"] = c => T(c.Calendar.GetPlagAlosToSunset());
        d["plagAlos16ToGeonim"] = c => T(c.Calendar.GetPlagAlos16Point1DegreesToTzaisGeonim7Point083Degrees());
        // ── שקיעה / בין השמשות ──────────────────────────────────────────────────
        d["sunset"] = c => T(c.Calendar.GetSunset());
        d["seaLevelSunset"] = c => T(c.Calendar.GetSeaLevelSunset());
        d["binRT13point24"] = c => T(c.Calendar.GetBainHashmashosRT13Point24Degrees());
        d["binRT58point5"] = c => T(c.Calendar.GetBainHashmashosRT58Point5Minutes());
        d["binRT13point5"] = c => T(c.Calendar.GetBainHashmashosRT13Point5MinutesBefore7Point083Degrees());
        d["binYereim18"] = c => T(c.Calendar.GetBainHashmashosYereim18Minutes());
        d["binYereim16point875"] = c => T(c.Calendar.GetBainHashmashosYereim16Point875Minutes());
        d["binYereim13point5"] = c => T(c.Calendar.GetBainHashmashosYereim13Point5Minutes());
        d["binYereim3point5"] = c => T(c.Calendar.GetBainHashmashosYereim3Point05Degrees()); // kosher_dart's "3.5" is really 3.05°
        d["binYereim2point8"] = c => T(c.Calendar.GetBainHashmashosYereim2Point8Degrees());
        d["binYereim2point1"] = c => T(c.Calendar.GetBainHashmashosYereim2Point1Degrees());
        // ── צאת הכוכבים (fixed minutes are added to the elevation-adjusted sunset, by Otzaria's own code) ──
        d["tzeitTikochinsky"] = c => T(Plus(c.Sunset, Tikochinsky));
        d["tzeitItimLeVina"] = c => T(Plus(c.Sunset, TimeSpan.FromMinutes(18)));
        d["tzeitGeonim3point65"] = c => T(c.Calendar.GetSunsetOffsetByDegrees(90 + 3.65));
        d["tzeitGeonim7point083"] = c => T(c.Calendar.GetTzaisGeonim7Point083Degrees());
        d["tzeitGeonim8point5"] = c => T(c.Calendar.GetTzaisGeonim8Point5Degrees());
        d["tzeitChazonIsh"] = c => T(c.Calendar.GetTzaisGeonim9Point3Degrees());
        // ── רבנו תם ─────────────────────────────────────────────────────────────
        d["rt72Shavos"] = c => T(Plus(c.Sunset, TimeSpan.FromMinutes(72)));
        d["rt72Zmanis"] = c => T(c.Calendar.GetTzais72Zmanis());
        d["rt72Degrees"] = c => T(c.Calendar.GetTzais16Point1Degrees());
        d["rt90Shavos"] = c => T(c.Calendar.GetTzais90Minutes());
        d["rt90Zmanis"] = c => T(c.Calendar.GetTzais90Zmanis());
        d["rt90Degrees"] = c => T(c.Calendar.GetTzais19Point8Degrees());
        d["rt120Shavos"] = c => T(c.Calendar.GetTzais120Minutes());
        d["rt120Zmanis"] = c => T(c.Calendar.GetTzais120Zmanis());
        d["rt120Degrees"] = c => T(c.Calendar.GetTzais26Degrees());
        // ── שבת וחג (Otzaria's own rules, NOT KosherJava's getCandleLighting) ───
        d["candleLighting"] = c => T(Plus(c.Sunset, TimeSpan.FromMinutes(-c.City.CandleLightingMinutes)));
        d["motzeiShabbos"] = c => T(Plus(c.Sunset, TimeSpan.FromMinutes(34)));
        d["motzeiShabbosChazonIsh"] = c => T(Plus(c.Sunset, TimeSpan.FromMinutes(38)));
        d["motzeiYomTov"] = c => T(Plus(c.Sunset, TimeSpan.FromMinutes(34)));
        d["motzeiYomTovChazonIsh"] = c => T(Plus(c.Sunset, TimeSpan.FromMinutes(38)));
        // ── ערב פסח ─────────────────────────────────────────────────────────────
        d["achilasChametzMGADegrees"] = c => T(c.Calendar.GetSofZmanAchilasChametzMGA16Point1Degrees());
        d["achilasChametzGRA"] = c => T(c.Calendar.GetSofZmanAchilasChametzGRA());
        d["achilasChametzMGAShavos"] = c => T(c.Calendar.GetSofZmanAchilasChametzMGA72Minutes());
        d["biurChametzMGADegrees"] = c => T(c.Calendar.GetSofZmanBiurChametzMGA16Point1Degrees());
        d["biurChametzGRA"] = c => T(c.Calendar.GetSofZmanBiurChametzGRA());
        d["biurChametzMGAShavos"] = c => T(c.Calendar.GetSofZmanBiurChametzMGA72Minutes());
        // ── תעניות ──────────────────────────────────────────────────────────────
        d["fastEndTikLenient"] = c => T(Plus(c.Sunset, Tikochinsky));
        d["fastEndItimLenient"] = c => T(Plus(c.Sunset, TimeSpan.FromMinutes(18)));
        d["fastEndTikStringent"] = c => T(Plus(c.Sunset, TimeSpan.FromMinutes(18)));
        d["fastEndItimStringent"] = c => T(Plus(c.Sunset, TimeSpan.FromMinutes(22)));
        // ── קידוש לבנה (raw molad quirk, see KidushLevana) ──────────────────────
        d["tchilasKidushLevana3"] = c => KidushLevana.Compute(c, TimeSpan.FromDays(3), pushToTzais: true);
        d["tchilasKidushLevana7"] = c => KidushLevana.Compute(c, TimeSpan.FromDays(7), pushToTzais: true);
        d["sofKidushLevanaMoldos"] = c => KidushLevana.Compute(c, new TimeSpan(14, 18, 22, 1, 666), pushToTzais: false);
        d["sofKidushLevana15"] = c => KidushLevana.Compute(c, TimeSpan.FromDays(15), pushToTzais: false);
        return d;
    }
}
