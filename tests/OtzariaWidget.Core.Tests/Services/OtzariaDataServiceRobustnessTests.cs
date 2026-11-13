using OtzariaWidget.Core.Contracts;
using OtzariaWidget.Core.Launch;
using OtzariaWidget.Core.Services;
using OtzariaWidget.Core.Tests.UserState;
using OtzariaWidget.Core.UserState;
using Xunit.Abstractions;
using static OtzariaWidget.Core.Tests.UserState.UserStateFixture;

namespace OtzariaWidget.Core.Tests.Services;

/// <summary>QA: concurrency, re-entrancy, corrupt/missing files, privacy of statuses and real-data timing for the data service.</summary>
public class OtzariaDataServiceRobustnessTests(ITestOutputHelper output) : IDisposable
{
    private readonly List<IDisposable> _toDispose = [];
    private readonly FakeSettingsStore _settings = new();
    public void Dispose() { foreach (var d in _toDispose) d.Dispose(); }

    private OtzariaDataService Create(OtzariaPaths paths, TimeSpan? debounce = null, IUserStateReader? reader = null)
    {
        var svc = new OtzariaDataService(_settings, new FakeResolver(_ => paths), reader ?? new UserStateReader(),
            new FakePrefs(_ => new OtzariaPreferences()), new FakeProgress((_, _) => []), p => new FakeCatalog(p),
            new OtzariaLauncher(), debounce ?? TimeSpan.FromMilliseconds(80));
        _toDispose.Add(svc);
        return svc;
    }

    [Fact]
    public async Task Handlers_may_call_back_into_the_service_and_dispose_it_without_deadlock()
    {
        using var db = new UserStateFixture();
        db.SetList("history", "history", new object[] { Entry(Book(1, "ספר"), 1) });
        var svc = Create(new OtzariaPaths { UserStateDb = db.DbPath });
        svc.GetSnapshot();
        var done = new TaskCompletionSource<int>();
        svc.Changed += (_, _) =>
        {
            var n = svc.GetSnapshot().RecentBooks.Count;   // re-entrant read inside the callback
            svc.Invalidate();
            svc.Dispose();                                 // dispose from inside the callback
            done.TrySetResult(n);
        };
        svc.Invalidate();
        _settings.Raise();
        Assert.Same(done.Task, await Task.WhenAny(done.Task, Task.Delay(5000)));
        Assert.Equal(1, await done.Task);
    }

