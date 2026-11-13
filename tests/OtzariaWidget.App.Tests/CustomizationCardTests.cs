using System.Text.Json;
using OtzariaWidget.App.Provider.Cards;

namespace OtzariaWidget.App.Tests;

public class CustomizationCardTests
{
    public static IEnumerable<object[]> KindsAndThemes()
    {
        foreach (var k in WidgetKinds.All)
            foreach (var size in CardExpander.Sizes)
                foreach (var theme in CardExpander.Themes)
                    yield return [k, size, theme];
    }

    private static string[] InputTypes(string kind) => kind switch
    {
        WidgetKinds.RecentBooks => ["Input.Number", "Input.Toggle"],
        WidgetKinds.Bookmarks => ["Input.ChoiceSet", "Input.Number"],
        WidgetKinds.ShamorZachor => ["Input.ChoiceSet", "Input.Number", "Input.Toggle"],
        WidgetKinds.Zmanim => ["Input.ChoiceSet"],
        WidgetKinds.HebrewCalendar => ["Input.Toggle"],
        WidgetKinds.Dashboard => ["Input.Number"],
        _ => [],
    };

    [Theory]
    [MemberData(nameof(KindsAndThemes))]
    public void Customization_card_expands_validates_and_has_save_cancel(string kind, string size, string theme)
    {
        var ctx = Ctx.Make(kind, size, DataCase.Present);
        var r = CardBuilders.BuildCustomizationSafe(ctx);
        Assert.Equal(CardTemplates.Customize, r.TemplateFile);
        var ex = CardExpander.Expand(r, size, theme);
        CardExpander.Validate(ex, customization: true, authenticated: true, $"{kind}/{size}/{theme}/customize");

        foreach (var t in InputTypes(kind)) Assert.NotEmpty(CardExpander.Elements(ex.Root, t));
        var verbs = CardExpander.Elements(ex.Root, "Action.Execute").Select(a => a.GetProperty("verb").GetString()).ToList();
        Assert.Contains("saveSettings", verbs);
        Assert.Contains("exitCustomization", verbs);
        Assert.Contains(CardExpander.TextBlocks(ex.Root), t => t.StartsWith("הגדרות · "));

        // every input has a unique id and a Hebrew label/title
        var inputs = CardExpander.Descendants(ex.Root)
            .Where(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("type", out var t) && t.GetString()!.StartsWith("Input.")).ToList();
        var ids = inputs.Select(i => i.GetProperty("id").GetString()).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(inputs, i => Assert.True(CardExpander.HasHebrew(
            i.TryGetProperty("label", out var l) ? l.GetString()! : i.GetProperty("title").GetString()!)));
    }

    [Fact]
    public void Customization_works_with_stubbed_calendar_as_error_card_not_crash()
    {
        var ctx = Ctx.Make(WidgetKinds.Zmanim, "large", DataCase.Present, cal: new FakeCalendarService { Throw = true });
        var r = CardBuilders.BuildCustomizationSafe(ctx);
        Assert.Equal(CardTemplates.Error, r.TemplateFile);
        CardExpander.Validate(CardExpander.Expand(r, "large", "dark"), false, true, "zmanim customize error");
    }

