// ZmanimSpike - verifies that Kosher.Zmanim 3.1.1 (.NET port of KosherJava 3.1.0) can reproduce
// what Otzaria (kosher_dart 2.0.20) displays.
//
// Usage:
//   dotnet run                                  -> human readable report for 2026-11-13 (Jerusalem + a few cities)
//   dotnet run -- report <city|all> <yyyy-MM-dd>
//   dotnet run -- dump <city,city> <yyyy-MM-dd,yyyy-MM-dd>   -> machine lines "city|date|id|HH:mm:ss" (diffable vs the Dart ground truth)
//   dotnet run -- hebrew <yyyy-MM-dd> <days> <israel|diaspora>  -> machine lines for Hebrew date/parsha/holidays/daf/omer
using System.Text;
using System.Text.Json;
using Kosher.Zmanim;
using Kosher.Zmanim.HebrewCalendar;
using Kosher.Zmanim.Util;

Console.OutputEncoding = Encoding.UTF8;
// GOTCHA: on a he-IL Windows the current culture formats DateOnly/DateTime with the *Hebrew calendar* ("תשפ\"ז-ג'-ג'").
// Always format machine/Gregorian dates with the invariant culture.
System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

var citiesPath = FindCitiesJson();
var cities = LoadCities(citiesPath);

var mode = args.Length > 0 ? args[0] : "report";
switch (mode)
{
    case "dump":
        foreach (var cityName in args[1].Split(','))
            foreach (var d in args[2].Split(','))
                Dump(cities[cityName], ParseDate(d));
        break;
    case "tz":
        foreach (var z in new[] { "America/Edmonton", "America/Vancouver", "Africa/Casablanca", "America/Los_Angeles" })
            foreach (var d in new[] { new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc) })
                Console.WriteLine($"{z} {d:yyyy-MM-dd} offset={TimeZoneInfo.FindSystemTimeZoneById(z).GetUtcOffset(d)}");
        break;
    case "netcal":
        NetCalendarProbe();
        break;
    case "molad":
        MoladDump();
        break;
    case "hebrew":
        HebrewDump(ParseDate(args[1]), int.Parse(args[2]), args[3] == "israel");
        break;
    default:
        var date = args.Length > 2 ? ParseDate(args[2]) : new DateOnly(2026, 11, 13);
        var otz = OtzariaSettings.Read();
        Console.WriteLine("Otzaria settings (copy of %APPDATA%/otzaria/app_preferences.hive): " +
            (otz.Count == 0 ? "<not found>" : string.Join(", ", otz.Select(kv => $"{kv.Key}={kv.Value}"))));
        var configured = otz.TryGetValue("key-selected-city", out var cc) && cities.ContainsKey(cc) ? cc : "ירושלים";
        var which = args.Length > 1 ? args[1] : $"ירושלים,{configured}";
        foreach (var cityName in which == "all" ? cities.Keys.ToArray() : which.Split(','))
            Report(cities[cityName], date);
        break;
}
return;

// ---------------------------------------------------------------------------------------------
// City data (exported verbatim from Otzaria's calendar_location.dart)
// ---------------------------------------------------------------------------------------------
static string FindCitiesJson()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        var p = Path.Combine(dir.FullName, "docs", "research", "otzaria-cities.json");
        if (File.Exists(p)) return p;
        dir = dir.Parent;
    }
    throw new FileNotFoundException("docs/research/otzaria-cities.json not found above " + AppContext.BaseDirectory);
}

static Dictionary<string, City> LoadCities(string path)
{
    using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
    var result = new Dictionary<string, City>();
    foreach (var c in doc.RootElement.GetProperty("cities").EnumerateArray())
    {
        var city = new City(
            c.GetProperty("name").GetString()!,
            c.GetProperty("country").GetString()!,
            c.GetProperty("lat").GetDouble(),
            c.GetProperty("lng").GetDouble(),
            c.GetProperty("elevation").GetDouble(),
            c.GetProperty("timezone").GetString()!,
            c.GetProperty("inIsrael").GetBoolean(),
            c.GetProperty("candleLightingMinutes").GetInt32());
        result[city.Name] = city;
    }
    return result;
}

