// Agent D - the provider's brain without any WinRT type: per-widget state, activation tracking, action verbs,
// refresh timers. WidgetProvider (WinRT adapter) forwards host callbacks here; tests drive it with fakes.
using System.Text.Json.Nodes;
using OtzariaWidget.Core.Contracts;
using OtzariaWidget.Core.Launch;

namespace OtzariaWidget.App.Provider.Cards;

/// <summary>Services the controller needs (implemented by ProviderServices in production, by fakes in tests).</summary>
public interface IWidgetEnvironment
{
    WidgetAppSettings LoadSettings();
    /// <summary>Null (+ error type name) when the data service fails.</summary>
    OtzariaSnapshot? TryGetSnapshot(out string? error);
    void InvalidateData();
    ICalendarService Calendar { get; }
    /// <summary>Launch Otzaria (uri null = just open it). False when Otzaria could not be found/started.</summary>
    bool LaunchOtzaria(string? uri, OtzariaPaths paths);
    /// <summary>Start the settings window (own exe, no args).</summary>
    void StartSettingsWindow();
    DateTimeOffset Now { get; }
}

/// <summary>Where finished cards go (WidgetManager.UpdateWidget in production).</summary>
public interface IWidgetSink
{
    void Update(string widgetId, string template, string data, string customState);
}

/// <summary>Plain description of a widget as reported by the host.</summary>
public sealed record WidgetRef(string Id, string DefinitionId, CardSize Size, bool IsActive);

public sealed class WidgetController : IDisposable
{
    /// <summary>Longest time a refresh timer sleeps. A coarse tick (instead of one long timer to the next boundary) re-evaluates the
    /// wall clock every minute, so DST changes, manual clock changes and sleep/resume cannot make a boundary refresh late.</summary>
    public static readonly TimeSpan MaxTick = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MinTick = TimeSpan.FromSeconds(5);
    private const int MaxTombstones = 256;

    private sealed class Instance
    {
        public required string Id { get; init; }
        public required string Kind { get; init; }
        // --- everything below is guarded by Gate (short critical sections only: never call the host while holding it)
        public CardSize Size { get; set; } = CardSize.Medium;
        public bool IsActive { get; set; }
        public bool Customizing { get; set; }
        public int Page { get; set; }
        public string? Notice { get; set; }
        public WidgetCustomState State { get; set; } = new();
        public IDisposable? Timer { get; set; }
        public DateTimeOffset? NextRefresh { get; set; }
        public bool Deleted { get; set; }
        public readonly object Gate = new();
        /// <summary>Serialises build+push so an older card can never overwrite a newer one (held while calling the host, but Gate is not).</summary>
        public readonly object RenderGate = new();
    }

    private readonly IWidgetEnvironment _env;
    private readonly IWidgetSink _sink;
    private readonly Func<string, string?> _lookupCustomState;
    private readonly Func<TimeSpan, Action, IDisposable> _schedule;
    private readonly Dictionary<string, Instance> _widgets = new(StringComparer.Ordinal);
    private readonly Queue<string> _tombstoneOrder = new();
    private readonly HashSet<string> _tombstones = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private long _lastActivityTicks = DateTime.UtcNow.Ticks;
    private volatile bool _disposed;

    public Action<string>? Log { get; set; }

    public WidgetController(IWidgetEnvironment env, IWidgetSink sink,
        Func<string, string?>? lookupCustomState = null, Func<TimeSpan, Action, IDisposable>? scheduler = null)
    {
        _env = env;
        _sink = sink;
        _lookupCustomState = lookupCustomState ?? (_ => null);
        _schedule = scheduler ?? DefaultSchedule;
    }

    private static IDisposable DefaultSchedule(TimeSpan due, Action callback) =>
        new Timer(_ => callback(), null, due, Timeout.InfiniteTimeSpan);

    private void Info(string m) { try { Log?.Invoke(m); } catch { /* logging must never throw */ } }

