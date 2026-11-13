using OtzariaWidget.Core.Contracts;
using OtzariaWidget.Core.Launch;
using OtzariaWidget.Core.Services;
using OtzariaWidget.Core.Tests.UserState;
using OtzariaWidget.Core.UserState;
using Xunit.Abstractions;
using static OtzariaWidget.Core.Tests.UserState.UserStateFixture;

namespace OtzariaWidget.Core.Tests.Services;

/// <summary>QA regression tests for the data service (watchers on a checkpointed WAL database, error recovery, cache age).</summary>
public class OtzariaDataServiceQaTests(ITestOutputHelper output) : IDisposable
{
    private readonly List<IDisposable> _toDispose = [];
    private readonly FakeSettingsStore _settings = new();

    public void Dispose() { foreach (var d in _toDispose) d.Dispose(); }

    private OtzariaDataService Create(Func<WidgetAppSettings, OtzariaPaths> paths, TimeSpan? debounce = null)
    {
        var svc = new OtzariaDataService(_settings, new FakeResolver(paths), new UserStateReader(),
            new FakePrefs(_ => new OtzariaPreferences()), new FakeProgress((_, _) => []), p => new FakeCatalog(p),
            new OtzariaLauncher(), debounce ?? TimeSpan.FromMilliseconds(100));
        _toDispose.Add(svc);
        return svc;
    }

    [Fact]
    public async Task Reading_a_checkpointed_wal_database_does_not_trigger_change_events()
    {
        // Production shape: Otzaria is closed, user_state.db is in WAL mode and has no -wal/-shm. Our own read-only
        // connection creates them; that must not make the service re-raise Changed forever.
        using var db = new UserStateFixture(wal: true);
        db.SetList("history", "history", new object[] { Entry(Book(1, "ספר"), 1) });
        foreach (var s in new[] { "-wal", "-shm" })
            Assert.False(File.Exists(db.DbPath + s), "fixture should be checkpointed: " + s);

        var svc = Create(_ => new OtzariaPaths { UserStateDb = db.DbPath });
        var count = 0;
        svc.Changed += (_, _) => Interlocked.Increment(ref count);
        for (var i = 0; i < 4; i++)
        {
            Assert.Single(svc.GetSnapshot().RecentBooks);
            svc.Invalidate();
            await Task.Delay(120);
        }
        await Task.Delay(800);
        output.WriteLine("changed events: " + count + " wal=" + File.Exists(db.DbPath + "-wal") + " shm=" + File.Exists(db.DbPath + "-shm"));
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Missing_databases_folder_is_picked_up_later_without_manual_invalidate()
    {
        // %APPDATA%\otzaria does not exist yet (Otzaria never ran) => no watchers can be armed. Once Otzaria creates the
        // data, a later GetSnapshot must see it (cache must not be sticky forever).
        var dir = Path.Combine(Path.GetTempPath(), "otz-late-" + Guid.NewGuid().ToString("N"));
        var dbPath = Path.Combine(dir, "databases", "user_state.db");
        var exists = false;
        var svc = Create(_ => exists ? new OtzariaPaths { UserStateDb = dbPath } : new OtzariaPaths());
        try
        {
            Assert.Empty(svc.GetSnapshot().RecentBooks);
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            using (var f = new UserStateFixtureAt(dbPath))
                f.SetList("history", "history", new object[] { Entry(Book(1, "ספר"), 1) });
            exists = true;
            // incomplete snapshots (missing user_state.db) are re-read after a short time
            var ok = false;
            for (var i = 0; i < 100 && !ok; i++)
            {
                await Task.Delay(100);
                ok = svc.GetSnapshot().RecentBooks.Count == 1;
            }
            Assert.True(ok);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task Watcher_error_invalidates_and_raises_Changed()
    {
        using var db = new UserStateFixture();
        db.SetList("history", "history", new object[] { Entry(Book(1, "ספר"), 1) });
        var svc = Create(_ => new OtzariaPaths { UserStateDb = db.DbPath });
        svc.GetSnapshot();
        var count = 0;
        svc.Changed += (_, _) => Interlocked.Increment(ref count);
        svc.SimulateWatcherErrorForTests();
        for (var i = 0; i < 40 && Volatile.Read(ref count) == 0; i++) await Task.Delay(50);
        Assert.True(count >= 1);
    }
}

/// <summary>Fixture variant that creates the schema at an explicit path.</summary>
internal sealed class UserStateFixtureAt : IDisposable
{
    private readonly string _path;
    public UserStateFixtureAt(string path)
    {
        _path = path;
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE lists (box TEXT NOT NULL, key TEXT NOT NULL, payload_json TEXT NOT NULL, updated_at INTEGER NOT NULL, PRIMARY KEY (box, key));
            CREATE TABLE window_sessions (slot INTEGER PRIMARY KEY, tabs_json TEXT NOT NULL, current_index INTEGER NOT NULL, active_workspace_id TEXT, bounds_json TEXT, updated_at INTEGER NOT NULL);
            PRAGMA user_version = 1;
            """;
        cmd.ExecuteNonQuery();
    }
    private Microsoft.Data.Sqlite.SqliteConnection Open()
    {
        var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_path};Pooling=False");
        c.Open();
        return c;
    }
    public void SetList(string box, string key, object payload)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO lists(box,key,payload_json,updated_at) VALUES($b,$k,$p,1)";
        cmd.Parameters.AddWithValue("$b", box);
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$p", System.Text.Json.JsonSerializer.Serialize(payload));
        cmd.ExecuteNonQuery();
    }
    public void Dispose() { }
}
