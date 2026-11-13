using OtzariaWidget.Core.Contracts;
using OtzariaWidget.Core.Launch;
using OtzariaWidget.Core.Services;
using OtzariaWidget.Core.Tests.UserState;
using OtzariaWidget.Core.UserState;
using static OtzariaWidget.Core.Tests.UserState.UserStateFixture;

namespace OtzariaWidget.Core.Tests.Services;

internal sealed class FakeSettingsStore : ISettingsStore
{
    public WidgetAppSettings Current { get; set; } = new();
    public int Loads;
    public bool ThrowOnLoad;
    public string SettingsFilePath => "fake";
    public event EventHandler? Changed;
    public WidgetAppSettings Load()
    {
        Interlocked.Increment(ref Loads);
        if (ThrowOnLoad) throw new IOException("boom");
        return Current;
    }
    public void Save(WidgetAppSettings s) => Current = s;
    public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    public bool HasSubscribers => Changed is not null;
}

internal sealed class FakeResolver(Func<WidgetAppSettings, OtzariaPaths> f) : IOtzariaPathResolver
{
    public int Calls;
    public OtzariaPaths Resolve(WidgetAppSettings settings) { Interlocked.Increment(ref Calls); return f(settings); }
}

internal sealed class FakePrefs(Func<string, OtzariaPreferences> f) : IPreferencesReader
{
    public OtzariaPreferences Read(string hiveFilePath) => f(hiveFilePath);
}

internal sealed class FakeProgress(Func<OtzariaPreferences, ILibraryCatalog?, IReadOnlyList<BookProgress>> f) : IProgressService
{
    public ILibraryCatalog? LastCatalog;
    public int Calls;
    public IReadOnlyList<BookProgress> Compute(OtzariaPreferences prefs, ILibraryCatalog? catalog)
    {
        Interlocked.Increment(ref Calls);
        LastCatalog = catalog;
        return f(prefs, catalog);
    }
}

internal sealed class FakeCatalog(string path) : ILibraryCatalog
{
    public string Path { get; } = path;
    public int? GetLearnableItemCount(int bookId) => 10;
    public (string Title, string? CategoryPath)? GetBookInfo(int bookId) => ("x", null);
    public int? FindBookIdByTitle(string title) => 1;
}

public class OtzariaDataServiceTests : IDisposable
{
    private readonly UserStateFixture _db = new();
    private readonly string _hiveDir = Path.Combine(Path.GetTempPath(), "otz-hive-" + Guid.NewGuid().ToString("N"));
    private readonly FakeSettingsStore _settings = new();
    private readonly List<IDisposable> _toDispose = [];

    public OtzariaDataServiceTests()
    {
        Directory.CreateDirectory(_hiveDir);
        File.WriteAllText(Path.Combine(_hiveDir, "app_preferences.hive"), "x");
        File.WriteAllText(Path.Combine(_hiveDir, "seforim.db"), "x");
    }

    public void Dispose()
    {
        foreach (var d in _toDispose) d.Dispose();
        _db.Dispose();
        try { Directory.Delete(_hiveDir, true); } catch { }
    }

    private OtzariaPaths FullPaths() => new()
    {
        DataRoot = _hiveDir,
        UserStateDb = _db.DbPath,
        PreferencesHive = Path.Combine(_hiveDir, "app_preferences.hive"),
        LibraryDb = Path.Combine(_hiveDir, "seforim.db"),
        OtzariaExe = @"C:\Otz\otzaria.exe",
    };

    private (OtzariaDataService Svc, FakeResolver Resolver, FakeProgress Progress) Create(
        Func<WidgetAppSettings, OtzariaPaths>? paths = null,
        Func<string, OtzariaPreferences>? prefs = null,
        Func<OtzariaPreferences, ILibraryCatalog?, IReadOnlyList<BookProgress>>? progress = null,
        IUserStateReader? reader = null,
        TimeSpan? debounce = null)
    {
        var resolver = new FakeResolver(paths ?? (_ => FullPaths()));
        var prog = new FakeProgress(progress ?? ((_, _) => []));
        var svc = new OtzariaDataService(_settings, resolver, reader ?? new UserStateReader(),
            new FakePrefs(prefs ?? (_ => new OtzariaPreferences { SelectedCity = "חיפה" })),
            prog, p => new FakeCatalog(p), new OtzariaLauncher(), debounce ?? TimeSpan.FromMilliseconds(150));
        _toDispose.Add(svc);
        return (svc, resolver, prog);
    }

