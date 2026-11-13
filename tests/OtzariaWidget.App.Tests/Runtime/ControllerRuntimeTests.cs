using OtzariaWidget.App.Provider.Cards;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.Tests.Runtime;

/// <summary>Runtime behaviour of <see cref="WidgetController"/> that the host-callback adapter relies on: lifecycle ordering, threading, timers.</summary>
public class ControllerRuntimeTests
{
    private static (WidgetController C, FakeEnv Env, FakeSink Sink, FakeScheduler Sched, List<string> Log) Make(Func<string, string?>? lookup = null)
    {
        var env = new FakeEnv(); var sink = new FakeSink(); var sched = new FakeScheduler(); var log = new List<string>();
        var c = new WidgetController(env, sink, lookup, sched.Schedule) { Log = m => { lock (log) log.Add(m); } };
        return (c, env, sink, sched, log);
    }

    private static WidgetRef W(string kind, string id = "w1", CardSize size = CardSize.Medium, bool active = false) => new(id, kind, size, active);

    // ------------------------------------------------------------------------------------------------ delete semantics
    [Fact]
    public void Late_callbacks_after_delete_do_not_resurrect_or_update_the_widget()
    {
        var (c, _, sink, sched, _) = Make();
        var w = W(WidgetKinds.Zmanim, "gone", CardSize.Medium, true);
        c.Create(w);
        c.Delete("gone");
        sink.Updates.Clear();

        c.Activate(w);
        c.ContextChanged(w);
        c.CustomizationRequested(w);
        c.Action(w, Verbs.Next, null);
        c.OnSourceChanged("data");

        Assert.Empty(sink.Updates);
        Assert.Equal(0, c.WidgetCount);
        Assert.All(sched.Handles, h => Assert.True(h.Disposed));
    }

    [Fact]
    public void Explicit_create_after_delete_of_same_id_works_again()
    {
        var (c, _, sink, _, _) = Make();
        var w = W(WidgetKinds.Bookmarks, "again");
        c.Create(w);
        c.Delete("again");
        sink.Updates.Clear();
        c.Create(w);
        Assert.Single(sink.Updates);
        Assert.Equal(1, c.WidgetCount);
    }

    [Fact]
    public void Delete_of_unknown_widget_is_harmless()
    {
        var (c, _, _, _, _) = Make();
        c.Delete("never-seen");
        c.Deactivate("never-seen");
        Assert.Equal(0, c.WidgetCount);
    }

    [Fact]
    public void Recover_skips_widgets_deleted_in_the_meantime()
    {
        var (c, _, sink, _, _) = Make();
        c.Delete("x");
        c.Recover([(W(WidgetKinds.Zmanim, "x", CardSize.Small, true), null)]);
        Assert.Equal(0, c.WidgetCount);
        Assert.Empty(sink.Updates);
    }

    [Fact]
    public void Recover_is_null_safe_and_survives_garbage_custom_state()
    {
        var (c, _, sink, _, _) = Make();
        c.Recover(
        [
            (W(WidgetKinds.Zmanim, "a", CardSize.Small, true), null),
            (W(WidgetKinds.Bookmarks, "b", CardSize.Large, true), ""),
            (W(WidgetKinds.Dashboard, "c", CardSize.Large, true), "{not json"),
            (W(WidgetKinds.RecentBooks, "d", CardSize.Medium, false), "[1,2,3]"),
        ]);
        Assert.Equal(4, c.WidgetCount);
        Assert.Equal(["a", "b", "c"], sink.Updates.Select(u => u.Id).OrderBy(x => x).ToArray());   // d is not visible -> not rendered
        Assert.Equal(WidgetCustomState.CurrentSchema, c.Peek("c")!.Value.State.Version);
    }

