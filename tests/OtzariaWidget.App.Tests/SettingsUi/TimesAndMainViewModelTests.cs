using OtzariaWidget.App.SettingsUi.Infrastructure;
using OtzariaWidget.App.SettingsUi.ViewModels;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.Tests.SettingsUi;

public class TimesViewModelTests
{
    private static (TimesViewModel T, SettingsSession S, FakeSettingsStore Store) Make(UiFakeCalendar? cal = null, FakeDataService? data = null)
    {
        var store = new FakeSettingsStore();
        var d = data ?? new FakeDataService();
        return UiTestHelpers.WithoutSyncContext(() =>
        {
            var session = new SettingsSession(store);
            var hub = new SnapshotHub(d);
            hub.RefreshAsync().Wait();
            var t = new TimesViewModel(session, UiTestHelpers.Services(store, d, cal), hub);
            UiTestHelpers.WaitFor(() => t.CityCount == 2);
            return (t, session, store);
        });
    }

    [Fact]
    public void City_follows_otzaria_by_default_and_custom_choice_is_stored()
    {
        var (t, s, _) = Make();
        using var _ = s;
        Assert.True(t.FollowCity);
        Assert.Equal(2, t.CityCount);
        Assert.Contains("ירושלים", t.OtzariaCityText);

        t.FollowCity = false;
        Assert.Equal("ירושלים", s.Settings.CityOverride);   // custom starts from Otzaria's city
        t.SelectedCity = t.CitiesView!.Cast<CityInfo>().First(c => c.Name == "ניו יורק");
        Assert.Equal("ניו יורק", s.Settings.CityOverride);

        t.CityFilter = "ניו";
        Assert.Single(t.CitiesView!.Cast<object>());
        t.CityFilter = "zzz";
        Assert.True(t.NoCityMatches);
        t.CityFilter = "";

        t.FollowCity = true;
        Assert.Null(s.Settings.CityOverride);
    }

    [Fact]
    public void Zmanim_selection_keeps_registry_order_and_supports_bulk_commands()
    {
        var (t, s, _) = Make();
        using var _ = s;
        t.FollowZmanim = false;
        Assert.Equal(["sunset"], s.Settings.ZmanimOverride!);
        t.Zmanim[0].IsChecked = true;
        Assert.Equal(["alos", "sunset"], s.Settings.ZmanimOverride!);
        t.SelectAllZmanimCommand.Execute(null);
        Assert.Equal(3, s.Settings.ZmanimOverride!.Count);
        t.ClearZmanimCommand.Execute(null);
        Assert.Empty(s.Settings.ZmanimOverride!);
        Assert.Contains("לא נבחרו", t.ZmanimSummary);
        t.DefaultZmanimCommand.Execute(null);
        Assert.Equal(["sunset"], s.Settings.ZmanimOverride!);
        t.FollowZmanim = true;
        Assert.Null(s.Settings.ZmanimOverride);
    }

    [Fact]
    public void Time_format_day_transition_and_yerushalmi_edit_the_settings()
    {
        var (t, s, _) = Make();
        using var _ = s;
        t.TimeFormatIndex = 2; Assert.True(s.Settings.TwelveHourClockOverride);
        t.TimeFormatIndex = 1; Assert.False(s.Settings.TwelveHourClockOverride);
        t.TimeFormatIndex = 0; Assert.Null(s.Settings.TwelveHourClockOverride);
        t.DayTransitionIndex = 3; Assert.Equal("rabbeinuTam", s.Settings.DayTransitionOverride);
        t.DayTransitionIndex = 0; Assert.Null(s.Settings.DayTransitionOverride);
        t.ShowYerushalmiDaf = true; Assert.True(s.Settings.ShowYerushalmiDaf);
    }

    [Fact]
    public async Task Preview_lists_rows_and_marks_missing_times()
    {
        var (t, s, _) = Make();
        using var _ = s;
        t.FollowZmanim = false;
        t.SelectAllZmanimCommand.Execute(null);   // includes "tzais" which the fake calendar reports as "does not occur"
        await t.RefreshPreviewAsync();
        Assert.False(t.HasPreviewError);
        Assert.Equal("ירושלים", t.PreviewCity);
        Assert.Equal(3, t.Preview.Count);
        Assert.Contains(t.Preview, r => r.Missing && r.TimeText == "—");
    }

