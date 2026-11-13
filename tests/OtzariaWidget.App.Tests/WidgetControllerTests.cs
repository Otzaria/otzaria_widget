using System.Text.Json;
using OtzariaWidget.App.Provider.Cards;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.Tests;

internal sealed class FakeEnv : IWidgetEnvironment
{
    public WidgetAppSettings Settings { get; set; } = new();
    public OtzariaSnapshot? Snapshot { get; set; } = TestData.Full();
    public FakeCalendarService Cal { get; set; } = new();
    public DateTimeOffset NowValue { get; set; } = TestData.Now;
    public bool LaunchResult { get; set; } = true;
    public List<(string? Uri, OtzariaPaths Paths)> Launches { get; } = [];
    public int Invalidations { get; private set; }
    public int SettingsStarts { get; private set; }

    public WidgetAppSettings LoadSettings() => Settings;
    public OtzariaSnapshot? TryGetSnapshot(out string? error) { error = Snapshot is null ? "stub" : null; return Snapshot; }
    public void InvalidateData() => Invalidations++;
    public ICalendarService Calendar => Cal;
    public bool LaunchOtzaria(string? uri, OtzariaPaths paths) { Launches.Add((uri, paths)); return LaunchResult; }
    public void StartSettingsWindow() => SettingsStarts++;
    public DateTimeOffset Now => NowValue;
}

internal sealed class FakeSink : IWidgetSink
{
    public record Update(string Id, string Template, string Data, string CustomState);
    public List<Update> Updates { get; } = [];
    public bool Throw { get; set; }
    void IWidgetSink.Update(string widgetId, string template, string data, string customState)
    {
        if (Throw) throw new InvalidOperationException("host failed");
        Updates.Add(new Update(widgetId, template, data, customState));
    }
}

internal sealed class FakeScheduler
{
    public sealed class Handle(Action cb, TimeSpan due) : IDisposable
    {
        public Action Callback { get; } = cb;
        public TimeSpan Due { get; } = due;
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
    public List<Handle> Handles { get; } = [];
    public IDisposable Schedule(TimeSpan due, Action cb) { var h = new Handle(cb, due); Handles.Add(h); return h; }
    public Handle? LastLive => Handles.LastOrDefault(h => !h.Disposed);
}

public class WidgetControllerTests
{
    private static (WidgetController C, FakeEnv Env, FakeSink Sink, FakeScheduler Sched, List<string> Log) Make(
        Func<string, string?>? lookup = null)
    {
        var env = new FakeEnv(); var sink = new FakeSink(); var sched = new FakeScheduler(); var log = new List<string>();
        var c = new WidgetController(env, sink, lookup, sched.Schedule) { Log = log.Add };
        return (c, env, sink, sched, log);
    }

    private static WidgetRef W(string kind, string id = "w1", CardSize size = CardSize.Medium, bool active = false) => new(id, kind, size, active);

    [Fact]
    public void CreateWidget_pushes_main_card_with_default_customstate()
    {
        var (c, _, sink, _, _) = Make();
        c.Create(W(WidgetKinds.RecentBooks));
        var u = Assert.Single(sink.Updates);
        Assert.Equal("w1", u.Id);
        Assert.Contains("AdaptiveCard", u.Template);
        Assert.Contains("ברכות", u.Data);
        Assert.Equal(WidgetCustomState.CurrentSchema, WidgetCustomState.Parse(u.CustomState).Version);
        Assert.Equal(1, c.WidgetCount);
    }

    [Fact]
    public void Unknown_definition_is_ignored()
    {
        var (c, _, sink, _, log) = Make();
        c.Create(W("Otzaria_Spike_Widget"));
        Assert.Empty(sink.Updates);
        Assert.Equal(0, c.WidgetCount);
        Assert.Contains(log, l => l.Contains("unknown definition"));
    }