    // ------------------------------------------------------------------------------------------------ source changes / customization
    [Fact]
    public void Source_change_does_not_overwrite_a_customization_card_being_edited()
    {
        var (c, _, sink, _, _) = Make();
        var w = W(WidgetKinds.RecentBooks, "cust", CardSize.Large, true);
        c.Create(w);
        c.CustomizationRequested(w);
        sink.Updates.Clear();

        c.OnSourceChanged("data");
        Assert.Empty(sink.Updates);

        c.Action(w, Verbs.ExitCustomization, null);   // back on the main card, then updates flow again
        sink.Updates.Clear();
        c.OnSourceChanged("data");
        Assert.Single(sink.Updates);
    }

    [Fact]
    public void Activate_always_refreshes_the_card()
    {
        var (c, env, sink, _, _) = Make();
        var w = W(WidgetKinds.HebrewCalendar, "act", CardSize.Medium, false);
        c.Create(w);
        sink.Updates.Clear();
        env.NowValue = TestData.Now.AddHours(30);
        c.Activate(w);
        Assert.Single(sink.Updates);
    }

    [Fact]
    public void Unknown_verbs_and_null_payloads_do_not_render_or_throw()
    {
        var (c, _, sink, _, _) = Make();
        var w = W(WidgetKinds.Bookmarks);
        c.Create(w);
        sink.Updates.Clear();
        c.Action(w, "", null);
        c.Action(w, "dropTables", "{}");
        c.Action(w, Verbs.SaveSettings, null);   // empty inputs: keeps state, still returns to the main card
        Assert.Single(sink.Updates);
    }

    // ------------------------------------------------------------------------------------------------ timers
    [Fact]
    public void Timer_never_sleeps_longer_than_one_minute_and_survives_a_clock_jump_back()
    {
        var (c, env, sink, sched, _) = Make();
        var w = W(WidgetKinds.HebrewCalendar, "t", CardSize.Medium, true);
        c.Create(w);
        var h = sched.LastLive!;
        Assert.True(h.Due <= WidgetController.MaxTick);

        // wall clock jumps backwards (DST end / manual change): the tick re-arms, does not render and does not throw
        env.NowValue = TestData.Now.AddHours(-1);
        var before = sink.Updates.Count;
        h.Callback();
        Assert.Equal(before, sink.Updates.Count);
        Assert.NotNull(sched.LastLive);
        Assert.True(sched.LastLive!.Due <= WidgetController.MaxTick);
    }

    [Fact]
    public void Timer_after_a_long_sleep_renders_once_at_the_first_tick()
    {
        var (c, env, sink, sched, _) = Make();
        var w = W(WidgetKinds.Zmanim, "sleep", CardSize.Medium, true);
        c.Create(w);
        var h = sched.LastLive!;
        env.NowValue = TestData.Now.AddHours(9);   // laptop slept through several boundaries
        sink.Updates.Clear();
        h.Callback();
        Assert.Single(sink.Updates);
    }

    [Fact]
    public void Timer_callback_for_unknown_or_deleted_widget_never_throws()
    {
        var (c, _, _, sched, _) = Make();
        var w = W(WidgetKinds.Zmanim, "tt", CardSize.Medium, true);
        c.Create(w);
        var h = sched.LastLive!;
        c.Delete("tt");
        h.Callback();
        c.Dispose();
        h.Callback();
    }

    [Fact]
    public void Failing_environment_inside_a_timer_callback_is_contained()
    {
        var (c, env, _, sched, log) = Make();
        var w = W(WidgetKinds.Zmanim, "boom", CardSize.Medium, true);
        c.Create(w);
        var h = sched.LastLive!;
        env.NowValue = TestData.Now.AddHours(2);
        env.Snapshot = null;
        env.Cal = new FakeCalendarService { Throw = true };
        var ex = Record.Exception(h.Callback);
        Assert.Null(ex);
        lock (log) Assert.DoesNotContain(log, l => l.Contains("timer callback failed"));   // handled as an error card, not an exception
    }