// ---------------------------------------------------------------------------------------------
// Zmanim, one entry per Otzaria registry id (lib/tools/calendar/helpers/zmanim_helpers.dart:404-1356)
// ---------------------------------------------------------------------------------------------
static ComprehensiveZmanimCalendar BuildCalendar(City city, DateOnly date)
{
    // Otzaria: location.setElevation(elevation > 0 ? elevation : 0)   (zmanim_helpers.dart:45)
    var tz = TimeZoneInfo.FindSystemTimeZoneById(city.TimeZone); // IANA ids work on Windows via ICU
    var geo = new GeoLocation(city.Name, city.Lat, city.Lng, city.Elevation > 0 ? city.Elevation : 0, tz);
    var cal = new ComprehensiveZmanimCalendar(geo) { LocalDate = date };
    // --- Settings needed to reproduce kosher_dart 2.0.20 (KosherJava 2.x behaviour) ---
    // 1) kosher_dart uses a constant 16' solar radius; KosherJava 3 defaults to the date-based apparent radius (shifts sunrise/sunset by 1-2 s).
    cal.AstronomicalCalculator.UseApparentSolarRadius = false;
    // 2) kosher_dart chatzos = midpoint of sea-level sunrise/sunset (half-day); KosherJava 3 defaults to true astronomical noon (up to 30 s apart).
    cal.UseAstronomicalChatzos = false;
    cal.UseAstronomicalChatzosForOtherZmanim = false;
    return cal;
}