    // ------------------------------------------------------------------------------------------------ status
    public int WidgetCount { get { lock (_gate) return _widgets.Count; } }
    public DateTime LastActivityUtc => new(Interlocked.Read(ref _lastActivityTicks), DateTimeKind.Utc);
    public void Touch() => Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);

    public IReadOnlyList<string> WidgetIds { get { lock (_gate) return _widgets.Keys.ToList(); } }

    /// <summary>Test/diagnostic view of one widget's state.</summary>
    public (CardSize Size, bool Active, bool Customizing, int Page, WidgetCustomState State)? Peek(string id)
    {
        Instance? w;
        lock (_gate) _widgets.TryGetValue(id, out w);
        if (w is null) return null;
        lock (w.Gate) return (w.Size, w.IsActive, w.Customizing, w.Page, w.State);
    }

    // ------------------------------------------------------------------------------------------------ host callbacks
    /// <summary>Rebuilds the map from the host's record after a (re)start. Renders widgets that are currently visible.</summary>
    public void Recover(IEnumerable<(WidgetRef Widget, string? CustomState)> known)
    {
        foreach (var (w, cs) in known)
        {
            try
            {
                if (w is null) continue;
                if (!WidgetKinds.IsKnown(w.DefinitionId)) { Info($"recover: ignoring unknown definition '{w.DefinitionId}' id={w.Id}"); continue; }
                Instance inst;
                lock (_gate)
                {
                    if (_tombstones.Contains(w.Id) || _widgets.ContainsKey(w.Id)) continue;   // deleted meanwhile / host already called us for it
                    inst = Register(w, cs);
                }
                Info($"recovered id={w.Id} kind={w.DefinitionId} size={w.Size} active={w.IsActive}");
                if (w.IsActive) Render(inst);
            }
            catch (Exception ex) { Info($"recover failed for one widget: {ex.GetType().Name}"); }
        }
    }

    public void Create(WidgetRef w)
    {
        Touch();
        lock (_gate) _tombstones.Remove(w.Id);   // an explicit CreateWidget always wins over an earlier delete of the same id
        var inst = GetOrAdd(w);
        if (inst is null) return;
        lock (inst.Gate) { inst.Size = w.Size; inst.IsActive |= w.IsActive; }
        Render(inst);
    }

    public void Delete(string widgetId)
    {
        Touch();
        Instance? inst;
        lock (_gate)
        {
            _widgets.Remove(widgetId, out inst);
            if (_tombstones.Add(widgetId))
            {
                _tombstoneOrder.Enqueue(widgetId);
                while (_tombstoneOrder.Count > MaxTombstones) _tombstones.Remove(_tombstoneOrder.Dequeue());
            }
        }
        if (inst is not null)
        {
            lock (inst.Gate) { inst.Deleted = true; inst.Timer?.Dispose(); inst.Timer = null; inst.NextRefresh = null; }
        }
    }

    public void ContextChanged(WidgetRef w)
    {
        Touch();
        var inst = GetOrAdd(w);
        if (inst is null) return;
        lock (inst.Gate)
        {
            if (inst.Size != w.Size) inst.Page = 0;
            inst.Size = w.Size;
            inst.IsActive = w.IsActive;
        }
        Render(inst);
    }

    public void Activate(WidgetRef w)
    {
        Touch();
        var inst = GetOrAdd(w);
        if (inst is null) return;
        lock (inst.Gate) { inst.Size = w.Size; inst.IsActive = true; }
        Render(inst);   // always refresh on activation: the data / the clock may have changed while we were inactive (or asleep)
    }

    public void Deactivate(string widgetId)
    {
        Touch();
        Instance? inst;
        lock (_gate) _widgets.TryGetValue(widgetId, out inst);
        if (inst is null) return;
        lock (inst.Gate) { inst.IsActive = false; inst.Timer?.Dispose(); inst.Timer = null; inst.NextRefresh = null; }
    }

    public void CustomizationRequested(WidgetRef w)
    {
        Touch();
        var inst = GetOrAdd(w);
        if (inst is null) return;
        lock (inst.Gate) { inst.Size = w.Size; inst.Customizing = true; }
        Render(inst);
    }

    /// <summary>Handles an Action.Execute verb. <paramref name="data"/> is the JSON the host sends (action data + input values).</summary>
    public void Action(WidgetRef w, string verb, string? data)
    {
        Touch();
        var inst = GetOrAdd(w);
        if (inst is null) return;
        bool render = true;
        switch (verb)
        {
            case Verbs.Open:
                render = OpenLink(inst, data);
                break;

            case Verbs.OpenOtzaria:
                render = LaunchOrNotice(inst, null);
                break;

            case Verbs.Settings:
                try { _env.StartSettingsWindow(); }
                catch (Exception ex) { Info($"settings window failed: {ex.GetType().Name}"); SetNotice(inst, "לא ניתן לפתוח את ההגדרות"); }
                lock (inst.Gate) render = inst.Notice is not null;
                break;

            case Verbs.Refresh:
                _env.InvalidateData();
                lock (inst.Gate) inst.Notice = null;
                break;

            case Verbs.Next:
                lock (inst.Gate) inst.Page++;
                break;

            case Verbs.Prev:
                lock (inst.Gate) inst.Page = Math.Max(0, inst.Page - 1);
                break;

            case Verbs.SaveSettings:
                lock (inst.Gate)
                {
                    inst.State.ApplyInputs(inst.Kind, data);
                    inst.State.Version = WidgetCustomState.CurrentSchema;
                    inst.Customizing = false;
                    inst.Page = 0;
                }
                break;

            case Verbs.ExitCustomization:
                lock (inst.Gate) inst.Customizing = false;
                break;

            default:
                Info($"unknown verb '{verb}' (id={inst.Id})");
                render = false;
                break;
        }
        if (render) Render(inst);
    }

    /// <summary>Otzaria data or widget settings changed: refresh only the widgets that are on screen (and not being customized:
    /// re-sending the customization card would wipe what the user is typing).</summary>
    public void OnSourceChanged(string source)
    {
        Touch();
        List<Instance> active;
        lock (_gate) active = _widgets.Values.ToList();
        active = active.Where(i => { lock (i.Gate) return i.IsActive && !i.Customizing && !i.Deleted; }).ToList();
        Info($"source '{source}' changed: refreshing {active.Count} active widget(s)");
        foreach (var i in active)
        {
            try { Render(i); }
            catch (Exception ex) { Info($"refresh failed id={i.Id}: {ex.GetType().Name}"); }
        }
    }

    // ------------------------------------------------------------------------------------------------ internals
    /// <summary>Caller holds <c>_gate</c>.</summary>
    private Instance Register(WidgetRef w, string? customState)
    {
        var inst = new Instance
        {
            Id = w.Id, Kind = w.DefinitionId, Size = w.Size, IsActive = w.IsActive,
            State = WidgetCustomState.Parse(customState),
        };
        _widgets[w.Id] = inst;
        return inst;
    }

    private Instance? GetOrAdd(WidgetRef w)
    {
        lock (_gate)
        {
            if (_widgets.TryGetValue(w.Id, out var existing)) return existing;
            if (_tombstones.Contains(w.Id)) { Info($"ignoring late callback for deleted widget id={w.Id}"); return null; }
        }
        if (!WidgetKinds.IsKnown(w.DefinitionId))
        {
            Info($"ignoring widget id={w.Id} with unknown definition '{w.DefinitionId}'");
            return null;
        }
        string? cs = null;
        try { cs = _lookupCustomState(w.Id); } catch (Exception ex) { Info($"customState lookup failed: {ex.GetType().Name}"); }
        lock (_gate)
        {
            if (_disposed) return null;
            if (_widgets.TryGetValue(w.Id, out var raced)) return raced;
            if (_tombstones.Contains(w.Id)) return null;
            return Register(w, cs);
        }
    }

    private bool OpenLink(Instance inst, string? data)
    {
        string? url = null;
        try { url = (JsonNode.Parse(data ?? "{}") as JsonObject)?["url"]?.GetValue<string>(); }
        catch (Exception) { /* fallthrough: invalid payload */ }
        if (!IsSafeOtzariaUrl(url))
        {
            Info($"open: rejected url (id={inst.Id})");
            return false;
        }
        return LaunchOrNotice(inst, url);
    }

    private static readonly HashSet<string> AllowedOpenTargets = new(StringComparer.OrdinalIgnoreCase)
        { "book", "pdf", "search", "library", "history", "bookmarks", "calendar", "shamor_zachor" };

    /// <summary>
    /// Only what our own cards/launcher generate: <c>otzaria://open/{book|pdf}/&lt;id&gt;[?...]</c>, <c>open/search[?q=..]</c> and the
    /// plain screens (library, history, bookmarks, calendar, shamor_zachor). Never plugin/tool/settings/other hosts
    /// (storage-and-links.md 5.2). <see cref="OtzariaLauncher.IsSafeOtzariaUri"/> adds the character / dot-segment checks.
    /// </summary>
    public static bool IsSafeOtzariaUrl(string? url)
    {
        if (!OtzariaLauncher.IsSafeOtzariaUri(url)) return false;
        var rest = url!["otzaria://open/".Length..];
        var q = rest.IndexOfAny(['?', '#']);
        var segs = (q < 0 ? rest : rest[..q]).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segs.Length == 0 || !AllowedOpenTargets.Contains(segs[0])) return false;
        if (segs[0].Equals("book", StringComparison.OrdinalIgnoreCase) || segs[0].Equals("pdf", StringComparison.OrdinalIgnoreCase))
            return segs.Length == 2 && segs[1].Length is > 0 and <= 10 && segs[1].All(char.IsAsciiDigit);
        return segs.Length == 1;
    }

    /// <returns>True when a card refresh is needed (failure notice to show).</returns>
    private bool LaunchOrNotice(Instance inst, string? uri)
    {
        try
        {
            var snap = _env.TryGetSnapshot(out _);
            bool ok = _env.LaunchOtzaria(uri, snap?.Paths ?? new OtzariaPaths());
            if (!ok) { SetNotice(inst, "אוצריא לא נמצא — בדוק את ההגדרות"); return true; }
            lock (inst.Gate) { if (inst.Notice is not null) { inst.Notice = null; return true; } }
            return false;
        }
        catch (Exception ex)
        {
            Info($"launch failed: {ex.GetType().Name}");
            SetNotice(inst, "לא ניתן לפתוח את אוצריא");
            return true;
        }
    }

    private static void SetNotice(Instance inst, string text) { lock (inst.Gate) inst.Notice = text; }

    /// <summary>Builds and pushes the card for one widget. Never throws (errors become error cards / logged).
    /// Concurrent renders of the same widget are serialised (RenderGate) and every one reads a fresh state snapshot, so the last push
    /// always reflects the latest customization; nothing is pushed after Delete (an already in-flight host call cannot be recalled).</summary>
    private void Render(Instance inst)
    {
        if (_disposed) return;
        try
        {
            lock (inst.RenderGate)
            {
                CardSize size; bool customizing; int page; string? notice; WidgetCustomState state;
                lock (inst.Gate)
                {
                    if (inst.Deleted) return;
                    size = inst.Size; customizing = inst.Customizing; page = inst.Page; notice = inst.Notice;
                    state = inst.State.Clone();
                }

                var settings = _env.LoadSettings();
                var snap = _env.TryGetSnapshot(out var snapErr);
                var ctx = new CardContext
                {
                    Kind = inst.Kind, Size = size, Settings = settings, Calendar = _env.Calendar, Now = _env.Now,
                    Snapshot = snap, SnapshotError = snapErr, State = state, Page = page, Notice = notice,
                };

                CardResult card = customizing
                    ? CardBuilders.BuildCustomizationSafe(ctx, ex => Info($"customization build failed kind={inst.Kind}: {ex.GetType().Name}"))
                    : CardBuilders.BuildSafe(ctx, ex => Info($"build failed kind={inst.Kind}: {ex.GetType().Name}"));

                var template = CardTemplates.Load(card.TemplateFile);
                string customState;
                lock (inst.Gate)
                {
                    if (inst.Deleted) return;
                    if (!customizing && card.EffectivePage is int ep && inst.Page == page) inst.Page = ep;   // builders clamp the page; keep prev/next honest
                    customState = inst.State.Serialize();                                                    // latest, even if saved while we were building
                }

                _sink.Update(inst.Id, template, card.DataJson, customState);

                lock (inst.Gate)
                {
                    if (inst.Notice == notice) inst.Notice = null;   // one-shot: but keep a newer notice set meanwhile
                    Info($"updated id={inst.Id} kind={inst.Kind} size={inst.Size} customizing={customizing} {card.Describe()}");
                    Reschedule(inst, inst.Customizing ? null : card.NextRefresh);
                }
            }
        }
        catch (Exception ex)
        {
            Info($"render failed id={inst.Id} kind={inst.Kind}: {ex.GetType().Name}");
        }
    }

    /// <summary>Remember when this widget next needs a time-based refresh and arm a (at most <see cref="MaxTick"/>) timer. Caller holds inst.Gate.</summary>
    private void Reschedule(Instance inst, DateTimeOffset? next)
    {
        inst.Timer?.Dispose();
        inst.Timer = null;
        inst.NextRefresh = null;
        if (!inst.IsActive || next is null || _disposed || inst.Deleted) return;
        var now = _env.Now;
        if (next.Value <= now) next = now + MaxTick;   // a builder that reports a boundary in the past must not spin us
        inst.NextRefresh = next;
        Arm(inst, next.Value - now);
    }

    /// <summary>Caller holds inst.Gate.</summary>
    private void Arm(Instance inst, TimeSpan remaining, bool logIt = true)
    {
        var due = remaining < MinTick ? MinTick : remaining > MaxTick ? MaxTick : remaining;
        var id = inst.Id;
        inst.Timer = _schedule(due, () => OnTimer(id));
        if (logIt) Info($"timer id={id} in {due.TotalSeconds:0}s (boundary in {remaining.TotalSeconds:0}s)");
    }

    private void OnTimer(string id)
    {
        try
        {
            Instance? inst;
            lock (_gate) _widgets.TryGetValue(id, out inst);
            if (inst is null || _disposed) return;
            lock (inst.Gate)
            {
                if (inst.Deleted || !inst.IsActive || inst.Customizing || inst.NextRefresh is not DateTimeOffset next) return;
                var now = _env.Now;
                if (now < next)
                {
                    // coarse tick: boundary not reached yet (or the clock moved) -> just re-arm from the current wall clock
                    inst.Timer?.Dispose();
                    inst.Timer = null;
                    Arm(inst, next - now, logIt: false);   // quiet: this happens every minute
                    return;
                }
            }
            Touch();
            Render(inst);
        }
        catch (Exception ex)
        {
            Info($"timer callback failed id={id}: {ex.GetType().Name}");   // a Timer callback must never throw: it would kill the process
        }
    }

    public void Dispose()
    {
        _disposed = true;
        List<Instance> all;
        lock (_gate) { all = _widgets.Values.ToList(); _widgets.Clear(); }
        foreach (var i in all) { lock (i.Gate) { i.Deleted = true; i.Timer?.Dispose(); i.Timer = null; } }
    }
}