    [Fact]
    public void Only_active_widgets_are_refreshed_on_source_change()
    {
        var (c, _, sink, _, _) = Make();
        c.Create(W(WidgetKinds.Bookmarks, "a"));
        c.Create(W(WidgetKinds.RecentBooks, "b"));
        sink.Updates.Clear();

        c.OnSourceChanged("data");
        Assert.Empty(sink.Updates);                       // nothing active

        c.Activate(W(WidgetKinds.Bookmarks, "a"));
        sink.Updates.Clear();
        c.OnSourceChanged("data");
        Assert.Equal(["a"], sink.Updates.Select(u => u.Id).ToArray());

        c.Deactivate("a");
        sink.Updates.Clear();
        c.OnSourceChanged("settings");
        Assert.Empty(sink.Updates);
    }

    [Fact]
    public void Customization_flow_saves_state_and_returns_to_main_card()
    {
        var (c, _, sink, _, _) = Make();
        var w = W(WidgetKinds.RecentBooks);
        c.Create(w);
        c.CustomizationRequested(w);
        var cust = sink.Updates.Last();
        Assert.Contains("Input.Number", cust.Template);   // customize.json template
        Assert.Contains("showSearches", cust.Data);

        c.Action(w, Verbs.SaveSettings, """{"count":"2","showSearches":"true","showLocation":"false","includePdfs":"false"}""");
        var main = sink.Updates.Last();
        Assert.DoesNotContain("Input.Number", main.Template);
        var st = WidgetCustomState.Parse(main.CustomState);
        Assert.Equal(2, st.Count);
        Assert.True(st.ShowSearches);
        Assert.False(st.ShowLocation);
        Assert.False(st.IncludePdfs);
        // PDF excluded => hero is no longer the PDF tab
        Assert.DoesNotContain("דף ב עמוד א", main.Data);
        Assert.False(c.Peek("w1")!.Value.Customizing);
    }

    [Fact]
    public void Customization_cancel_keeps_state()
    {
        var (c, _, sink, _, _) = Make();
        var w = W(WidgetKinds.Bookmarks);
        c.Create(w);
        c.CustomizationRequested(w);
        c.Action(w, Verbs.ExitCustomization, """{"count":"7"}""");
        Assert.Equal(0, WidgetCustomState.Parse(sink.Updates.Last().CustomState).Count);
        Assert.False(c.Peek("w1")!.Value.Customizing);
    }

    [Fact]
    public void CustomState_from_host_is_restored_lazily_and_on_recover()
    {
        var saved = new WidgetCustomState { Count = 3, ShowSearches = true }.Serialize();
        var (c, _, sink, _, _) = Make(id => id == "lazy" ? saved : null);
        c.Activate(W(WidgetKinds.RecentBooks, "lazy", CardSize.Large, true));
        Assert.Equal(3, c.Peek("lazy")!.Value.State.Count);

        var (c2, _, sink2, _, _) = Make();
        c2.Recover([(W(WidgetKinds.Zmanim, "r1", CardSize.Small, true), saved), (W("Nope", "r2"), null), (W(WidgetKinds.Dashboard, "r3", CardSize.Large, false), null)]);
        Assert.Equal(2, c2.WidgetCount);
        Assert.Equal(["r1"], sink2.Updates.Select(u => u.Id).Distinct().ToArray());   // only the visible one rendered
    }

    [Fact]
    public void Recover_does_not_clobber_a_widget_the_host_already_created()
    {
        var (c, _, _, _, _) = Make();
        c.Create(W(WidgetKinds.Bookmarks, "x"));
        c.Action(W(WidgetKinds.Bookmarks, "x"), Verbs.Next, null);
        c.Recover([(W(WidgetKinds.Bookmarks, "x"), null)]);
        Assert.Equal(1, c.WidgetCount);
    }

    [Fact]
    public void Open_verb_launches_valid_otzaria_links_with_snapshot_paths()
    {
        var (c, env, sink, _, _) = Make();
        var w = W(WidgetKinds.RecentBooks);
        c.Create(w);
        sink.Updates.Clear();
        c.Action(w, Verbs.Open, """{"url":"otzaria://open/book/1938?index=140"}""");
        var l = Assert.Single(env.Launches);
        Assert.Equal("otzaria://open/book/1938?index=140", l.Uri);
        Assert.Equal(TestData.GoodPaths.OtzariaExe, l.Paths.OtzariaExe);
        Assert.Empty(sink.Updates);   // success needs no card refresh
    }