static List<(string Id, string Title, string Subtitle, DateTimeOffset? Time)> ComputeAll(City city, DateOnly date)
{
    var c = BuildCalendar(city, date);
    DateTimeOffset? Plus(DateTimeOffset? t, TimeSpan d) => t?.Add(d);
    // Otzaria chatzosLayla (zmanim_helpers.dart:330-359): midpoint between today's getSunset() and tomorrow's getSunrise()
    DateTimeOffset? Midnight()
    {
        var sunsetToday = c.GetSunset();
        if (sunsetToday == null) return null;
        var tomorrow = BuildCalendar(city, date.AddDays(1));
        var sunriseTomorrow = tomorrow.GetSunrise();
        if (sunriseTomorrow == null) return c.GetChatzosHayom()?.AddHours(12);
        var ms = (sunriseTomorrow.Value - sunsetToday.Value).Ticks / 2;
        return sunsetToday.Value.AddTicks(ms);
    }
    var sunset = c.GetSunset(); // elevation adjusted, same as kosher_dart getSunset()

    var l = new List<(string, string, string, DateTimeOffset?)>();
    void A(string id, string title, string sub, DateTimeOffset? t) => l.Add((id, title, sub, t));

    A("alos72Degrees", "עלות השחר", "72 דק׳ (מעלות)", c.GetAlos16Point1Degrees());
    A("alos90Degrees", "עלות השחר", "90 דק׳ (מעלות)", c.GetAlos19Point8Degrees());
    A("alos120Degrees", "עלות השחר", "120 דק׳ (מעלות)", c.GetAlos26Degrees());
    A("alos72Shavos", "עלות השחר", "72 דק׳ (שוות)", c.GetAlos72Minutes());
    A("alos90Shavos", "עלות השחר", "90 דק׳ (שוות)", c.GetAlos90Minutes());
    A("alos120Shavos", "עלות השחר", "120 דק׳ (שוות)", c.GetAlos120Minutes());
    A("alos72Zmanis", "עלות השחר", "72 דק׳ (זמניות)", c.GetAlos72Zmanis());
    A("alos90Zmanis", "עלות השחר", "90 דק׳ (זמניות)", c.GetAlos90Zmanis());
    A("alos120Zmanis", "עלות השחר", "120 דק׳ (זמניות)", c.GetAlos120Zmanis());
    A("misheyakir10point2", "זמן ציצית ותפילין", "", c.GetMisheyakir10Point2Degrees());
    A("misheyakir11", "זמן ציצית ותפילין", "11°", c.GetMisheyakir11Degrees());
    A("misheyakir11point5", "זמן ציצית ותפילין", "11.5°", c.GetMisheyakir11Point5Degrees());
    A("sunriseAstronomical", "נץ אסטרונומי", "בתיקון גובה", c.GetSunrise());
    A("sunrise", "הנץ הנראה", "", c.GetSeaLevelSunrise());
    A("sofZmanShmaMGA90Degrees", "סוף זמן ק\"ש", "מג\"א 90 דק׳ (מעלות)", c.GetSofZmanShmaMGA19Point8Degrees());
    A("sofZmanShmaGRA", "סוף זמן ק\"ש", "גר\"א", c.GetSofZmanShmaGRA());
    A("sofZmanShmaMGA72Degrees", "סוף זמן ק\"ש", "מג\"א 72 דק׳ (מעלות)", c.GetSofZmanShmaMGA16Point1Degrees());
    A("sofZmanShmaMGA72Shavos", "סוף זמן ק\"ש", "מג\"א 72 דק׳ (שוות)", c.GetSofZmanShmaMGA72Minutes());
    A("sofZmanShmaMGA72Zmanis", "סוף זמן ק\"ש", "מג\"א 72 דק׳ (זמניות)", c.GetSofZmanShmaMGA72MinutesZmanis());
    A("sofZmanShmaMGA90Shavos", "סוף זמן ק\"ש", "מג\"א 90 דק׳ (שוות)", c.GetSofZmanShmaMGA90Minutes());
    A("sofZmanShmaMGA90Zmanis", "סוף זמן ק\"ש", "מג\"א 90 דק׳ (זמניות)", c.GetSofZmanShmaMGA90MinutesZmanis());
    A("sofZmanShmaMGA120Shavos", "סוף זמן ק\"ש", "מג\"א 120 דק׳ (שוות)", c.GetSofZmanShmaMGA120Minutes());
    A("sofZmanTfilaMGA90Degrees", "סוף זמן תפילה", "מג\"א 90 דק׳ (מעלות)", c.GetSofZmanTfilaMGA19Point8Degrees());
    A("sofZmanTfilaGRA", "סוף זמן תפילה", "גר\"א", c.GetSofZmanTfilaGRA());
    A("sofZmanTfilaMGA72Degrees", "סוף זמן תפילה", "מג\"א 72 דק׳ (מעלות)", c.GetSofZmanTfilaMGA16Point1Degrees());
    A("sofZmanTfilaMGA72Shavos", "סוף זמן תפילה", "מג\"א 72 דק׳ (שוות)", c.GetSofZmanTfilaMGA72Minutes());
    A("sofZmanTfilaMGA72Zmanis", "סוף זמן תפילה", "מג\"א 72 דק׳ (זמניות)", c.GetSofZmanTfilaMGA72MinutesZmanis());
    A("sofZmanTfilaMGA90Shavos", "סוף זמן תפילה", "מג\"א 90 דק׳ (שוות)", c.GetSofZmanTfilaMGA90Minutes());
    A("sofZmanTfilaMGA90Zmanis", "סוף זמן תפילה", "מג\"א 90 דק׳ (זמניות)", c.GetSofZmanTfilaMGA90MinutesZmanis());
    A("sofZmanTfilaMGA120Shavos", "סוף זמן תפילה", "מג\"א 120 דק׳ (שוות)", c.GetSofZmanTfilaMGA120Minutes());
    A("chatzos", "חצות היום", "", c.GetChatzosHayom());
    A("chatzosFixedLocal", "חצות מקומי קבוע", "", c.GetFixedLocalChatzosHayom());
    A("chatzosLayla", "חצות לילה", "", Midnight());
    A("minchaGedolaGreater30", "מנחה גדולה", "המאוחר", c.GetMinchaGedolaGRAGreaterThan30());
    A("minchaGedolaGRA", "מנחה גדולה", "גר\"א", c.GetMinchaGedolaGRA());
    A("minchaGedola30", "מנחה גדולה", "30 דק׳ שוות", c.GetMinchaGedola30Minutes());
    A("minchaGedola16point1", "מנחה גדולה", "16.1°", c.GetMinchaGedola16Point1Degrees());
    A("minchaKetanaGRA", "מנחה קטנה", "גר\"א", c.GetMinchaKetanaGRA());
    A("minchaKetana16point1", "מנחה קטנה", "72 דק׳ (מעלות)", c.GetMinchaKetana16Point1Degrees());
    // Otzaria: getMinchaKetana(alos19.8, tzais19.8) (zmanim_helpers.dart:793-799)
    {
        var dawn = c.GetAlos19Point8Degrees();
        var dusk = c.GetTzais19Point8Degrees();
        A("minchaKetana90Degrees", "מנחה קטנה", "90 דק׳ (מעלות)", dawn == null || dusk == null ? null : c.GetMinchaKetana(dawn, dusk));
    }
    A("minchaKetana72", "מנחה קטנה", "72 דק׳ (שוות)", c.GetMinchaKetana72Minutes());
    A("plagGRA", "פלג המנחה", "גר\"א", c.GetPlagHaminchaGRA());
    A("plag72Shavos", "פלג המנחה", "72 דק׳ (שוות)", c.GetPlagHamincha72Minutes());
    A("plag90Shavos", "פלג המנחה", "90 דק׳ (שוות)", c.GetPlagHamincha90Minutes());
    A("plag120Shavos", "פלג המנחה", "120 דק׳ (שוות)", c.GetPlagHamincha120Minutes());
    A("plag72Zmanis", "פלג המנחה", "72 דק׳ (זמניות)", c.GetPlagHamincha72MinutesZmanis());
    A("plag90Zmanis", "פלג המנחה", "90 דק׳ (זמניות)", c.GetPlagHamincha90MinutesZmanis());
    A("plag120Zmanis", "פלג המנחה", "120 דק׳ (זמניות)", c.GetPlagHamincha120MinutesZmanis());
    A("plag72Degrees", "פלג המנחה", "72 דק׳ (מעלות)", c.GetPlagHamincha16Point1Degrees());
    A("plag90Degrees", "פלג המנחה", "90 דק׳ (מעלות)", c.GetPlagHamincha19Point8Degrees());
    A("plag120Degrees", "פלג המנחה", "120 דק׳ (מעלות)", c.GetPlagHamincha26Degrees());
    A("plagAlosToSunset", "פלג המנחה", "מעלוה\"ש עד שקיעה", c.GetPlagAlosToSunset());
    A("plagAlos16ToGeonim", "פלג המנחה", "מ-16.1° עד גאונים 7.083°", c.GetPlagAlos16Point1DegreesToTzaisGeonim7Point083Degrees());
    A("sunset", "שקיעה", "", sunset);
    A("seaLevelSunset", "שקיעה מישורית", "", c.GetSeaLevelSunset());
    A("binRT13point24", "בין השמשות", "ר\"ת 13.24°", c.GetBainHashmashosRT13Point24Degrees());
    A("binRT58point5", "בין השמשות", "ר\"ת 58.5 דק׳", c.GetBainHashmashosRT58Point5Minutes());
    A("binRT13point5", "בין השמשות", "ר\"ת 13.5 דק׳", c.GetBainHashmashosRT13Point5MinutesBefore7Point083Degrees());
    A("binYereim18", "בין השמשות", "יראים 18 דק׳", c.GetBainHashmashosYereim18Minutes());
    A("binYereim16point875", "בין השמשות", "יראים 16.875 דק׳", c.GetBainHashmashosYereim16Point875Minutes());
    A("binYereim13point5", "בין השמשות", "יראים 13.5 דק׳", c.GetBainHashmashosYereim13Point5Minutes());
    A("binYereim3point5", "בין השמשות", "יראים 3.5°", c.GetBainHashmashosYereim3Point05Degrees());
    A("binYereim2point8", "בין השמשות", "יראים 2.8°", c.GetBainHashmashosYereim2Point8Degrees());
    A("binYereim2point1", "בין השמשות", "יראים 2.1°", c.GetBainHashmashosYereim2Point1Degrees());
    A("tzeitTikochinsky", "צאת הכוכבים", "טיקוצ׳ינסקי", Plus(sunset, new TimeSpan(0, 13, 30)));
    A("tzeitItimLeVina", "צאת הכוכבים", "עיתים לבינה", Plus(sunset, TimeSpan.FromMinutes(18)));
    // kosher_dart getTzaisGeonim3Point65Degrees() == getSunsetOffsetByDegrees(90 + 3.65); not exposed by KosherJava 3.x
    A("tzeitGeonim3point65", "צאת הכוכבים", "גאונים 3.65°", c.GetSunsetOffsetByDegrees(90 + 3.65));
    A("tzeitGeonim7point083", "צאת הכוכבים", "גאונים 7.083°", c.GetTzaisGeonim7Point083Degrees());
    A("tzeitGeonim8point5", "צאת הכוכבים", "גאונים 8.5°", c.GetTzaisGeonim8Point5Degrees());
    A("tzeitChazonIsh", "צאת הכוכבים", "חזו\"א (9.3°)", c.GetTzaisGeonim9Point3Degrees());
    A("rt72Shavos", "רבנו תם", "72 דק׳ (שוות)", Plus(sunset, TimeSpan.FromMinutes(72)));
    A("rt72Zmanis", "רבנו תם", "72 דק׳ (זמניות)", c.GetTzais72Zmanis());
    A("rt72Degrees", "רבנו תם", "72 דק׳ (מעלות)", c.GetTzais16Point1Degrees());
    A("rt90Shavos", "רבנו תם", "90 דק׳ (שוות)", c.GetTzais90Minutes());
    A("rt90Zmanis", "רבנו תם", "90 דק׳ (זמניות)", c.GetTzais90Zmanis());
    A("rt90Degrees", "רבנו תם", "90 דק׳ (מעלות)", c.GetTzais19Point8Degrees());
    A("rt120Shavos", "רבנו תם", "120 דק׳ (שוות)", c.GetTzais120Minutes());
    A("rt120Zmanis", "רבנו תם", "120 דק׳ (זמניות)", c.GetTzais120Zmanis());
    A("rt120Degrees", "רבנו תם", "120 דק׳ (מעלות)", c.GetTzais26Degrees());
    // Otzaria: sunset - candleLightingMinutesByCity (zmanim_helpers.dart:362-370), NOT KosherJava's 18 min offset
    A("candleLighting", "הדלקת נרות", "", Plus(sunset, TimeSpan.FromMinutes(-city.CandleLightingMinutes)));
    A("motzeiShabbos", "מוצאי שבת", "רגיל", Plus(sunset, TimeSpan.FromMinutes(34)));
    A("motzeiShabbosChazonIsh", "מוצאי שבת", "חזו\"א", Plus(sunset, TimeSpan.FromMinutes(38)));
    A("motzeiYomTov", "מוצאי יום טוב", "רגיל", Plus(sunset, TimeSpan.FromMinutes(34)));
    A("motzeiYomTovChazonIsh", "מוצאי יום טוב", "חזו\"א", Plus(sunset, TimeSpan.FromMinutes(38)));
    A("achilasChametzMGADegrees", "סוף זמן אכילת חמץ", "מג\"א (מעלות)", c.GetSofZmanAchilasChametzMGA16Point1Degrees());
    A("achilasChametzGRA", "סוף זמן אכילת חמץ", "גר\"א", c.GetSofZmanAchilasChametzGRA());
    A("achilasChametzMGAShavos", "סוף זמן אכילת חמץ", "מג\"א (שוות)", c.GetSofZmanAchilasChametzMGA72Minutes());
    A("biurChametzMGADegrees", "סוף זמן ביעור חמץ", "מג\"א (מעלות)", c.GetSofZmanBiurChametzMGA16Point1Degrees());
    A("biurChametzGRA", "סוף זמן ביעור חמץ", "גר\"א", c.GetSofZmanBiurChametzGRA());
    A("biurChametzMGAShavos", "סוף זמן ביעור חמץ", "מג\"א (שוות)", c.GetSofZmanBiurChametzMGA72Minutes());
    A("fastEndTikLenient", "יציאת הצום", "לקולא — טיקוצ׳ינסקי", Plus(sunset, new TimeSpan(0, 13, 30)));
    A("fastEndItimLenient", "יציאת הצום", "לקולא — עיתים לבינה", Plus(sunset, TimeSpan.FromMinutes(18)));
    A("fastEndTikStringent", "יציאת הצום", "לחומרא — טיקוצ׳ינסקי", Plus(sunset, TimeSpan.FromMinutes(18)));
    A("fastEndItimStringent", "יציאת הצום", "לחומרא — עיתים לבינה", Plus(sunset, TimeSpan.FromMinutes(22)));
    A("_tzais85", "tzais()", "8.5° (omer counting time)", c.GetTzaisGeonim8Point5Degrees());
    return l;
}

