// Agent D - customization-card field builders (rendered by Templates\customize.json).
using System.Text.Json.Nodes;

namespace OtzariaWidget.App.Provider.Cards;

internal static class Fields
{
    public static JsonObject Toggle(string id, string label, bool value) => new()
    {
        ["kind"] = "toggle", ["id"] = id, ["label"] = label, ["value"] = value ? "true" : "false",
    };

    public static JsonObject Number(string id, string label, int value, int min, int max) => new()
    {
        ["kind"] = "number", ["id"] = id, ["label"] = label, ["value"] = value, ["min"] = min, ["max"] = max,
    };

    public static JsonObject Choice(string id, string label, string value, IEnumerable<(string Title, string Value)> choices) => new()
    {
        ["kind"] = "choice", ["id"] = id, ["label"] = label, ["value"] = value, ["choices"] = Choices(choices),
    };

    public static JsonObject Multi(string id, string label, IEnumerable<string> selected, IEnumerable<(string Title, string Value)> choices) => new()
    {
        ["kind"] = "multi", ["id"] = id, ["label"] = label, ["value"] = string.Join(",", selected), ["choices"] = Choices(choices),
    };

    private static JsonArray Choices(IEnumerable<(string Title, string Value)> choices)
    {
        var a = new JsonArray();
        foreach (var (t, v) in choices) a.Add(new JsonObject { ["title"] = t, ["value"] = v });
        return a;
    }

    public static CardResult Card(string kind, JsonArray fields, string? subtitle = null, bool settingsButton = false)
    {
        var root = new JsonObject
        {
            ["title"] = "הגדרות · " + WidgetKinds.DisplayName(kind),
            ["subtitle"] = subtitle ?? "",
            ["fields"] = fields,
            ["settingsButton"] = settingsButton,   // adds an "open settings" button (full checklists live in the settings app)
        };
        return new CardResult(CardTemplates.Customize, CardTemplates.ToJson(root));
    }
}
