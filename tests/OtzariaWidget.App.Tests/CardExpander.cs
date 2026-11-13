using System.Text;
using System.Text.Json;
using AdaptiveCards.Templating;
using OtzariaWidget.App.Provider.Cards;

namespace OtzariaWidget.App.Tests;

/// <summary>Expands template + data with a fake $host (what the Widgets Board does) and validates the resulting card.</summary>
public static class CardExpander
{
    static CardExpander()
    {
        // Real templates are copied next to the test assembly (see csproj Content link).
        CardTemplates.Directory = Path.Combine(AppContext.BaseDirectory, "Templates");
    }

    public static readonly string[] Sizes = ["small", "medium", "large"];
    public static readonly string[] Themes = ["light", "dark"];

    public static readonly HashSet<string> AllowedElements = new(StringComparer.Ordinal)
    {
        "TextBlock", "RichTextBlock", "Image", "Container", "ColumnSet", "Column", "FactSet", "ImageSet", "ActionSet", "Table",
    };

    public sealed record Expanded(string Json, JsonElement Root, IReadOnlyList<string> Warnings, int DataBytes);

    public static Expanded Expand(CardResult result, string size, string theme, bool authenticated = true)
    {
        var template = new AdaptiveCardTemplate(CardTemplates.Load(result.TemplateFile));
        var host = $"{{\"widgetSize\":\"{size}\",\"hostTheme\":\"{theme}\",\"isUserContextAuthenticated\":{(authenticated ? "true" : "false")},\"isSettingsPayload\":false}}";
        var json = template.Expand(new EvaluationContext(result.DataJson, host));
        var warnings = template.GetLastTemplateExpansionWarnings().Cast<object>().Select(o => o.ToString() ?? "").ToList();
        using var doc = JsonDocument.Parse(json);
        return new Expanded(json, doc.RootElement.Clone(), warnings, Encoding.UTF8.GetByteCount(result.DataJson));
    }

    public static IEnumerable<JsonElement> Descendants(JsonElement e)
    {
        yield return e;
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                    foreach (var d in Descendants(p.Value)) yield return d;
                break;
            case JsonValueKind.Array:
                foreach (var i in e.EnumerateArray())
                    foreach (var d in Descendants(i)) yield return d;
                break;
        }
    }

    public static IEnumerable<JsonElement> Elements(JsonElement root, string type) =>
        Descendants(root).Where(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() == type);

    public static IEnumerable<string> TextBlocks(JsonElement root) =>
        Elements(root, "TextBlock").Select(t => t.GetProperty("text").GetString() ?? "");

    public static bool HasHebrew(string s) => s.Any(c => c >= '֐' && c <= '׿');

    /// <summary>Full structural validation. Throws Xunit assertion exceptions.</summary>
    public static void Validate(Expanded ex, bool customization, bool authenticated, string context)
    {
        var json = ex.Json;
        var root = ex.Root;

        Assert.True(ex.Warnings.Count == 0, $"[{context}] template warnings: {string.Join(" | ", ex.Warnings)}");
        Assert.DoesNotContain("${", json);
        Assert.DoesNotContain("$when", json);
        Assert.DoesNotContain("$data", json);
        Assert.True(Encoding.UTF8.GetByteCount(json) < 16 * 1024, $"[{context}] expanded card {Encoding.UTF8.GetByteCount(json)} bytes");
        Assert.True(ex.DataBytes < 16 * 1024, $"[{context}] data {ex.DataBytes} bytes");

        Assert.Equal("AdaptiveCard", root.GetProperty("type").GetString());
        var version = Version.Parse(root.GetProperty("version").GetString()!);
        Assert.True(version <= new Version(1, 6), $"[{context}] version {version}");
        Assert.True(root.TryGetProperty("rtl", out var rtl) && rtl.GetBoolean(), $"[{context}] root rtl");

        // allowed element types
        foreach (var e in Descendants(root))
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("type", out var t) || t.ValueKind != JsonValueKind.String) continue;
            var type = t.GetString()!;
            if (type == "AdaptiveCard" || type.StartsWith("Action.", StringComparison.Ordinal)) continue;
            if (type.StartsWith("Input.", StringComparison.Ordinal))
            {
                Assert.True(customization, $"[{context}] Input element {type} outside customization card");
                continue;
            }
            Assert.True(AllowedElements.Contains(type), $"[{context}] forbidden element type '{type}'");
        }

        // Actions: only OpenUrl (otzaria://, https://, otzaria-widget://settings) and Execute with known verbs.
        foreach (var a in Elements(root, "Action.OpenUrl"))
        {
            var url = a.GetProperty("url").GetString() ?? "";
            Assert.True(url.StartsWith("otzaria://", StringComparison.Ordinal) || url.StartsWith("https://", StringComparison.Ordinal)
                        || url == CardData.SettingsUrl, $"[{context}] bad OpenUrl '{url}'");
        }
        foreach (var a in Elements(root, "Action.Execute"))
        {
            var verb = a.GetProperty("verb").GetString() ?? "";
            Assert.True(Verbs.Known.Contains(verb), $"[{context}] unknown verb '{verb}'");
            if (verb == Verbs.Open)
            {
                var url = a.GetProperty("data").GetProperty("url").GetString() ?? "";
                Assert.StartsWith("otzaria://", url);
            }
            if (!authenticated) Assert.Fail($"[{context}] Action.Execute '{verb}' present although user context is not authenticated");
        }
        foreach (var a in Descendants(root).Where(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty("type", out var t) &&
                                                       t.ValueKind == JsonValueKind.String && t.GetString()!.StartsWith("Action.", StringComparison.Ordinal)))
        {
            var type = a.GetProperty("type").GetString();
            Assert.True(type is "Action.OpenUrl" or "Action.Execute", $"[{context}] action {type}");
        }

        // structure sanity (Widgets Board renderer is strict about empty containers)
        foreach (var cs in Elements(root, "ColumnSet"))
            Assert.True(cs.TryGetProperty("columns", out var cols) && cols.GetArrayLength() > 0, $"[{context}] empty ColumnSet");
        foreach (var col in Elements(root, "Column"))
        {
            if (!col.TryGetProperty("width", out var w)) continue;
            if (w.ValueKind == JsonValueKind.Number) Assert.True(w.GetInt32() > 0, $"[{context}] zero-weight column");
            else Assert.True(w.GetString() is "auto" or "stretch" || System.Text.RegularExpressions.Regex.IsMatch(w.GetString()!, @"^\d+px$"), $"[{context}] column width {w}");
        }
        foreach (var tb in TextBlocks(root))
            Assert.False(string.IsNullOrWhiteSpace(tb), $"[{context}] empty TextBlock");

        // Hebrew UI text must be present
        Assert.True(TextBlocks(root).Any(HasHebrew), $"[{context}] no Hebrew text found");
    }

    /// <summary>Every Action in the card (including selectAction).</summary>
    public static IEnumerable<JsonElement> AllActions(JsonElement root) =>
        Descendants(root).Where(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty("type", out var t) &&
                                     t.ValueKind == JsonValueKind.String && t.GetString()!.StartsWith("Action.", StringComparison.Ordinal));
}
