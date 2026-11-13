using OtzariaWidget.Core.Contracts;
using OtzariaWidget.Core.Services;
using OtzariaWidget.Core.Settings;
using OtzariaWidget.Core.Tests.Hive;
using OtzariaWidget.Core.Tests.UserState;
using W = OtzariaWidget.Core.Tests.Hive.HiveTestWriter;

namespace OtzariaWidget.Core.Tests.Services;

/// <summary>
/// Release QA: the four path overrides and HideSearches written to settings.json are honoured by the REAL resolver + readers + data service
/// (no fakes except the files themselves), and a later change of the file reaches <see cref="IOtzariaDataService.Changed"/>.
/// </summary>
public class SettingsOverridesEndToEndTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "otz-ovr-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string Dir(string name) { var d = Path.Combine(_root, name); Directory.CreateDirectory(d); return d; }

    [Fact]
    public async Task All_path_overrides_and_HideSearches_are_honoured_end_to_end()
    {
        // data root with preferences only; user_state.db lives in ANOTHER folder (DatabasesDirOverride); seforim.db and otzaria.exe elsewhere again
        var dataRoot = Dir("data");
        new HiveTestWriter()
            .Put("key-selected-city", W.Str("בני ברק"))
            .Put("key-calendar-twelve-hour-clock", W.Bool(true))
            .Save(Path.Combine(dataRoot, "app_preferences.hive"));

        var dbDir = Dir("dbs");
        using (var _ = new UserStateFixtureIn(dbDir, db =>
        {
            db.SetList("history", "history", new object[]
            {
                UserStateFixture.Entry(UserStateFixture.Book(11, "ספר ראשון"), 3),
                UserStateFixture.Entry(UserStateFixture.Book(12, "ספר שני"), 5),
                UserStateFixture.Search("מילה לחיפוש"),
            });
            db.SetList("bookmarks", "key-bookmarks", new object[] { UserStateFixture.Entry(UserStateFixture.Book(13, "ספר סימניה"), 1, label: "סימניה") });
        })) { }

        var libDir = Dir("lib");
        var libDb = Path.Combine(libDir, "seforim.db");
        File.WriteAllBytes(libDb, []);
        var exeDir = Dir("exe");
        var exe = Path.Combine(exeDir, "otzaria.exe");
        File.WriteAllBytes(exe, []);

        var settingsDir = Dir("settings");
        var store = new JsonSettingsStore(settingsDir, TimeSpan.FromMilliseconds(50));
        store.Save(new WidgetAppSettings
        {
            DataRootOverride = dataRoot, DatabasesDirOverride = dbDir, LibraryDbOverride = libDir /* folder form */, OtzariaExeOverride = exe,
        });

        using var svc = new OtzariaDataService(store);
        var snap = svc.GetSnapshot();

        Assert.Equal(dataRoot, snap.Paths.DataRoot);
        Assert.Equal(Path.Combine(dbDir, "user_state.db"), snap.Paths.UserStateDb);
        Assert.Equal(Path.Combine(dataRoot, "app_preferences.hive"), snap.Paths.PreferencesHive);
        Assert.Equal(libDb, snap.Paths.LibraryDb);
        Assert.Equal(exe, snap.Paths.OtzariaExe);
        Assert.Equal("בני ברק", snap.Preferences.SelectedCity);
        Assert.True(snap.Preferences.TwelveHourClock);
        Assert.Equal(2, snap.RecentBooks.Count);
        Assert.Single(snap.Bookmarks);
        Assert.Single(snap.RecentSearches);
        Assert.Equal(DataHealth.Ok, snap.Status.Single(s => s.Name == SourceNames.UserState).Health);
        Assert.Equal(DataHealth.Ok, snap.Status.Single(s => s.Name == SourceNames.OtzariaExe).Health);

        // HideSearches written by the settings UI later: the data service notices (Changed) and the next snapshot has no searches
        var changed = new TaskCompletionSource();
        svc.Changed += (_, _) => changed.TrySetResult();
        var s2 = store.Load();
        s2.HideSearches = true;
        store.Save(s2);
        Assert.True(await Task.WhenAny(changed.Task, Task.Delay(10_000)) == changed.Task, "Changed was not raised for the settings change");
        var snap2 = svc.GetSnapshot();
        Assert.Empty(snap2.RecentSearches);
        Assert.Equal(2, snap2.RecentBooks.Count);
        Assert.Equal(exe, snap2.Paths.OtzariaExe);   // the other overrides survive the reload
    }

    [Fact]
    public void Overrides_that_point_nowhere_degrade_to_NotFound_statuses_without_exceptions()
    {
        var store = new JsonSettingsStore(Dir("settings2"), TimeSpan.FromMilliseconds(50));
        store.Save(new WidgetAppSettings
        {
            DataRootOverride = Path.Combine(_root, "nope"), DatabasesDirOverride = Path.Combine(_root, "nope2"),
            LibraryDbOverride = Path.Combine(_root, "nope3", "seforim.db"), OtzariaExeOverride = Path.Combine(_root, "nope4", "otzaria.exe"),
        });
        using var svc = new OtzariaDataService(store);
        var snap = svc.GetSnapshot();
        Assert.NotEqual(Path.Combine(_root, "nope3", "seforim.db"), snap.Paths.LibraryDb);
        Assert.NotEqual(Path.Combine(_root, "nope4", "otzaria.exe"), snap.Paths.OtzariaExe);
        Assert.All(snap.Status.Where(s => s.Health != DataHealth.Ok), s => Assert.False(string.IsNullOrWhiteSpace(s.Message)));
    }

    /// <summary>A user_state.db with Otzaria's schema created inside an existing folder.</summary>
    private sealed class UserStateFixtureIn : IDisposable
    {
        private readonly UserStateFixture _f = new();
        public UserStateFixtureIn(string targetDir, Action<UserStateFixture> fill)
        {
            fill(_f);
            File.Copy(_f.DbPath, Path.Combine(targetDir, "user_state.db"), overwrite: true);
        }
        public void Dispose() => _f.Dispose();
    }
}