    [Theory]
    [InlineData("https://evil.example/x")]
    [InlineData("otzaria://plugin/install?url=http://x")]
    [InlineData("OTZARIA://PLUGIN/install-local?path=c:\\x")]
    [InlineData("file:///c:/windows/system32/cmd.exe")]
    [InlineData("")]
    [InlineData("otzaria://open/book/1\r\n--args")]
    public void Open_verb_rejects_unsafe_urls(string url)
    {
        var (c, env, _, _, _) = Make();
        var w = W(WidgetKinds.RecentBooks);
        c.Create(w);
        c.Action(w, Verbs.Open, JsonSerializer.Serialize(new { url }));
        Assert.Empty(env.Launches);
    }

    [Fact]
    public void Open_verb_with_garbage_payload_does_not_throw()
    {
        var (c, env, _, _, _) = Make();
        var w = W(WidgetKinds.RecentBooks);
        c.Create(w);
        c.Action(w, Verbs.Open, "not json");
        c.Action(w, Verbs.Open, null);
        c.Action(w, "nonsense", "{}");
        Assert.Empty(env.Launches);
    }

    [Fact]
    public void Launch_failure_shows_one_shot_notice()
    {
        var (c, env, sink, _, _) = Make();
        var w = W(WidgetKinds.RecentBooks);
        c.Create(w);
        env.LaunchResult = false;
        sink.Updates.Clear();
        c.Action(w, Verbs.OpenOtzaria, null);
        Assert.Contains("אוצריא לא נמצא", sink.Updates.Last().Data);
        Assert.Null(env.Launches.Single().Uri);
        env.LaunchResult = true;
        c.Action(w, Verbs.Refresh, null);
        Assert.DoesNotContain("אוצריא לא נמצא", sink.Updates.Last().Data);
    }

    [Fact]
    public void Settings_verb_starts_settings_window_and_refresh_invalidates()
    {
        var (c, env, _, _, _) = Make();
        var w = W(WidgetKinds.ShamorZachor);
        c.Create(w);
        c.Action(w, Verbs.Settings, null);
        c.Action(w, Verbs.Refresh, null);
        Assert.Equal(1, env.SettingsStarts);
        Assert.Equal(1, env.Invalidations);
    }

    [Fact]
    public void Paging_is_clamped_by_the_builder_result()
    {
        var (c, _, sink, _, _) = Make();
        var w = W(WidgetKinds.RecentBooks, "p", CardSize.Large, true);
        c.Create(w);
        for (int i = 0; i < 20; i++) c.Action(w, Verbs.Next, null);
        var page = c.Peek("p")!.Value.Page;
        Assert.InRange(page, 1, 3);              // 9 older books / 6 per page => at most 2 pages
        c.Action(w, Verbs.Prev, null);
        Assert.Equal(page - 1, c.Peek("p")!.Value.Page);
        c.Action(w, Verbs.Prev, null); c.Action(w, Verbs.Prev, null); c.Action(w, Verbs.Prev, null);
        Assert.Equal(0, c.Peek("p")!.Value.Page);
    }

    [Fact]
    public void Resize_resets_page_and_rerenders()
    {
        var (c, _, sink, _, _) = Make();
        var w = W(WidgetKinds.Bookmarks, "s", CardSize.Medium, true);
        c.Create(w);
        c.Action(w, Verbs.Next, null);
        sink.Updates.Clear();
        c.ContextChanged(w with { Size = CardSize.Large });
        Assert.Single(sink.Updates);
        Assert.Equal(CardSize.Large, c.Peek("s")!.Value.Size);
        Assert.Equal(0, c.Peek("s")!.Value.Page);
    }