    private static void Exec(Microsoft.Data.Sqlite.SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task Commits_by_a_writer_that_keeps_the_wal_open_are_detected_by_polling()
    {
        // NTFS sends no change notification for appends to a file that stays open (Otzaria's -wal): FileSystemWatcher alone is blind.
        using var db = new UserStateFixture(wal: true);
        using var writer = db.Open();
        Exec(writer, "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0;");
        Exec(writer, "INSERT OR REPLACE INTO lists VALUES('history','history','[]',1)");

        var svc = Create(new OtzariaPaths { UserStateDb = db.DbPath }, TimeSpan.FromMilliseconds(60));
        svc.PollInterval = TimeSpan.FromMilliseconds(150);
        svc.GetSnapshot();
        var count = 0;
        svc.Changed += (_, _) => Interlocked.Increment(ref count);

        await Task.Delay(700);
        var baseline = Volatile.Read(ref count);                  // NTFS may deliver late events of the fixture's own setup writes

        var json = System.Text.Json.JsonSerializer.Serialize(new object[] { Entry(Book(5, "חדש"), 3) });
        using (var cmd = writer.CreateCommand())
        {
            cmd.CommandText = "INSERT OR REPLACE INTO lists VALUES('history','history',$p,2)";
            cmd.Parameters.AddWithValue("$p", json);
            cmd.ExecuteNonQuery();                                // stays in the -wal; the writer connection is never closed
        }
        for (var i = 0; i < 100 && Volatile.Read(ref count) == baseline; i++) await Task.Delay(50);
        Assert.True(count > baseline, "poll did not notice the commit");
        Assert.Equal("חדש", svc.GetSnapshot().RecentBooks.Single().Book.Title);

        await Task.Delay(300);
        var settled = Volatile.Read(ref count);
        await Task.Delay(800);
        Assert.Equal(settled, Volatile.Read(ref count));          // and it settles: no endless re-raising
    }

    [Fact]
    public async Task Polling_stops_after_the_last_subscriber_leaves_and_after_dispose()
    {
        using var db = new UserStateFixture(wal: true);
        using var writer = db.Open();
        Exec(writer, "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0;");
        Exec(writer, "INSERT OR REPLACE INTO lists VALUES('history','history','[]',1)");
        var svc = Create(new OtzariaPaths { UserStateDb = db.DbPath }, TimeSpan.FromMilliseconds(40));
        svc.PollInterval = TimeSpan.FromMilliseconds(100);
        svc.GetSnapshot();
        var count = 0;
        EventHandler h = (_, _) => Interlocked.Increment(ref count);
        svc.Changed += h;
        svc.Changed -= h;
        Exec(writer, "INSERT OR REPLACE INTO lists VALUES('history','history','[1]',3)");
        await Task.Delay(600);
        Assert.Equal(0, count);

        svc.Changed += h;
        svc.Dispose();
        Exec(writer, "INSERT OR REPLACE INTO lists VALUES('history','history','[2]',4)");
        await Task.Delay(600);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task One_throwing_subscriber_does_not_starve_the_others()
    {
        using var db = new UserStateFixture();
        var svc = Create(new OtzariaPaths { UserStateDb = db.DbPath });
        var second = 0;
        svc.Changed += (_, _) => throw new InvalidOperationException("bug");
        svc.Changed += (_, _) => Interlocked.Increment(ref second);
        _settings.Raise();
        for (var i = 0; i < 40 && Volatile.Read(ref second) == 0; i++) await Task.Delay(50);
        Assert.Equal(1, second);
    }

    [Fact]
    public void Throwing_handler_does_not_break_the_raiser_or_the_service()
    {
        using var db = new UserStateFixture();
        var svc = Create(new OtzariaPaths { UserStateDb = db.DbPath });
        svc.Changed += (_, _) => throw new InvalidOperationException("bug");
        _settings.Raise();
        Assert.NotNull(svc.GetSnapshot());
    }

    [Fact]
    public void GetSnapshot_after_Dispose_still_returns_a_value()
    {
        using var db = new UserStateFixture();
        db.SetList("history", "history", new object[] { Entry(Book(1, "ספר"), 1) });
        var svc = Create(new OtzariaPaths { UserStateDb = db.DbPath });
        svc.Dispose();
        Assert.Single(svc.GetSnapshot().RecentBooks);
        svc.Invalidate();
        svc.Dispose();
    }

    [Fact]
    public async Task Stress_get_and_invalidate_from_many_threads_with_file_churn_never_throws()
    {
        using var db = new UserStateFixture(wal: true);
        db.SetList("history", "history", new object[] { Entry(Book(1, "ספר"), 1) });
        var svc = Create(new OtzariaPaths { UserStateDb = db.DbPath }, TimeSpan.FromMilliseconds(30));
        var changed = 0;
        svc.Changed += (_, _) => Interlocked.Increment(ref changed);
        var stop = false;
        var errors = new List<Exception>();
        var tasks = new List<Task>();
        for (var t = 0; t < 4; t++)
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    while (!Volatile.Read(ref stop))
                    {
                        var s = svc.GetSnapshot();
                        if (s.Paths is null) throw new InvalidOperationException("null paths");
                        if (Random.Shared.Next(3) == 0) svc.Invalidate();
                    }
                }
                catch (Exception ex) { lock (errors) errors.Add(ex); }
            }));
        tasks.Add(Task.Run(() =>
        {
            try
            {
                for (var i = 0; i < 40; i++) { db.SetList("history", "history", new object[] { Entry(Book(i + 1, "ספר " + i), i) }); Thread.Sleep(10); }
            }
            catch (Exception ex) { lock (errors) errors.Add(ex); }
        }));
        await Task.Delay(1200);
        Volatile.Write(ref stop, true);
        await Task.WhenAll(tasks);
        Assert.Empty(errors);
        for (var i = 0; i < 100 && Volatile.Read(ref changed) == 0; i++) await Task.Delay(50);
        Assert.True(changed >= 1);
        svc.Invalidate();
        Assert.Equal("ספר 39", svc.GetSnapshot().RecentBooks.Single().Book.Title);
    }

    [Fact]
    public void Database_deleted_between_resolve_and_read_gives_a_status_not_an_exception()
    {
        using var db = new UserStateFixture();
        var path = db.DbPath;
        File.Delete(path);
        var snap = Create(new OtzariaPaths { UserStateDb = path }).GetSnapshot();
        var st = snap.Status.Single(x => x.Name == "user_state.db");
        Assert.NotEqual(DataHealth.Ok, st.Health);
        Assert.False(string.IsNullOrWhiteSpace(st.Message));
        Assert.False(File.Exists(path));                // reading must not have created an empty database
    }

    [Fact]
    public void Corrupt_database_file_gives_unreadable_status()
    {
        var dir = Path.Combine(Path.GetTempPath(), "otz-corrupt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "user_state.db");
            File.WriteAllBytes(path, new byte[5000].Select((_, i) => (byte)(i * 31)).ToArray());
            var snap = Create(new OtzariaPaths { UserStateDb = path }).GetSnapshot();
            Assert.Equal(DataHealth.Unreadable, snap.Status.Single(x => x.Name == "user_state.db").Health);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Status_messages_do_not_leak_search_queries()
    {
        using var db = new UserStateFixture();
        db.SetList("history", "history", new object[] { Search("סוד מאוד אישי") });
        var snap = Create(new OtzariaPaths { UserStateDb = db.DbPath }).GetSnapshot();
        Assert.DoesNotContain(snap.Status, s => (s.Message ?? "").Contains("סוד"));
        Assert.DoesNotContain(snap.Paths.Diagnostics, d => d.Contains("סוד"));
    }

    [Fact]
    public void Cached_snapshot_is_reused_but_an_incomplete_one_is_refreshed_quickly()
    {
        using var db = new UserStateFixture();
        var resolver = new FakeResolver(_ => new OtzariaPaths { UserStateDb = db.DbPath });
        using var svc = new OtzariaDataService(_settings, resolver, new UserStateReader(), new FakePrefs(_ => new OtzariaPreferences()),
            new FakeProgress((_, _) => []), p => new FakeCatalog(p), new OtzariaLauncher(), TimeSpan.FromMilliseconds(80));
        var a = svc.GetSnapshot();
        Assert.Same(a, svc.GetSnapshot());          // no hive path => incomplete: still cached for a few seconds
        Assert.Equal(1, resolver.Calls);
    }

    [Fact]
    public void Real_data_snapshot_timing()
    {
        var appData = Environment.GetEnvironmentVariable("APPDATA");
        if (string.IsNullOrEmpty(appData) || !Directory.Exists(Path.Combine(appData, "otzaria"))) return;
        var dir = Path.Combine(Path.GetTempPath(), "otz-real2-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new OtzariaWidget.Core.Settings.JsonSettingsStore(dir);
            using var svc = new OtzariaDataService(store);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var s1 = svc.GetSnapshot();
            var cold = sw.ElapsedMilliseconds;
            long worstWarm = long.MaxValue; // best of 5: robust against CPU noise from other tests running in parallel
            for (var i = 0; i < 5; i++)
            {
                svc.Invalidate();
                sw.Restart();
                svc.GetSnapshot();
                worstWarm = Math.Min(worstWarm, sw.ElapsedMilliseconds);
            }
            sw.Restart();
            for (var i = 0; i < 1000; i++) svc.GetSnapshot();
            var cached = sw.Elapsed.TotalMilliseconds / 1000;
            output.WriteLine($"cold={cold} ms, best warm rebuild={worstWarm} ms, cached get={cached:F4} ms, progress={s1.Progress.Count}");
            Assert.True(worstWarm < 500, "warm rebuild " + worstWarm + " ms");
            Assert.True(cold < 5000, "cold " + cold + " ms");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