    private void SeedHistory(int books = 3, int searches = 2)
    {
        var items = new List<object>();
        for (var i = 1; i <= books; i++) items.Add(Entry(Book(i, "ספר " + i), i));
        for (var i = 1; i <= searches; i++) items.Add(Search("חיפוש " + i));
        _db.SetList("history", "history", items);
    }

    // ----------------------------------------------------------------------------------------------------
    [Fact]
    public void Snapshot_aggregates_everything_with_ok_status()
    {
        SeedHistory();
        _db.SetList("bookmarks", "key-bookmarks", new object[] { Entry(Book(9, "סימניה"), 4, label: "תווית") });
        _db.SetList("workspaces", "key-workspaces", new object[] { new { id = "w", name = "ש", tabs = Array.Empty<object>() } });
        _db.SetSession(1, new object[] { new { type = "ToolTab", title = "כלי" } }, 0, "w");
        var (svc, _, prog) = Create(progress: (_, _) => [new BookProgress { BookId = 5, Title = "ספר" }]);

        var s = svc.GetSnapshot();

        Assert.Equal(3, s.RecentBooks.Count);
        Assert.Equal(2, s.RecentSearches.Count);
        Assert.Single(s.Bookmarks);
        Assert.True(s.Workspaces.Single().IsActive);
        Assert.Single(s.OpenTabs);
        Assert.Equal("חיפה", s.Preferences.SelectedCity);
        var bp = Assert.Single(s.Progress);
        Assert.Equal("otzaria://open/book/5", bp.DeepLink);           // filled in by the service
        Assert.IsType<FakeCatalog>(prog.LastCatalog);
        Assert.All(s.Status, st => Assert.Equal(DataHealth.Ok, st.Health));
        Assert.Contains(s.Status, st => st.Name == "user_state.db");
        Assert.Equal(@"C:\Otz\otzaria.exe", s.Paths.OtzariaExe);
        Assert.True((DateTimeOffset.Now - s.ReadAt).TotalSeconds < 30);
    }

    [Fact]
    public void Snapshot_is_cached_until_invalidated()
    {
        SeedHistory();
        var (svc, resolver, _) = Create();
        var a = svc.GetSnapshot();
        var b = svc.GetSnapshot();
        Assert.Same(a, b);
        Assert.Equal(1, resolver.Calls);
        svc.Invalidate();
        var c = svc.GetSnapshot();
        Assert.NotSame(a, c);
        Assert.Equal(2, resolver.Calls);
    }

    [Fact]
    public void Catalog_is_cached_per_library_file()
    {
        var created = 0;
        var resolver = new FakeResolver(_ => FullPaths());
        var svc = new OtzariaDataService(_settings, resolver, new UserStateReader(), new FakePrefs(_ => new OtzariaPreferences()),
            new FakeProgress((_, _) => []), p => { created++; return new FakeCatalog(p); }, new OtzariaLauncher());
        _toDispose.Add(svc);
        svc.GetSnapshot(); svc.Invalidate(); svc.GetSnapshot(); svc.Invalidate(); svc.GetSnapshot();
        Assert.Equal(1, created);
    }

    [Fact]
    public void Limits_30_books_and_20_searches()
    {
        SeedHistory(books: 45, searches: 35);
        var (svc, _, _) = Create();
        var s = svc.GetSnapshot();
        Assert.Equal(30, s.RecentBooks.Count);
        Assert.Equal(20, s.RecentSearches.Count);
    }

    [Fact]
    public void HideSearches_setting_removes_searches()
    {
        SeedHistory();
        _settings.Current = new WidgetAppSettings { HideSearches = true };
        var (svc, _, _) = Create();
        var s = svc.GetSnapshot();
        Assert.Empty(s.RecentSearches);
        Assert.Equal(3, s.RecentBooks.Count);
    }

    [Fact]
    public void Settings_are_passed_to_resolver()
    {
        WidgetAppSettings? seen = null;
        _settings.Current = new WidgetAppSettings { DataRootOverride = "zzz" };
        var (svc, _, _) = Create(paths: st => { seen = st; return FullPaths(); });
        svc.GetSnapshot();
        Assert.Equal("zzz", seen!.DataRootOverride);
    }

