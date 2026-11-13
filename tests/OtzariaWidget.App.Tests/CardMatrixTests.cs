using OtzariaWidget.App.Provider.Cards;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.Tests;

public enum DataCase { Present, Empty, Missing }

public static class Ctx
{
    public static CardContext Make(string kind, string size, DataCase data, LinkOpenMode link = LinkOpenMode.OpenUrl,
        WidgetCustomState? state = null, FakeCalendarService? cal = null, int page = 0, string? notice = null,
        DateTimeOffset? now = null, OtzariaSnapshot? snapshot = null)
    {
        var snap = snapshot ?? data switch
        {
            DataCase.Present => TestData.Full(),
            DataCase.Empty => TestData.Empty(),
            _ => TestData.Missing(),
        };
        return new CardContext
        {
            Kind = kind,
            Size = CardSizes.Parse(size),
            Settings = new WidgetAppSettings { LinkMode = link },
            Calendar = cal ?? new FakeCalendarService(),
            Now = now ?? TestData.Now,
            Snapshot = snap,
            State = state ?? new WidgetCustomState(),
            Page = page,
            Notice = notice,
        };
    }

    public static IEnumerable<object[]> Matrix()
    {
        foreach (var kind in WidgetKinds.All)
            foreach (var size in CardExpander.Sizes)
                foreach (var theme in CardExpander.Themes)
                    foreach (var data in Enum.GetValues<DataCase>())
                        yield return [kind, size, theme, data];
    }
}

public class CardMatrixTests
{
    public static IEnumerable<object[]> Matrix() => Ctx.Matrix();

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Card_expands_and_validates(string kind, string size, string theme, DataCase data)
    {
        var ctx = Ctx.Make(kind, size, data);
        var result = CardBuilders.BuildSafe(ctx);
        foreach (var auth in new[] { true, false })
        {
            var ex = CardExpander.Expand(result, size, theme, auth);
            CardExpander.Validate(ex, customization: false, authenticated: auth, $"{kind}/{size}/{theme}/{data}/auth={auth}");
        }
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Card_validates_in_ProviderLaunch_mode(string kind, string size, string theme, DataCase data)
    {
        var ctx = Ctx.Make(kind, size, data, LinkOpenMode.ProviderLaunch);
        var result = CardBuilders.BuildSafe(ctx);
        var ex = CardExpander.Expand(result, size, theme, true);
        CardExpander.Validate(ex, false, true, $"{kind}/{size}/{theme}/{data}/launch");
        // unauthenticated host must still get only OpenUrl actions (validated inside)
        var ex2 = CardExpander.Expand(result, size, theme, false);
        CardExpander.Validate(ex2, false, false, $"{kind}/{size}/{theme}/{data}/launch/unauth");
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Calendar_failure_gives_valid_error_card(string kind, string size, string theme, DataCase data)
    {
        if (data != DataCase.Present) return;   // one pass is enough
        var ctx = Ctx.Make(kind, size, data, cal: new FakeCalendarService { Throw = true });
        string? seen = null;
        var result = CardBuilders.BuildSafe(ctx, e => seen = e.GetType().Name);
        var ex = CardExpander.Expand(result, size, theme, true);
        CardExpander.Validate(ex, false, true, $"{kind}/{size}/{theme}/calendar-throws");
        if (kind is WidgetKinds.Zmanim or WidgetKinds.HebrewCalendar)
        {
            Assert.Equal(CardTemplates.Error, result.TemplateFile);
            Assert.Equal(nameof(NotImplementedException), seen);
        }
    }

    [Theory]
    [InlineData("small")]
    [InlineData("medium")]
    [InlineData("large")]
    public void Snapshot_unavailable_shows_missing_state(string size)
    {
        foreach (var kind in new[] { WidgetKinds.RecentBooks, WidgetKinds.Bookmarks, WidgetKinds.ShamorZachor })
        {
            var ctx = new CardContext
            {
                Kind = kind, Size = CardSizes.Parse(size), Settings = new WidgetAppSettings(),
                Calendar = new FakeCalendarService(), Now = TestData.Now, Snapshot = null, SnapshotError = "boom",
            };
            var result = CardBuilders.BuildSafe(ctx);
            var ex = CardExpander.Expand(result, size, "dark");
            CardExpander.Validate(ex, false, true, $"{kind}/{size}/no-snapshot");
            Assert.Contains(CardData.MissingText, CardExpander.TextBlocks(ex.Root));
            Assert.Contains(CardExpander.AllActions(ex.Root), a => a.GetProperty("type").GetString() == "Action.Execute"
                                                                   && a.GetProperty("verb").GetString() == Verbs.Settings);
        }
    }

    [Fact]
    public void Missing_state_offers_open_settings_via_openurl_when_unauthenticated()
    {
        var ctx = Ctx.Make(WidgetKinds.RecentBooks, "medium", DataCase.Missing);
        var ex = CardExpander.Expand(CardBuilders.BuildSafe(ctx), "medium", "light", authenticated: false);
        Assert.Contains(CardExpander.Elements(ex.Root, "Action.OpenUrl"), a => a.GetProperty("url").GetString() == CardData.SettingsUrl
                                                                              && a.GetProperty("title").GetString() == "פתח הגדרות");
    }
}