static string Hms(DateTimeOffset? t) => t == null ? "null" : t.Value.ToString("HH:mm:ss");

static void Dump(City city, DateOnly date)
{
    foreach (var z in ComputeAll(city, date))
        Console.WriteLine($"{city.Name}|{date:yyyy-MM-dd}|{z.Id}|{Hms(z.Time)}");
}

// ---------------------------------------------------------------------------------------------
// Human readable report
// ---------------------------------------------------------------------------------------------
static void Report(City city, DateOnly date)
{
    Console.WriteLine();
    Console.WriteLine($"==== {city.Name} ({city.Country}) {city.Lat},{city.Lng} elev {city.Elevation}m {city.TimeZone}  {date:yyyy-MM-dd} ====");
    var hdf = new HebrewDateFormatter { IsHebrewFormat = true };
    var jc = new JewishCalendar(date) { InIsrael = city.InIsrael };
    Console.WriteLine($"Hebrew date : {hdf.Format(jc)}   (day of week {jc.DayOfWeek}, month# {jc.JewishMonth}, leap {jc.IsJewishLeapYear()})");
    var yt = hdf.FormatYomTov(jc);
    Console.WriteLine($"Yom tov     : '{yt}'  roshChodesh={jc.IsRoshChodesh()} taanis={jc.IsTaanis()}");
    var shabbat = jc.DayOfWeek == 7 ? jc : new JewishCalendar(date.AddDays((7 - jc.DayOfWeek))) { InIsrael = city.InIsrael };
    Console.WriteLine($"Parasha     : {hdf.FormatParshah(shabbat)}  (upcoming Shabbat {shabbat.LocalDate:yyyy-MM-dd}; special: '{hdf.FormatSpecialParshah(shabbat)}')");
    try
    {
        var bavli = YomiCalculator.GetDafYomiBavli(jc);
        Console.WriteLine($"Daf Bavli   : {bavli.GetMasechta()} {hdf.FormatHebrewNumber(bavli.DafNumber)}   [{hdf.FormatDafYomiBavli(bavli)}]");
    }
    catch (Exception e) { Console.WriteLine("Daf Bavli   : n/a " + e.Message); }
    try
    {
        var yer = YerushalmiYomiCalculator.GetDafYomiYerushalmi(jc);
        Console.WriteLine(yer == null
            ? "Daf Yerushalmi: none today (Yom Kippur / Tisha B'Av)"
            : $"Daf Yerushalmi: {yer.GetYerushalmiMasechta()} {hdf.FormatHebrewNumber(yer.DafNumber)}   [{hdf.FormatDafYomiYerushalmi(yer)}]");
    }
    catch (Exception e) { Console.WriteLine("Daf Yerushalmi: n/a " + e.Message); }
    Console.WriteLine($"Omer        : day {jc.GetDayOfOmer()}  '{hdf.FormatOmer(jc)}'");
    var molad = jc.GetMoladAsDateTimeOffset();
    Console.WriteLine($"Molad (this month, Jerusalem LMT-ish): {molad:yyyy-MM-dd HH:mm:ss zzz}  chalakim={jc.MoladChalakim}");
    Console.WriteLine("-- zmanim (local time) --");
    foreach (var z in ComputeAll(city, date))
        Console.WriteLine($"{z.Id,-26} {z.Title} {z.Subtitle,-30} {Hms(z.Time)}");
}