    [Fact]
    public void Wording_uses_kmo_beotzaria_not_zehe()
    {
        var (t, s, _) = Make();
        using var _ = s;
        Assert.Equal("כמו באוצריא", t.TimeFormatLabels[0]);
        Assert.Equal("כמו באוצריא", t.DayTransitionOptions[0]);
        Assert.DoesNotContain(t.TimeFormatLabels.Concat(t.DayTransitionOptions), l => l.Contains("זהה"));
    }

    [Fact]
    public void Twelve_hour_preview_uses_otzarias_AM_PM_marker_and_24h_has_none()
    {
        var local = new DateTimeOffset(2026, 11, 13, 13, 45, 0, TimeSpan.FromHours(2));
        Assert.Equal("1:45‎ PM", TimesViewModel.FormatTime(local, true));
        Assert.Equal("12:05‎ AM", TimesViewModel.FormatTime(new DateTimeOffset(2026, 11, 13, 0, 5, 0, TimeSpan.Zero), true));
        Assert.Equal("11:59‎ AM", TimesViewModel.FormatTime(new DateTimeOffset(2026, 11, 13, 11, 59, 0, TimeSpan.Zero), true));
        Assert.Equal("13:45", TimesViewModel.FormatTime(local, false));
    }

    [Fact]
    public void Calendar_service_failure_is_shown_not_thrown()
    {
        var store = new FakeSettingsStore();
        var d = new FakeDataService();
        using var session = UiTestHelpers.WithoutSyncContext(() => new SettingsSession(store));
        var hub = UiTestHelpers.WithoutSyncContext(() => new SnapshotHub(d));
        var t = UiTestHelpers.WithoutSyncContext(() => new TimesViewModel(session, UiTestHelpers.Services(store, d, new UiFakeCalendar { Throw = true }), hub));
        Assert.True(t.HasCalendarError);
        Assert.True(UiTestHelpers.WaitFor(() => t.HasPreviewError));
    }
}

public class MainViewModelTests
{
    [Fact]
    public void Everything_failing_still_constructs_and_reports_errors()
    {
        var svc = new SettingsServices
        {
            Store = new ThrowingSettingsStore(), Data = new FakeDataService { Throw = true }, Calendar = new UiFakeCalendar { Throw = true },
            Resolver = new FakeUiResolver(), Launcher = new FakeUiLauncher { Result = false }, Shell = new FakeUiShell { UriThrows = true },
        };
        var main = UiTestHelpers.WithoutSyncContext(() => new MainViewModel(svc));
        using var _ = main.Session;
        Assert.NotNull(main.Session.LoadError);
        Assert.True(UiTestHelpers.WaitFor(() => main.Times.HasPreviewError && main.Status.HasError));
        Assert.True(main.Times.HasCalendarError);

        main.Status.OpenOtzariaCommand.Execute(null);
        Assert.Equal(StateKind.Warning, main.Status.Action!.State);
        main.About.TestLinkCommand.Execute(null);
        Assert.Equal(StateKind.Error, main.About.Message!.State);
        Assert.Equal(5, main.Pages.Count);
    }

    [Fact]
    public void Launch_and_link_test_succeed_with_working_services()
    {
        var store = new FakeSettingsStore();
        var launcher = new FakeUiLauncher();
        var shell = new FakeUiShell();
        var main = UiTestHelpers.WithoutSyncContext(() => new MainViewModel(UiTestHelpers.Services(store, l: launcher, sh: shell)));
        using var _ = main.Session;
        Assert.True(UiTestHelpers.WaitFor(() => main.Status.Rows.Count == 6));

        main.Status.OpenOtzariaCommand.Execute(null);
        Assert.Equal("(null)", launcher.Last);
        Assert.Equal(StateKind.Ok, main.Status.Action!.State);

        main.About.TestLinkCommand.Execute(null);
        Assert.Equal("otzaria://open/history", shell.OpenedUri);
        Assert.Single(main.Status.Sources);
        Assert.Single(main.Status.Diagnostics);
    }

    [Fact]
    public void Log_folder_is_the_virtual_data_folder_when_unpackaged_and_can_be_opened()
    {
        var store = new FakeSettingsStore();
        var shell = new FakeUiShell();
        var about = new AboutViewModel(UiTestHelpers.Services(store, sh: shell));
        about.OpenLogFolderCommand.Execute(null);
        Assert.Equal(AppEnvironment.RealDataFolder, shell.OpenedFolder);
        Assert.Equal("fake-settings.json", about.SettingsFilePath);
    }
}
