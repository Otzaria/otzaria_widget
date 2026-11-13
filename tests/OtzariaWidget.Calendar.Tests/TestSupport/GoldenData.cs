using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace OtzariaWidget.Calendar.Tests.TestSupport;

/// <summary>
/// Ground truth produced by Otzaria's REAL code (kosher_dart 2.0.20 + Otzaria's zmanim_helpers.dart / calendar_day_cell.dart /
/// calendar_times_panel.dart) via the Dart harness in spikes/ZmanimSpike/dart-groundtruth (golden_zmanim.dart, golden_hebrew.dart).
/// Stored gzip-compressed as embedded resources.
/// </summary>
public static class GoldenData
{
    public sealed record ZmanLine(string City, DateOnly Date, string Id, string? Clock, string? Label);

    /// <summary>One Dart line per day: see golden_hebrew.dart.</summary>
    public sealed record HebrewLine(
        DateOnly Date, string Chip, IReadOnlyList<string> Events, string UpcomingParasha, string Daf, string Omer,
        bool AssurBemelacha, int DayOfWeek, string JewishYmd, IReadOnlyList<string> RelevantIds);

    private static string[] ReadLines(string resource)
    {
        using var s = typeof(GoldenData).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("missing golden resource " + resource);
        using var gz = new GZipStream(s, CompressionMode.Decompress);
        using var r = new StreamReader(gz, Encoding.UTF8);
        return r.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static readonly Lazy<IReadOnlyList<ZmanLine>> Zmanim = new(() =>
    {
        var list = new List<ZmanLine>();
        foreach (var line in ReadLines("Golden.zmanim_full.txt.gz"))
        {
            var p = line.Split('|');
            var date = DateOnly.ParseExact(p[1], "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (p[3].StartsWith('=')) list.Add(new ZmanLine(p[0], date, p[2], null, p[3][1..]));
            else list.Add(new ZmanLine(p[0], date, p[2], p[3], null));
        }
        return list;
    });

    public static IReadOnlyList<ZmanLine> ZmanimLines => Zmanim.Value;

    public static IReadOnlyList<HebrewLine> Hebrew(bool israel)
    {
        var list = new List<HebrewLine>();
        foreach (var line in ReadLines(israel ? "Golden.hebrew_israel.txt.gz" : "Golden.hebrew_diaspora.txt.gz"))
        {
            var p = line.Split('|');
            list.Add(new HebrewLine(
                DateOnly.ParseExact(p[0], "yyyy-MM-dd", CultureInfo.InvariantCulture),
                p[1],
                p[2].Length == 0 ? [] : p[2].Split(';'),
                p[3], p[4], p[5], p[6] == "1", int.Parse(p[7], CultureInfo.InvariantCulture), p[8],
                p.Length > 9 && p[9].Length > 0 ? p[9].Split(',') : []));
        }
        return list;
    }

    /// <summary>Seconds since local midnight of a Dart "HH:mm:ss" value.</summary>
    public static int SecondsOfDay(string hms)
    {
        var p = hms.Split(':');
        return int.Parse(p[0], CultureInfo.InvariantCulture) * 3600 + int.Parse(p[1], CultureInfo.InvariantCulture) * 60 + int.Parse(p[2], CultureInfo.InvariantCulture);
    }
}