    [Fact]
    public void Missing_everything_gives_Hebrew_notfound_statuses_and_no_exception()
    {
        var (svc, _, _) = Create(paths: _ => new OtzariaPaths { Diagnostics = ["x"] });
        var s = svc.GetSnapshot();
        Assert.Empty(s.RecentBooks);
        var us = s.Status.Single(x => x.Name == "user_state.db");
        Assert.Equal(DataHealth.NotFound, us.Health);
        Assert.Equal("לא נמצא קובץ user_state.db", us.Message);
        Assert.Equal(DataHealth.NotFound, s.Status.Single(x => x.Name == "app_preferences.hive").Health);
        Assert.Equal(DataHealth.NotFound, s.Status.Single(x => x.Name == "seforim.db").Health);
        Assert.Equal(DataHealth.NotFound, s.Status.Single(x => x.Name == "otzaria.exe").Health);
        Assert.Equal(new[] { "x" }, s.Paths.Diagnostics);
    }

    [Fact]
    public void Unknown_schema_is_reported_and_other_sources_survive()
    {
        using var future = new UserStateFixture(userVersion: 9);
        var (svc, _, _) = Create(paths: _ => FullPaths() with { UserStateDb = future.DbPath });
        var s = svc.GetSnapshot();
        var us = s.Status.Single(x => x.Name == "user_state.db");
        Assert.Equal(DataHealth.UnknownSchema, us.Health);
        Assert.False(string.IsNullOrWhiteSpace(us.Message));
        Assert.Empty(s.RecentBooks);
        Assert.Equal("חיפה", s.Preferences.SelectedCity);
    }

    [Fact]
    public void Prefs_failure_is_isolated()
    {
        SeedHistory();
        var (svc, _, _) = Create(prefs: _ => throw new NotImplementedException());
        var s = svc.GetSnapshot();
        Assert.Equal(DataHealth.Unreadable, s.Status.Single(x => x.Name == "app_preferences.hive").Health);
        Assert.Equal("ירושלים", s.Preferences.SelectedCity);      // defaults
        Assert.Equal(3, s.RecentBooks.Count);
    }

    [Fact]
    public void Progress_failure_is_isolated()
    {
        SeedHistory();
        var (svc, _, _) = Create(progress: (_, _) => throw new NotImplementedException());
        var s = svc.GetSnapshot();
        Assert.Equal(DataHealth.Unreadable, s.Status.Single(x => x.Name == "progress").Health);
        Assert.Empty(s.Progress);
        Assert.Equal(3, s.RecentBooks.Count);
    }

    private sealed class ThrowingReader : IUserStateReader
    {
        public IReadOnlyList<ReadingEntry> ReadRecentBooks(string p, int max) => throw new IOException("locked");
        public IReadOnlyList<RecentSearch> ReadRecentSearches(string p, int max) => [new RecentSearch { Query = "q" }];
        public IReadOnlyList<ReadingEntry> ReadBookmarks(string p) => throw new IOException("locked");
        public IReadOnlyList<BookmarkGroup> ReadBookmarkGroups(string p) => [];
        public IReadOnlyList<WorkspaceInfo> ReadWorkspaces(string p) => [];
        public IReadOnlyList<OpenTabInfo> ReadOpenTabs(string p) => [];
    }

    [Fact]
    public void Partial_user_state_failure_keeps_what_was_read_and_flags_unreadable()
    {
        var (svc, _, _) = Create(reader: new ThrowingReader());
        var s = svc.GetSnapshot();
        Assert.Single(s.RecentSearches);
        Assert.Empty(s.RecentBooks);
        Assert.Equal(DataHealth.Unreadable, s.Status.Single(x => x.Name == "user_state.db").Health);
    }

    [Fact]
    public void Resolver_or_settings_exceptions_do_not_escape()
    {
        _settings.ThrowOnLoad = true;
        var (svc, _, _) = Create(paths: _ => throw new InvalidOperationException("x"));
        var s = svc.GetSnapshot();
        Assert.NotNull(s);
        Assert.Equal(DataHealth.NotFound, s.Status.Single(x => x.Name == "user_state.db").Health);
    }

