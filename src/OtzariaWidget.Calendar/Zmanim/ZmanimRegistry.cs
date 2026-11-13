using System.Text.Json;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.Calendar.Zmanim;

/// <summary>Everything Otzaria's registry knows about one zman (ZmanDefinition in zman_definition.dart).</summary>
public sealed record ZmanRegistryEntry(
    string Id,
    /// <summary>General name, e.g. "עלות השחר".</summary>
    string Title,
    /// <summary>Method detail, e.g. "72 דק׳ (מעלות)"; empty when none.</summary>
    string Subtitle,
    /// <summary>Otzaria's grouping category, e.g. "עלות השחר", "שבת וחג", "תעניות".</summary>
    string Category,
    /// <summary>First line of Otzaria's long Hebrew explanation (the "info" dialog).</summary>
    string Description,
    bool DefaultEnabled,
    /// <summary>Kidush levana entries: shown as a Hebrew night label instead of a clock time.</summary>
    bool ShowHebrewDate,
    /// <summary>Entries with the same PairId are shown as one card in Otzaria.</summary>
    string? PairId,
    string PairLabel)
{
    /// <summary>Otzaria's <c>fullName</c>: "title — subtitle" (just the title when there is no subtitle).</summary>
    public string FullName => Subtitle.Length == 0 ? Title : $"{Title} — {Subtitle}";
}

/// <summary>The 98 Otzaria zmanim in Otzaria's display order (kZmanimRegistry), loaded from an embedded JSON
/// generated from zmanim_helpers.dart.</summary>
public static class ZmanimRegistry
{
    public const string ResourceName = "OtzariaWidget.Calendar.zmanim-registry.json";

    private static readonly Lazy<IReadOnlyList<ZmanRegistryEntry>> Entries = new(Load);
    private static readonly Lazy<Dictionary<string, int>> IndexById = new(() =>
        Entries.Value.Select((e, i) => (e.Id, i)).ToDictionary(x => x.Id, x => x.i, StringComparer.Ordinal));

    public static IReadOnlyList<ZmanRegistryEntry> All => Entries.Value;

    public static IReadOnlyList<ZmanDefinition> Definitions { get; } =
        new Lazy<IReadOnlyList<ZmanDefinition>>(() => Entries.Value.Select(e => new ZmanDefinition(e.Id, e.FullName, e.Description)).ToList()).Value;

    public static IReadOnlyList<string> DefaultEnabled { get; } =
        new Lazy<IReadOnlyList<string>>(() => Entries.Value.Where(e => e.DefaultEnabled).Select(e => e.Id).ToList()).Value;

    public static ZmanRegistryEntry? Find(string id) => IndexById.Value.TryGetValue(id, out var i) ? Entries.Value[i] : null;

    public static int IndexOf(string id) => IndexById.Value.TryGetValue(id, out var i) ? i : -1;

    private static IReadOnlyList<ZmanRegistryEntry> Load()
    {
        using var stream = typeof(ZmanimRegistry).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Embedded zmanim registry missing: " + ResourceName);
        using var doc = JsonDocument.Parse(stream);
        var list = new List<ZmanRegistryEntry>();
        foreach (var z in doc.RootElement.GetProperty("zmanim").EnumerateArray())
        {
            list.Add(new ZmanRegistryEntry(
                z.GetProperty("id").GetString()!,
                z.GetProperty("title").GetString()!,
                z.GetProperty("subtitle").GetString() ?? "",
                z.GetProperty("category").GetString()!,
                z.GetProperty("description").GetString() ?? "",
                z.GetProperty("defaultEnabled").GetBoolean(),
                z.GetProperty("showHebrewDate").GetBoolean(),
                z.TryGetProperty("pairId", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null,
                z.GetProperty("pairLabel").GetString() ?? ""));
        }
        return list;
    }
}
