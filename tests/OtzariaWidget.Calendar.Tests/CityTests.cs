using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using OtzariaWidget.Calendar.Cities;

namespace OtzariaWidget.Calendar.Tests;

public class CityTests
{
    private static readonly CalendarService Svc = new();

    [Fact]
    public void Table_has_129_cities_55_in_israel_with_unique_names()
    {
        Assert.Equal(129, Svc.Cities.Count);
        Assert.Equal(55, Svc.Cities.Count(c => c.IsInIsrael));
        Assert.Equal(129, Svc.Cities.Select(c => c.Name).Distinct().Count());
        Assert.Equal(52, Svc.Cities.Select(c => c.TimeZoneId).Distinct().Count());
        Assert.All(Svc.Cities.Where(c => c.IsInIsrael), c =>
        {
            Assert.Equal("ארץ ישראל", c.Country);
            Assert.Equal("Asia/Jerusalem", c.TimeZoneId);
        });
        Assert.All(Svc.Cities.Where(c => !c.IsInIsrael), c => Assert.NotEqual("ארץ ישראל", c.Country));
    }

    [Fact]
    public void Order_is_Otzarias_source_order_israel_first()
    {
        Assert.Equal("אופקים", Svc.Cities[0].Name);
        Assert.True(Svc.Cities.Take(55).All(c => c.IsInIsrael));
        Assert.Equal("ירושלים", CityTable.DefaultCityName);
        Assert.NotNull(Svc.FindCity(CityTable.DefaultCityName));
    }