// ---------------------------------------------------------------------------------------------
// Hebrew calendar dump for cross-checking against kosher_dart
// ---------------------------------------------------------------------------------------------
static void HebrewDump(DateOnly start, int days, bool inIsrael)
{
    var hdf = new HebrewDateFormatter { IsHebrewFormat = true };
    for (var i = 0; i < days; i++)
    {
        var d = start.AddDays(i);
        var jc = new JewishCalendar(d) { InIsrael = inIsrael };
        string parsha = "";
        if (jc.DayOfWeek == 7) parsha = hdf.FormatParshah(jc);
        string daf;
        try { var f = YomiCalculator.GetDafYomiBavli(jc); daf = f.GetMasechta() + " " + f.DafNumber; } catch { daf = "n/a"; }
        string yer;
        try { var f = YerushalmiYomiCalculator.GetDafYomiYerushalmi(jc); yer = f == null ? "none" : f.GetYerushalmiMasechta() + " " + f.DafNumber; } catch { yer = "n/a"; }
        Console.WriteLine($"{d:yyyy-MM-dd}|{hdf.Format(jc)}|{hdf.FormatYomTov(jc)}|{parsha}|{daf}|{yer}|{jc.GetDayOfOmer()}|{(jc.IsRoshChodesh() ? 1 : 0)}|{hdf.FormatMonth(jc)}");
    }
}