    [Fact]
    public void Zmanim_timer_is_scheduled_only_while_active_and_refires()
    {
        var (c, env, sink, sched, _) = Make();
        var w = W(WidgetKinds.Zmanim, "z", CardSize.Medium, false);
        c.Create(w);
        Assert.Empty(sched.Handles);                       // inactive => no timer

        c.Activate(w);
        var h = sched.LastLive!;
        // next zman after 10:30 is chatzos 11:37 (+1 s) - but the timer is a coarse 60 s tick that re-checks the wall clock
        Assert.Equal(WidgetController.MaxTick, h.Due);

        // an early tick (boundary not reached) re-arms instead of rendering
        var beforeEarly = sink.Updates.Count;
        h.Callback();
        Assert.Equal(beforeEarly, sink.Updates.Count);
        h = sched.LastLive!;
        Assert.False(h.Disposed);

        // fire when time has advanced
        env.NowValue = TestData.Now.AddMinutes(68);
        var before = sink.Updates.Count;
        h.Callback();
        Assert.Equal(before + 1, sink.Updates.Count);
        Assert.NotNull(sched.LastLive);

        c.Deactivate("z");
        Assert.All(sched.Handles, x => Assert.True(x.Disposed));
        before = sink.Updates.Count;
        h.Callback();                                      // stale timer callback after deactivation does nothing
        Assert.Equal(before, sink.Updates.Count);
    }

    [Fact]
    public void Timer_is_cancelled_during_customization_and_restored_after_save()
    {
        var (c, _, _, sched, _) = Make();
        var w = W(WidgetKinds.HebrewCalendar, "k", CardSize.Medium, true);
        c.Create(w);
        Assert.NotNull(sched.LastLive);
        c.CustomizationRequested(w);
        Assert.Null(sched.LastLive);
        c.Action(w, Verbs.SaveSettings, """{"showOmer":"false"}""");
        Assert.NotNull(sched.LastLive);
    }

    [Fact]
    public void Delete_removes_widget_and_timer()
    {
        var (c, _, sink, sched, _) = Make();
        var w = W(WidgetKinds.Zmanim, "d", CardSize.Small, true);
        c.Create(w);
        c.Delete("d");
        Assert.Equal(0, c.WidgetCount);
        Assert.All(sched.Handles, h => Assert.True(h.Disposed));
        sink.Updates.Clear();
        c.OnSourceChanged("data");
        Assert.Empty(sink.Updates);
    }

    [Fact]
    public void Stubbed_services_produce_graceful_cards_not_exceptions()
    {
        var (c, env, sink, _, _) = Make();
        env.Snapshot = null;                                   // data service throws NotImplementedException
        env.Cal = new FakeCalendarService { Throw = true };    // calendar throws NotImplementedException
        foreach (var kind in WidgetKinds.All)
        {
            var w = W(kind, "g-" + kind, CardSize.Large, true);
            c.Create(w);
            var u = sink.Updates.Last(x => x.Id == w.Id);
            var card = CardExpander.Expand(new CardResult(TemplateOf(u), u.Data), "large", "dark");
            Assert.True(CardExpander.TextBlocks(card.Root).Any(CardExpander.HasHebrew), kind);
        }
    }

    private static string TemplateOf(FakeSink.Update u)
    {
        foreach (var t in CardTemplates.All)
            if (CardTemplates.Load(t) == u.Template) return t;
        throw new InvalidOperationException("template not recognised");
    }

    [Fact]
    public void Sink_failure_never_escapes()
    {
        var (c, _, sink, _, log) = Make();
        sink.Throw = true;
        c.Create(W(WidgetKinds.RecentBooks));
        c.Activate(W(WidgetKinds.RecentBooks));
        c.OnSourceChanged("data");
        Assert.Contains(log, l => l.Contains("render failed"));
    }

    [Fact]
    public void ProviderLaunch_mode_emits_execute_open_with_url()
    {
        var (c, env, sink, _, _) = Make();
        env.Settings = new WidgetAppSettings { LinkMode = LinkOpenMode.ProviderLaunch };
        c.Create(W(WidgetKinds.RecentBooks));
        using var doc = JsonDocument.Parse(sink.Updates.Last().Data);
        var action = doc.RootElement.GetProperty("hero").GetProperty("action");
        Assert.Equal("Action.Execute", action.GetProperty("type").GetString());
        Assert.Equal("open", action.GetProperty("verb").GetString());
        Assert.StartsWith("otzaria://", action.GetProperty("data").GetProperty("url").GetString());
        Assert.True(doc.RootElement.GetProperty("hero").TryGetProperty("urlAction", out _));
    }
}
