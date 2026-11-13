using OtzariaWidget.App.SettingsUi.Infrastructure;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.Tests.SettingsUi;

public class SettingsSessionTests
{
    [Fact]
    public void Saves_are_debounced_into_one_write()
    {
        var store = new FakeSettingsStore();
        using var session = UiTestHelpers.WithoutSyncContext(() => new SettingsSession(store));
        session.Settings.HideSearches = true; session.MarkChanged();
        Thread.Sleep(100);
        session.Settings.ShowYerushalmiDaf = true; session.MarkChanged();
        Thread.Sleep(250);
        Assert.Equal(0, store.Saves);   // still inside the quiet period
        Assert.True(UiTestHelpers.WaitFor(() => store.Saves == 1));
        Thread.Sleep(300);
        Assert.Equal(1, store.Saves);
        Assert.True(session.SaveVisible);
        Assert.Equal("נשמר", session.SaveText);
        Assert.False(session.SaveFailed);
        Assert.True(store.Current.HideSearches && store.Current.ShowYerushalmiDaf);
    }

    [Fact]
    public void Failed_save_is_reported_not_thrown_and_flush_saves_immediately()
    {
        var store = new FakeSettingsStore { Fail = true };
        using var session = UiTestHelpers.WithoutSyncContext(() => new SettingsSession(store));
        session.MarkChanged();
        session.Flush();
        Assert.True(session.SaveFailed);
        Assert.Contains("נכשלה", session.SaveText);

        store.Fail = false;
        session.MarkChanged();
        session.Flush();
        Assert.False(session.SaveFailed);
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public void Throwing_store_yields_defaults_and_a_load_error()
    {
        using var s = UiTestHelpers.WithoutSyncContext(() => new SettingsSession(new ThrowingSettingsStore()));
        Assert.NotNull(s.LoadError);
        Assert.NotNull(s.Settings);
        s.MarkChanged();
        s.Flush();
        Assert.True(s.SaveFailed);
    }

    [Fact]
    public void Theme_and_accent_from_an_older_settings_file_survive_a_save()
    {
        // the UI no longer edits them, but the contract properties must round-trip untouched
        var store = new FakeSettingsStore { Current = new WidgetAppSettings { Theme = ThemeMode.Dark, AccentColor = "#123456", LinkMode = LinkOpenMode.ProviderLaunch } };
        using var session = UiTestHelpers.WithoutSyncContext(() => new SettingsSession(store));
        session.Settings.HideSearches = true;
        session.MarkChanged();
        session.Flush();
        Assert.Equal(ThemeMode.Dark, store.Current.Theme);
        Assert.Equal("#123456", store.Current.AccentColor);
        Assert.Equal(LinkOpenMode.ProviderLaunch, store.Current.LinkMode);
    }
}

public class LaunchArgumentsTests
{
    [Theory]
    [InlineData("otzaria-widget://settings", null)]
    [InlineData("otzaria-widget://settings/", null)]
    [InlineData("otzaria-widget://settings/times", 2)]
    [InlineData("otzaria-widget://settings/times/", 2)]
    [InlineData("OTZARIA-WIDGET://SETTINGS/Display?x=1", 3)]
    [InlineData("otzaria-widget://settings/about", 4)]
    [InlineData("otzaria-widget://settings/nonsense", null)]
    [InlineData("otzaria-widget://other/times", null)]
    [InlineData("otzaria://open/library", null)]
    public void Protocol_uri_selects_a_page_or_nothing(string uri, int? expected)
        => Assert.Equal(expected, OtzariaWidget.App.SettingsUi.Infrastructure.LaunchArguments.PageFromProtocolUri(["--foo", uri]));

    [Fact]
    public void No_arguments_or_garbage_is_fine()
    {
        Assert.Null(OtzariaWidget.App.SettingsUi.Infrastructure.LaunchArguments.PageFromProtocolUri([]));
        Assert.Null(OtzariaWidget.App.SettingsUi.Infrastructure.LaunchArguments.PageFromProtocolUri(["", "--page", "2"]));
    }
}