// What the BCL System.Globalization.HebrewCalendar can / cannot do (see docs/research/calendar-and-zmanim.md section 3)
static void NetCalendarProbe()
{
    var hc = new System.Globalization.HebrewCalendar();
    var d = new DateTime(2026, 11, 13);
    Console.WriteLine($"HebrewCalendar range: {hc.MinSupportedDateTime:yyyy-MM-dd} .. {hc.MaxSupportedDateTime:yyyy-MM-dd}; eras {string.Join(',', hc.Eras)}");
    Console.WriteLine($"2026-11-13 -> year {hc.GetYear(d)} month {hc.GetMonth(d)} day {hc.GetDayOfMonth(d)}  leap={hc.IsLeapYear(hc.GetYear(d))}  monthsInYear={hc.GetMonthsInYear(hc.GetYear(d))}  (BCL months: Tishrei=1 ... Adar I=6/Adar II=7 in leap years; KosherJava: Nissan=1, Tishrei=7)");
    var he = new System.Globalization.CultureInfo("he-IL");
    he.DateTimeFormat.Calendar = hc;
    Console.WriteLine($"he-IL + HebrewCalendar: d MMMM yyyy => {d.ToString("d MMMM yyyy", he)} | dddd => {d.ToString("dddd", he)}");
    Console.WriteLine($"he-IL default calendar type on this machine: {new System.Globalization.CultureInfo("he-IL").DateTimeFormat.Calendar.GetType().Name}");
    foreach (var y in new[] { 5343, 5342, 5999, 6000 })
    {
        try { Console.WriteLine($"year {y}: leap={hc.IsLeapYear(y)} days in month 1 = {hc.GetDaysInMonth(y, 1)}"); }
        catch (Exception e) { Console.WriteLine($"year {y}: {e.GetType().Name}"); }
    }
}

