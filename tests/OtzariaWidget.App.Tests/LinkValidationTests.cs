using System.Text.Json;
using OtzariaWidget.App.Provider.Cards;
using OtzariaWidget.Core.Contracts;
using OtzariaWidget.Core.Launch;

namespace OtzariaWidget.App.Tests;

/// <summary>Release QA: every link a card can ever contain is statically valid (never opened here: otzaria:// would launch the real app).</summary>
public class LinkValidationTests
{
    /// <summary>Every string property named "url" anywhere in the card data.</summary>
    internal static IEnumerable<(string Path, string Url)> Urls(JsonElement e, string path = "$")
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in e.EnumerateObject())
            {
                if (p.Name == "url" && p.Value.ValueKind == JsonValueKind.String) yield return (path + ".url", p.Value.GetString()!);
                foreach (var x in Urls(p.Value, path + "." + p.Name)) yield return x;
            }
        }
        else if (e.ValueKind == JsonValueKind.Array)
        {
            int i = 0;
            foreach (var v in e.EnumerateArray()) foreach (var x in Urls(v, path + "[" + i++ + "]")) yield return x;
        }
    }

    internal static void AssertLinksAreValid(string dataJson, bool providerLaunch, string context)
    {
        using var doc = JsonDocument.Parse(dataJson);
        foreach (var (path, url) in Urls(doc.RootElement))
        {
            Assert.True(url.StartsWith("otzaria://open/", StringComparison.Ordinal) || url == CardData.SettingsUrl, $"{context} {path}: unexpected scheme");
            if (url == CardData.SettingsUrl) continue;
            Assert.True(OtzariaLauncher.IsSafeOtzariaUri(url), $"{context} {path}: not a safe otzaria uri");
            // links the provider launches itself pass the stricter allow-list too (ids are digits, only known screens)
            Assert.True(WidgetController.IsSafeOtzariaUrl(url), $"{context} {path}: rejected by the provider allow-list ({url.Split('?')[0]})");
            Assert.True(url.Length < 2000, $"{context} {path}: very long link");
        }
        // an "action" (ProviderLaunch) exists only when the mode asks for it, and carries the same url as its sibling urlAction
        foreach (var o in Objects(doc.RootElement))
        {
            bool hasAction = o.TryGetProperty("action", out var a) && a.ValueKind == JsonValueKind.Object;
            bool hasUrlAction = o.TryGetProperty("urlAction", out var ua) && ua.ValueKind == JsonValueKind.Object;
            if (hasAction)
            {
                Assert.True(providerLaunch, $"{context}: Execute action in OpenUrl mode");
                Assert.True(hasUrlAction, $"{context}: action without urlAction fallback");
                Assert.Equal(Verbs.Open, o.GetProperty("action").GetProperty("verb").GetString());
                Assert.Equal(o.GetProperty("urlAction").GetProperty("url").GetString(), o.GetProperty("action").GetProperty("data").GetProperty("url").GetString());
            }
        }
    }

    private static IEnumerable<JsonElement> Objects(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            yield return e;
            foreach (var p in e.EnumerateObject()) foreach (var x in Objects(p.Value)) yield return x;
        }
        else if (e.ValueKind == JsonValueKind.Array)
            foreach (var v in e.EnumerateArray()) foreach (var x in Objects(v)) yield return x;
    }

    [Fact]
    public void All_links_in_all_cards_are_valid_for_both_link_modes()
    {
        foreach (var kind in WidgetKinds.All)
            foreach (var size in CardExpander.Sizes)
                foreach (var mode in Enum.GetValues<LinkOpenMode>())
                    foreach (var page in new[] { 0, 1 })
                    {
                        var r = CardBuilders.BuildSafe(Ctx.Make(kind, size, DataCase.Present, mode, page: page));
                        AssertLinksAreValid(r.DataJson, mode == LinkOpenMode.ProviderLaunch, $"{kind}/{size}/{mode}/p{page}");
                    }
    }

    [Fact]
    public void Search_links_with_hostile_queries_stay_valid_or_are_rejected_not_launched()
    {
        // queries are user data: quotes, backslashes, spaces, newlines, huge text, unicode controls
        foreach (var q in new[] { "a\"b", "c:"+(char)92+"x", "x y\nz", new string('ש', 5000), "\u202E\u2066x", "q=1&mode=exact", "%00", "'; drop" })
        {
            var link = new OtzariaLauncher().BuildSearchLink(q, "exact");
            Assert.StartsWith("otzaria://open/search", link);
            Assert.True(OtzariaLauncher.IsSafeOtzariaUri(link), "unsafe link for a hostile query");
            Assert.True(WidgetController.IsSafeOtzariaUrl(link) || link.Length > 2000, "rejected by the provider allow-list");
        }
        // the card's own fallback link for a search without DeepLink
        var snap = TestData.Full() with { RecentSearches = [new RecentSearch { Query = "a"+(char)34+"b c:"+(char)92+"x"+(char)10+"z", Mode = null }] };
        var ctx = Ctx.Make(WidgetKinds.RecentBooks, "large", DataCase.Present, LinkOpenMode.ProviderLaunch, snapshot: snap, state: new WidgetCustomState { ShowSearches = true });
        AssertLinksAreValid(CardBuilders.BuildSafe(ctx).DataJson, true, "hostile search");
    }
}