    [Fact]
    public void Every_city_has_a_valid_real_time_zone_consistent_with_its_longitude()
    {
        foreach (var c in Svc.Cities)
        {
            var tz = TimeZones.Resolve(c);
            Assert.False(tz.Id.StartsWith("fixed:", StringComparison.Ordinal), $"{c.Name}: IANA id {c.TimeZoneId} did not resolve");
            var offsetHours = tz.GetUtcOffset(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc)).TotalHours;
            Assert.True(Math.Abs(offsetHours - c.Longitude / 15.0) <= 3.5, $"{c.Name}: offset {offsetHours} vs longitude {c.Longitude}");
            Assert.InRange(c.Latitude, -60, 70);
            Assert.InRange(c.Longitude, -180, 180);
        }
    }

    [Fact]
    public void Unknown_time_zone_id_falls_back_to_a_longitude_offset_without_throwing()
    {
        var tz = TimeZones.Resolve("Mars/Olympus_Mons", 35.0);
        Assert.Equal(TimeSpan.FromHours(2), tz.BaseUtcOffset);
    }

    [Theory]
    [InlineData("ירושלים", 40)]
    [InlineData("מודיעין עילית", 30)]
    [InlineData("בני ברק", 30)]
    [InlineData("חיפה", 30)]
    [InlineData("בית שמש", 30)]
    [InlineData("תל אביב", 18)]
    [InlineData("אשדוד", 18)]
    [InlineData("ניו יורק", 18)]
    [InlineData("לונדון", 18)]
    [InlineData("אופקים", 20)]
    [InlineData("חצור הגלילית", 25)]
    [InlineData("ביתר עילית", 40)]
    public void Candle_lighting_minutes_per_city(string name, int minutes)
        => Assert.Equal(minutes, Svc.FindCity(name)!.CandleLightingMinutes);

    [Fact]
    public void Candle_lighting_fallbacks_by_country_then_30()
    {
        var us = Svc.Cities.Where(c => c.Country == "ארצות הברית").ToList();
        Assert.NotEmpty(us);
        Assert.All(us, c => Assert.Equal(18, c.CandleLightingMinutes));
        // a city with no explicit entry outside the USA gets the global default 30
        Assert.Equal(30, Svc.FindCity("פראג")?.CandleLightingMinutes ?? 30);
    }

    [Fact]
    public void FindCity_exact_trim_and_geresh_variants()
    {
        var la = Svc.Cities.Single(c => c.Name.StartsWith("לוס אנג", StringComparison.Ordinal));
        Assert.Same(la, Svc.FindCity(la.Name));
        Assert.Same(la, Svc.FindCity("  " + la.Name + "  "));
        Assert.Same(la, Svc.FindCity("לוס אנג’לס"));   // typographic apostrophe
        Assert.Same(la, Svc.FindCity("לוס אנג׳לס"));   // Hebrew geresh
        Assert.Same(la, Svc.FindCity("לוס  אנג'לס"));  // doubled space
        var kiryat = Svc.Cities.Single(c => c.Name.StartsWith("קרית יערים", StringComparison.Ordinal));
        Assert.Same(kiryat, Svc.FindCity("‏קרית יערים (טלז־סטון)‏")); // RLM marks + maqaf instead of hyphen
        Assert.Null(Svc.FindCity("אטלנטיס"));
        Assert.Null(Svc.FindCity(""));
        Assert.Null(Svc.FindCity("   "));
    }

    [Fact]
    public void Embedded_table_equals_docs_research_json()
    {
        var path = FindUpwards(Path.Combine("docs", "research", "otzaria-cities.json"));
        if (path == null) return; // docs not next to the test run: skip silently
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var json = doc.RootElement.GetProperty("cities").EnumerateArray().ToList();
        Assert.Equal(json.Count, Svc.Cities.Count);
        for (var i = 0; i < json.Count; i++)
        {
            var j = json[i];
            var c = Svc.Cities[i];
            Assert.Equal(j.GetProperty("name").GetString(), c.Name);
            Assert.Equal(j.GetProperty("country").GetString(), c.Country);
            Assert.Equal(j.GetProperty("lat").GetDouble(), c.Latitude);
            Assert.Equal(j.GetProperty("lng").GetDouble(), c.Longitude);
            Assert.Equal(j.GetProperty("elevation").GetDouble(), c.Elevation);
            Assert.Equal(j.GetProperty("timezone").GetString(), c.TimeZoneId);
            Assert.Equal(j.GetProperty("inIsrael").GetBoolean(), c.IsInIsrael);
            Assert.Equal(j.GetProperty("candleLightingMinutes").GetInt32(), c.CandleLightingMinutes);
        }
    }

    /// <summary>Validates the embedded table against Otzaria's own Dart source when it is available on this machine.</summary>
    [Fact]
    public void Embedded_table_matches_Otzaria_calendar_location_dart_when_available()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("OTZARIA_SOURCE_DIR") is { Length: > 0 } e ? Path.Combine(e, "lib", "tools", "calendar", "models", "calendar_location.dart") : "",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "otzaria_project", "lib", "tools", "calendar", "models", "calendar_location.dart"),
        };
        var path = candidates.FirstOrDefault(p => p.Length > 0 && File.Exists(p));
        if (path == null) return;

        static string Unescape(string s) => s.Replace("\\'", "'");
        var country = "";
        var city = "";
        var dict = new List<(string Country, string Name, double Lat, double Lng, double Elev, string Tz)>();
        double lat = 0, lng = 0, elev = 0;
        var candle = new Dictionary<string, int>();
        var inCandle = false;
        var countryCandle = new Dictionary<string, int>();
        var inCountryCandle = false;
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("const Map<String, int> candleLightingMinutesByCity", StringComparison.Ordinal)) { inCandle = true; continue; }
            if (line.StartsWith("const Map<String, int> candleLightingMinutesByCountry", StringComparison.Ordinal)) { inCandle = false; inCountryCandle = true; continue; }
            if (inCandle || inCountryCandle)
            {
                var m = Regex.Match(line, @"^\s+'((?:[^'\\]|\\.)*)':\s*(\d+),");
                if (m.Success) (inCandle ? candle : countryCandle)[Unescape(m.Groups[1].Value)] = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                if (line.StartsWith("};", StringComparison.Ordinal)) { inCandle = false; inCountryCandle = false; }
                continue;
            }
            Match h;
            if ((h = Regex.Match(line, @"^  '((?:[^'\\]|\\.)*)': \{$")).Success) { country = Unescape(h.Groups[1].Value); continue; }
            if ((h = Regex.Match(line, @"^    '((?:[^'\\]|\\.)*)': \{$")).Success) { city = Unescape(h.Groups[1].Value); continue; }
            if ((h = Regex.Match(line, @"^      'lat': (-?[\d.]+),")).Success) lat = double.Parse(h.Groups[1].Value, CultureInfo.InvariantCulture);
            else if ((h = Regex.Match(line, @"^      'lng': (-?[\d.]+),")).Success) lng = double.Parse(h.Groups[1].Value, CultureInfo.InvariantCulture);
            else if ((h = Regex.Match(line, @"^      'elevation': (-?[\d.]+),")).Success) elev = double.Parse(h.Groups[1].Value, CultureInfo.InvariantCulture);
            else if ((h = Regex.Match(line, @"^      'timezone': '([^']+)',")).Success) dict.Add((country, city, lat, lng, elev, h.Groups[1].Value));
        }

        Assert.Equal(dict.Count, Svc.Cities.Count);
        for (var i = 0; i < dict.Count; i++)
        {
            var d = dict[i];
            var c = Svc.Cities[i];
            Assert.Equal(d.Name, c.Name);
            Assert.Equal(d.Country, c.Country);
            Assert.Equal(d.Lat, c.Latitude);
            Assert.Equal(d.Lng, c.Longitude);
            Assert.Equal(d.Elev, c.Elevation);
            Assert.Equal(d.Tz, c.TimeZoneId);
            Assert.Equal(d.Country == "ארץ ישראל", c.IsInIsrael);
            var expectedMinutes = candle.TryGetValue(d.Name, out var m1) ? m1 : countryCandle.TryGetValue(d.Country, out var m2) ? m2 : 30;
            Assert.Equal(expectedMinutes, c.CandleLightingMinutes);
        }
    }

    private static string? FindUpwards(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var p = Path.Combine(dir.FullName, relative);
            if (File.Exists(p)) return p;
            dir = dir.Parent;
        }
        return null;
    }
}