    /// <summary>
    /// Simulates what the board sends back: every Input.* value as a string keyed by id. Applying them to a fresh state must
    /// reproduce the state the card was pre-filled from (round trip).
    /// </summary>
    [Fact]
    public void Prefilled_values_round_trip_through_the_card_and_back()
    {
        var states = new Dictionary<string, WidgetCustomState>
        {
            [WidgetKinds.RecentBooks] = new() { Count = 4, ShowSearches = true, ShowLocation = false, IncludePdfs = false },
            [WidgetKinds.Bookmarks] = new() { Count = 2, Group = "g2" },
            [WidgetKinds.ShamorZachor] = new() { Count = 3, SzFilter = "completed", ShowAllColumns = true },
            [WidgetKinds.Zmanim] = new() { City = "בני ברק", ZmanimMode = "custom", ZmanimIds = ["sunrise", "sunset", "tzeis"] },
            [WidgetKinds.HebrewCalendar] = new() { ShowDaf = false, ShowYerushalmi = true, ShowOmer = false, ShowCandle = false },
            [WidgetKinds.Dashboard] = new() { Sections = ["progress", "date", "bookmarks"] },
        };
        foreach (var (kind, original) in states)
        {
            var ctx = Ctx.Make(kind, "large", DataCase.Present, state: original);
            var ex = CardExpander.Expand(CardBuilders.BuildCustomizationSafe(ctx), "large", "light");
            var posted = new Dictionary<string, string>();
            foreach (var i in CardExpander.Descendants(ex.Root).Where(e => e.ValueKind == JsonValueKind.Object &&
                         e.TryGetProperty("type", out var t) && t.GetString()!.StartsWith("Input.")))
            {
                var v = i.GetProperty("value");
                posted[i.GetProperty("id").GetString()!] = v.ValueKind == JsonValueKind.String ? v.GetString()! : v.ToString();
            }
            var restored = new WidgetCustomState();
            restored.ApplyInputs(kind, JsonSerializer.Serialize(posted));
            Assert.Equal(original.Serialize(), Normalize(kind, restored, original), ignoreCase: false);
        }
    }

    // Fields a kind does not expose are not part of its round trip; copy them from the original before comparing.
    private static string Normalize(string kind, WidgetCustomState restored, WidgetCustomState original)
    {
        var c = WidgetCustomState.Parse(original.Serialize());
        switch (kind)
        {
            case WidgetKinds.RecentBooks: c.Count = restored.Count; c.ShowSearches = restored.ShowSearches; c.ShowLocation = restored.ShowLocation; c.IncludePdfs = restored.IncludePdfs; break;
            case WidgetKinds.Bookmarks: c.Count = restored.Count; c.Group = restored.Group; break;
            case WidgetKinds.ShamorZachor: c.Count = restored.Count; c.SzFilter = restored.SzFilter; c.ShowAllColumns = restored.ShowAllColumns; break;
            case WidgetKinds.Zmanim: c.City = restored.City; c.ZmanimMode = restored.ZmanimMode; c.ZmanimIds = restored.ZmanimIds; break;
            case WidgetKinds.HebrewCalendar: c.ShowDaf = restored.ShowDaf; c.ShowYerushalmi = restored.ShowYerushalmi; c.ShowOmer = restored.ShowOmer; c.ShowCandle = restored.ShowCandle; break;
            case WidgetKinds.Dashboard: c.Sections = restored.Sections; break;
        }
        return c.Serialize();
    }

    [Fact]
    public void Dashboard_customization_positions_reflect_current_order()
    {
        var st = new WidgetCustomState { Sections = ["progress", "date"] };
        var r = CardBuilders.BuildCustomizationSafe(Ctx.Make(WidgetKinds.Dashboard, "large", DataCase.Present, state: st));
        using var doc = JsonDocument.Parse(r.DataJson);
        var pos = doc.RootElement.GetProperty("fields").EnumerateArray()
            .ToDictionary(f => f.GetProperty("id").GetString()!, f => f.GetProperty("value").GetInt32());
        Assert.Equal(1, pos["pos_progress"]);
        Assert.Equal(2, pos["pos_date"]);
        Assert.Equal(0, pos["pos_zman"]);
        Assert.Equal(0, pos["pos_bookmarks"]);
    }

    [Fact]
    public void Bookmark_group_choices_list_the_users_groups()
    {
        var r = CardBuilders.BuildCustomizationSafe(Ctx.Make(WidgetKinds.Bookmarks, "large", DataCase.Present));
        using var doc = JsonDocument.Parse(r.DataJson);
        var group = doc.RootElement.GetProperty("fields").EnumerateArray().Single(f => f.GetProperty("id").GetString() == "group");
        var titles = group.GetProperty("choices").EnumerateArray().Select(c => c.GetProperty("title").GetString()).ToList();
        Assert.Equal(["כל הסימניות", "ללא קבוצה", "תפילה", "שבת"], titles);
    }
}