static void MoladDump()
{
    var hdf = new HebrewDateFormatter { IsHebrewFormat = true };
    var jc = new JewishCalendar(new DateOnly(2026, 9, 1));
    for (var i = 0; i < 30; i++)
    {
        var m = jc.GetMolad();
        // Otzaria/kosher_dart "raw" molad = naive molad wall-clock fields (Jerusalem LMT, NOT shifted by -20:56) treated as UTC+2.
        var raw = new DateTime(m.LocalDate.Year, m.LocalDate.Month, m.LocalDate.Day, m.MoladHours, m.MoladMinutes, (int)(m.MoladChalakim * 10 / 3.0));
        Console.WriteLine($"{jc.JewishYear}|{jc.JewishMonth}|{hdf.FormatMonth(jc)}|{m.DayOfWeek}|{m.MoladHours}|{m.MoladMinutes}|{m.MoladChalakim}|{m.LocalDate.Year}-{m.LocalDate.Month}-{m.LocalDate.Day}|raw={raw:yyyy-MM-ddTHH:mm:ss}|kj3-correct={jc.GetMoladAsDateTimeOffset():yyyy-MM-ddTHH:mm:ss}Z|shabMev={jc.IsShabbosMevorchim()}");
        jc.PlusMonths(1);
    }
}