    // ---- change notification --------------------------------------------------------------------------
    private static async Task<bool> WaitFor(Func<bool> cond, int ms = 6000)
    {
        var until = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < until)
        {
            if (cond()) return true;
            await Task.Delay(25);
        }
        return cond();
    }

    [Fact]
    public async Task Changed_raised_once_debounced_when_user_state_changes()
    {
        SeedHistory(books: 1, searches: 0);
        var (svc, _, _) = Create(debounce: TimeSpan.FromMilliseconds(700)); // generous: the suite runs many heavy tests in parallel
        Assert.Single(svc.GetSnapshot().RecentBooks);
        var count = 0;
        svc.Changed += (_, _) => Interlocked.Increment(ref count);

        for (var i = 0; i < 3; i++)
        {
            _db.SetList("history", "history", new object[] { Entry(Book(1, "ספר"), i), Entry(Book(2, "ספר 2"), i) });
            await Task.Yield();
        }
        Assert.True(await WaitFor(() => Volatile.Read(ref count) >= 1));
        await Task.Delay(1000);
        Assert.Equal(1, count);
        Assert.Equal(2, svc.GetSnapshot().RecentBooks.Count);       // cache was invalidated
    }

    [Fact]
    public async Task Reading_the_snapshot_does_not_trigger_change_events()
    {
        SeedHistory();
        var (svc, _, _) = Create(debounce: TimeSpan.FromMilliseconds(100));
        var count = 0;
        svc.Changed += (_, _) => Interlocked.Increment(ref count);
        for (var i = 0; i < 5; i++) { svc.GetSnapshot(); svc.Invalidate(); await Task.Delay(40); }
        await Task.Delay(700);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Changed_raised_when_settings_change()
    {
        SeedHistory();
        var (svc, resolver, _) = Create();
        svc.GetSnapshot();
        var count = 0;
        svc.Changed += (_, _) => Interlocked.Increment(ref count);
        _settings.Current = new WidgetAppSettings { HideSearches = true };
        _settings.Raise();
        Assert.True(await WaitFor(() => Volatile.Read(ref count) >= 1));
        Assert.Empty(svc.GetSnapshot().RecentSearches);             // re-read with the new settings
        Assert.Equal(2, resolver.Calls);
    }

    [Fact]
    public async Task Hive_change_raises_Changed()
    {
        SeedHistory();
        var (svc, _, _) = Create(debounce: TimeSpan.FromMilliseconds(100));
        svc.GetSnapshot();
        var count = 0;
        svc.Changed += (_, _) => Interlocked.Increment(ref count);
        File.AppendAllText(Path.Combine(_hiveDir, "app_preferences.hive"), "more");
        Assert.True(await WaitFor(() => Volatile.Read(ref count) >= 1));
    }

    [Fact]
    public async Task Watchers_are_rearmed_when_paths_change()
    {
        SeedHistory();
        using var other = new UserStateFixture();
        var useOther = false;
        var (svc, _, _) = Create(paths: _ => FullPaths() with { UserStateDb = useOther ? other.DbPath : _db.DbPath }, debounce: TimeSpan.FromMilliseconds(100));
        svc.GetSnapshot();
        useOther = true;
        svc.Invalidate();
        svc.GetSnapshot();                       // now watching `other`

        var count = 0;
        svc.Changed += (_, _) => Interlocked.Increment(ref count);
        other.SetList("history", "history", new object[] { Entry(Book(1, "ספר"), 1) });
        Assert.True(await WaitFor(() => Volatile.Read(ref count) >= 1));
        Assert.Single(svc.GetSnapshot().RecentBooks);
    }

    [Fact]
    public async Task Dispose_stops_events_and_unsubscribes_from_settings()
    {
        SeedHistory();
        var (svc, _, _) = Create(debounce: TimeSpan.FromMilliseconds(100));
        svc.GetSnapshot();
        var count = 0;
        svc.Changed += (_, _) => Interlocked.Increment(ref count);
        svc.Dispose();
        svc.Dispose();
        Assert.False(_settings.HasSubscribers);
        _db.SetList("history", "history", new object[] { Entry(Book(1, "ספר"), 1) });
        _settings.Raise();
        await Task.Delay(600);
        Assert.Equal(0, count);
    }

    [Fact]
    public void Concurrent_GetSnapshot_calls_are_safe()
    {
        SeedHistory();
        var (svc, resolver, _) = Create();
        var snaps = new OtzariaSnapshot[16];
        Parallel.For(0, 16, i =>
        {
            if (i % 5 == 0) svc.Invalidate();
            snaps[i] = svc.GetSnapshot();
        });
        Assert.All(snaps, s => Assert.Equal(3, s.RecentBooks.Count));
    }
}
