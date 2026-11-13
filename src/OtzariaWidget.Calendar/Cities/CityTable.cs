using System.Text;
using System.Text.Json;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.Calendar.Cities;

/// <summary>
/// Otzaria's city table (lib/tools/calendar/models/calendar_location.dart), exported to
/// docs/research/otzaria-cities.json and embedded in this assembly. Order = Otzaria's source order
/// (grouped by country, Israel first). Names are the Hebrew keys Otzaria stores in <c>key-selected-city</c>.
/// </summary>
public static class CityTable
{
    public const string ResourceName = "OtzariaWidget.Calendar.otzaria-cities.json";
    public const string DefaultCityName = "ירושלים";

    private static readonly Lazy<(IReadOnlyList<CityInfo> Cities, Dictionary<string, CityInfo> ByName, Dictionary<string, CityInfo> ByNormalized)> Loaded = new(Load);

    public static IReadOnlyList<CityInfo> All => Loaded.Value.Cities;

    public static CityInfo Default => Find(DefaultCityName) ?? All[0];

    /// <summary>Exact Hebrew name first, then a normalized match (trim, geresh/apostrophe variants, whitespace, maqaf).</summary>
    public static CityInfo? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var l = Loaded.Value;
        if (l.ByName.TryGetValue(name, out var exact)) return exact;
        return l.ByNormalized.TryGetValue(Normalize(name), out var c) ? c : null;
    }

    /// <summary>Normalization used for lookups: trims, collapses whitespace, unifies the apostrophe family
    /// (' ’ ‘ ′ ׳ ` ´) to ASCII ', quote family (" ״ “ ”) to ASCII ", dashes (– — ‑ ־) to '-', and drops RTL/LTR marks.</summary>
    public static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        var lastSpace = true;
        foreach (var ch in s.Trim())
        {
            var c = ch switch
            {
                '\'' or '’' or '‘' or '′' or '׳' or '`' or '´' => '\'',
                '"' or '״' or '“' or '”' or '„' => '"',
                '–' or '—' or '‑' or '־' or '‐' => '-',
                '‎' or '‏' or '‪' or '‫' or '‬' or '⁦' or '⁧' or '⁨' or '⁩' or '﻿' => '\0',
                _ => ch,
            };
            if (c == '\0') continue;
            if (char.IsWhiteSpace(c))
            {
                if (!lastSpace) sb.Append(' ');
                lastSpace = true;
                continue;
            }
            sb.Append(c);
            lastSpace = false;
        }
        return sb.ToString().TrimEnd();
    }

    private static (IReadOnlyList<CityInfo>, Dictionary<string, CityInfo>, Dictionary<string, CityInfo>) Load()
    {
        using var stream = typeof(CityTable).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Embedded city table missing: " + ResourceName);
        using var doc = JsonDocument.Parse(stream);
        var list = new List<CityInfo>();
        var byName = new Dictionary<string, CityInfo>(StringComparer.Ordinal);
        var byNorm = new Dictionary<string, CityInfo>(StringComparer.Ordinal);
        foreach (var c in doc.RootElement.GetProperty("cities").EnumerateArray())
        {
            var city = new CityInfo
            {
                Name = c.GetProperty("name").GetString()!,
                Country = c.GetProperty("country").GetString()!,
                Latitude = c.GetProperty("lat").GetDouble(),
                Longitude = c.GetProperty("lng").GetDouble(),
                Elevation = c.GetProperty("elevation").GetDouble(),
                TimeZoneId = c.GetProperty("timezone").GetString()!,
                IsInIsrael = c.GetProperty("inIsrael").GetBoolean(),
                CandleLightingMinutes = c.GetProperty("candleLightingMinutes").GetInt32(),
            };
            list.Add(city);
            byName[city.Name] = city;
            byNorm[Normalize(city.Name)] = city;
        }
        return (list, byName, byNorm);
    }
}