static DateOnly ParseDate(string s) =>
    DateOnly.ParseExact(s, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

record City(string Name, string Country, double Lat, double Lng, double Elevation, string TimeZone, bool InIsrael, int CandleLightingMinutes);

// ---------------------------------------------------------------------------------------------
// Minimal reader for Otzaria's Hive CE settings box (a log of frames). Frame layout (verified on a real file):
//   uint32 LE frameLength (whole frame) | key: 0x01, uint8 len, utf8 bytes | value: typeId byte + payload | uint32 CRC
//   value typeId 4 = string (uint32 LE len + utf8), 3 = bool (1 byte), 0 = null; later frames for a key override earlier ones.
// Otzaria itself reads the box from a *copy* because Hive locks it (core/info/settings_snapshot.dart:72-90).
// ---------------------------------------------------------------------------------------------
static class OtzariaSettings
{
    static readonly string[] Keys =
    {
        "key-selected-city", "key-calendar-day-transition", "key-calendar-type", "key-calendar-twelve-hour-clock", "key-calendar-enabled-zmanim",
    };

    public static Dictionary<string, string> Read()
    {
        var result = new Dictionary<string, string>();
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "otzaria", "app_preferences.hive");
        if (!File.Exists(path)) return result;
        var tmp = Path.Combine(Path.GetTempPath(), "zspike-" + Guid.NewGuid().ToString("N") + ".hive");
        try
        {
            File.Copy(path, tmp);
            var b = File.ReadAllBytes(tmp);
            foreach (var key in Keys)
            {
                var needle = new byte[] { 0x01, (byte)key.Length }.Concat(Encoding.UTF8.GetBytes(key)).ToArray();
                for (var i = 0; i + needle.Length + 1 < b.Length; i++)
                {
                    if (!b.AsSpan(i, needle.Length).SequenceEqual(needle)) continue;
                    var v = i + needle.Length;
                    if (b[v] == 0x04) // string
                    {
                        var len = BitConverter.ToInt32(b, v + 1);
                        result[key] = Encoding.UTF8.GetString(b, v + 5, len); // keep scanning: the last frame wins
                    }
                    else if (b[v] == 0x03) result[key] = (b[v + 1] != 0).ToString();
                }
            }
        }
        catch (Exception e) { result["error"] = e.Message; }
        finally { try { File.Delete(tmp); } catch { } }
        return result;
    }
}