    // ------------------------------------------------------------------------------------------------ concurrency
    [Fact]
    public async Task Parallel_host_callbacks_do_not_deadlock_or_lose_the_final_customization()
    {
        var sink = new SyncSink();
        var c = new WidgetController(new FakeEnv(), sink, null, new FakeScheduler().Schedule);
        var tasks = new List<Task>();
        for (int n = 0; n < 6; n++)
        {
            var id = "p" + n;
            var kind = n % 2 == 0 ? WidgetKinds.RecentBooks : WidgetKinds.Bookmarks;   // both honour "count"
            var w = W(kind, id, CardSize.Large, true);
            tasks.Add(Task.Run(() =>
            {
                c.Create(w);
                for (int i = 0; i < 40; i++)
                {
                    c.Activate(w);
                    c.Action(w, Verbs.Next, null);
                    c.OnSourceChanged("data");
                    c.ContextChanged(w with { Size = i % 2 == 0 ? CardSize.Medium : CardSize.Large });
                    c.Action(w, Verbs.Prev, null);
                }
                c.CustomizationRequested(w);
                c.Action(w, Verbs.SaveSettings, """{"count":"3"}""");
            }));
        }
        // a data-changed storm on other threads at the same time
        for (int t = 0; t < 3; t++) tasks.Add(Task.Run(() => { for (int i = 0; i < 60; i++) c.OnSourceChanged("settings"); }));

        var all = Task.WhenAll(tasks);
        Assert.True(await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(60))) == all, "deadlock suspected");
        await all;

        Assert.Equal(6, c.WidgetCount);
        for (int n = 0; n < 6; n++)
        {
            var peek = c.Peek("p" + n)!.Value;
            Assert.False(peek.Customizing);
            Assert.Equal(3, peek.State.Count);
            // whatever pushed last for this widget must carry the saved customization
            var last = sink.Snapshot().Last(u => u.Id == "p" + n);
            Assert.Equal(3, WidgetCustomState.Parse(last.CustomState).Count);
        }
    }

    [Fact]
    public async Task Delete_racing_with_renders_leaves_no_updates_after_it_returns()
    {
        var env = new FakeEnv();
        var sink = new SlowSink();
        var sched = new FakeScheduler();
        var c = new WidgetController(env, sink, null, sched.Schedule);
        var w = W(WidgetKinds.RecentBooks, "race", CardSize.Large, true);
        c.Create(w);

        var storm = Task.Run(() => { for (int i = 0; i < 200; i++) c.OnSourceChanged("data"); });
        await Task.Delay(20);
        c.Delete("race");
        int atDelete = sink.Count;
        await Task.Delay(50);
        await storm;
        // at most the one render that was already inside the host call when Delete ran can still land
        Assert.True(sink.Count - atDelete <= 1, $"{sink.Count - atDelete} updates after Delete");
    }

    private sealed class SyncSink : IWidgetSink
    {
        private readonly List<FakeSink.Update> _all = [];
        public List<FakeSink.Update> Snapshot() { lock (_all) return [.. _all]; }
        public void Update(string widgetId, string template, string data, string customState)
        { lock (_all) _all.Add(new FakeSink.Update(widgetId, template, data, customState)); }
    }

    private sealed class SlowSink : IWidgetSink
    {
        private int _n;
        public int Count => Volatile.Read(ref _n);
        public void Update(string widgetId, string template, string data, string customState)
        {
            Thread.Sleep(2);   // stands in for the cross-process UpdateWidget call
            Interlocked.Increment(ref _n);
        }
    }

    // ------------------------------------------------------------------------------------------------ open-url validation
    [Theory]
    [InlineData("otzaria://open/book/1938?index=140", true)]
    [InlineData("otzaria://open/book/1938", true)]
    [InlineData("OTZARIA://OPEN/PDF/131?index=150", true)]
    [InlineData("otzaria://open/pdf/131?source=user&index=3", true)]
    [InlineData("otzaria://open/search?q=%D7%A9%D7%9C%D7%95%D7%9D&mode=exact", true)]
    [InlineData("otzaria://open/search", true)]
    [InlineData("otzaria://open/library", true)]
    [InlineData("otzaria://open/history", true)]
    [InlineData("otzaria://open/bookmarks", true)]
    [InlineData("otzaria://open/calendar", true)]
    [InlineData("otzaria://open/shamor_zachor", true)]
    [InlineData("otzaria://open/plugin/evil", false)]
    [InlineData("otzaria://open/Plugin?x=1", false)]
    [InlineData("otzaria://open/%70lugin/evil", false)]
    [InlineData("otzaria://open/book/../plugin/x", false)]
    [InlineData("otzaria://open/book/abc", false)]
    [InlineData("otzaria://open/book/1/extra", false)]
    [InlineData("otzaria://open/library/extra", false)]
    [InlineData("otzaria://open/tool/x", false)]
    [InlineData("otzaria://open/settings", false)]
    [InlineData("otzaria://open/inspection", false)]
    [InlineData("otzaria://plugin/install?url=http://x", false)]
    [InlineData("otzaria://settings", false)]
    [InlineData("otzaria-widget://settings", false)]
    [InlineData("https://example.com/", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("otzaria://open/book/1 --flag", false)]
    [InlineData("otzaria://open/book/1\"", false)]
    [InlineData("otzaria://open/book/1?x=`calc`", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSafeOtzariaUrl_allowlist(string? url, bool expected) => Assert.Equal(expected, WidgetController.IsSafeOtzariaUrl(url));

    [Fact]
    public void IsSafeOtzariaUrl_rejects_overlong_urls()
        => Assert.False(WidgetController.IsSafeOtzariaUrl("otzaria://open/search?q=" + new string('a', 9000)));

    [Fact]
    public void Open_verb_never_reaches_the_launcher_for_unsafe_data_shapes()
    {
        var (c, env, _, _, _) = Make();
        var w = W(WidgetKinds.RecentBooks);
        c.Create(w);
        foreach (var data in new[] { """{"url":123}""", """{"url":null}""", """{"u":"otzaria://open/library"}""", "[]", "\"otzaria://open/library\"", "{" })
            c.Action(w, Verbs.Open, data);
        Assert.Empty(env.Launches);
    }

    [Fact]
    public void Settings_verb_failure_shows_notice_and_never_throws()
    {
        var env = new ThrowingSettingsEnv();
        var sink = new FakeSink();
        var c = new WidgetController(env, sink);
        var w = W(WidgetKinds.Bookmarks);
        c.Create(w);
        sink.Updates.Clear();
        c.Action(w, Verbs.Settings, null);
        Assert.Single(sink.Updates);
        Assert.Contains("לא ניתן לפתוח את ההגדרות", sink.Updates[0].Data);
    }

    private sealed class ThrowingSettingsEnv : IWidgetEnvironment
    {
        private readonly FakeEnv _inner = new();
        public WidgetAppSettings LoadSettings() => _inner.LoadSettings();
        public OtzariaSnapshot? TryGetSnapshot(out string? error) => _inner.TryGetSnapshot(out error);
        public void InvalidateData() { }
        public ICalendarService Calendar => _inner.Calendar;
        public bool LaunchOtzaria(string? uri, OtzariaPaths paths) => true;
        public void StartSettingsWindow() => throw new InvalidOperationException("no process path");
        public DateTimeOffset Now => _inner.Now;
    }

    [Fact]
    public void Log_never_contains_content_of_the_cards()
    {
        var (c, _, _, _, log) = Make();
        var w = W(WidgetKinds.RecentBooks, "priv", CardSize.Large, true);
        c.Create(w);
        c.Action(w, Verbs.Open, """{"url":"otzaria://open/book/1938?index=140"}""");
        c.Action(w, Verbs.SaveSettings, """{"count":"2"}""");
        string text; lock (log) text = string.Join('\n', log);
        Assert.DoesNotContain("ברכות", text);   // a book title from the fake snapshot
        Assert.DoesNotContain("1938", text);
    }
}
