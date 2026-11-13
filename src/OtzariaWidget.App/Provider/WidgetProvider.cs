// Agent D - thin WinRT adapter between the Widgets host callbacks and WidgetController.
// Rules: every callback is guarded (one widget can never crash the process), never keep the callback argument objects
// after returning, and keep work short. Callbacks arrive on RPC threads (MTA).
using Microsoft.Windows.Widgets.Providers;
using OtzariaWidget.App.Provider.Cards;

namespace OtzariaWidget.App.Provider;

internal sealed class WidgetProvider : IWidgetProvider, IWidgetProvider2
{
    // ---- lifetime ---------------------------------------------------------------------------------------------
    public static readonly ManualResetEvent ShutdownEvent = new(false);
    private static readonly TimeSpan IdleExit = TimeSpan.FromSeconds(60);
    private static WidgetProvider? _instance;
    private static readonly object InstanceGate = new();

    /// <summary>The single provider object handed to every <c>CreateInstance</c>. Counts as host activity (keeps the process alive).</summary>
    public static WidgetProvider EnsureInstance()
    {
        WidgetProvider p;
        lock (InstanceGate) p = _instance ??= new WidgetProvider();
        p._controller.Touch();
        return p;
    }

    /// <summary>True when the host called us (or activated the class) within <paramref name="window"/>.</summary>
    public static bool HasRecentActivity(TimeSpan window)
    {
        WidgetProvider? p;
        lock (InstanceGate) p = _instance;
        return p is not null && DateTime.UtcNow - p._controller.LastActivityUtc < window;
    }

    public static int CurrentWidgetCount
    {
        get { WidgetProvider? p; lock (InstanceGate) p = _instance; return p?._controller.WidgetCount ?? 0; }
    }

    public static void DisposeInstance()
    {
        WidgetProvider? p;
        lock (InstanceGate) { p = _instance; _instance = null; }
        p?.Dispose();
    }

    // ---- state ------------------------------------------------------------------------------------------------
    private readonly ProviderServices _services;
    private readonly WidgetController _controller;
    private readonly Timer _idleTimer;

    private sealed class HostSink : IWidgetSink
    {
        public void Update(string widgetId, string template, string data, string customState)
        {
            var opts = new WidgetUpdateRequestOptions(widgetId) { Template = template, Data = data, CustomState = customState };
            WidgetManager.GetDefault().UpdateWidget(opts);
        }
    }

    private WidgetProvider()
    {
        _services = ProviderServices.CreateDefault();
        _controller = new WidgetController(_services, new HostSink(), LookupCustomState) { Log = ProviderLog.Info };
        _services.SourceChanged += src => Task.Run(() => Guard("SourceChanged", () => _controller.OnSourceChanged(src)));

        // Recover widgets pinned before this process (re)started. Off the activation path: rendering reads files.
        Task.Run(() => RecoverWithRetry());

        // Exit only when no widgets exist AND nothing has happened for a while. With pinned widgets the process stays up.
        _idleTimer = new Timer(_ =>
        {
            try
            {
                bool idle = _controller.WidgetCount == 0 && DateTime.UtcNow - _controller.LastActivityUtc > IdleExit;
                if (idle) { ProviderLog.Info("idle with no widgets -> requesting shutdown"); ShutdownEvent.Set(); }
            }
            catch (Exception ex) { ProviderLog.Error("idle timer", ex); }   // an exception escaping a Timer callback would kill the process
        }, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    private void Dispose()
    {
        _idleTimer.Dispose();
        _controller.Dispose();
        _services.Dispose();
    }

    private static string? LookupCustomState(string widgetId)
    {
        foreach (var i in WidgetManager.GetDefault().GetWidgetInfos() ?? Array.Empty<WidgetInfo>())
            if (i?.WidgetContext is { } c && c.Id == widgetId) return i.CustomState;
        return null;
    }

    /// <summary>The host may not be ready to answer right after our process was started by COM: retry a few times before giving up
    /// (later host callbacks still register widgets lazily, so this is only about restoring the pinned ones early).</summary>
    private void RecoverWithRetry()
    {
        foreach (var delay in new[] { 0, 3, 15 })
        {
            if (delay > 0) Thread.Sleep(TimeSpan.FromSeconds(delay));
            try { _controller.Touch(); RecoverWidgets(); return; }
            catch (Exception ex) { ProviderLog.Error($"RecoverWidgets (retry in {delay}s)", ex); }
        }
    }

    private void RecoverWidgets()
    {
        var infos = WidgetManager.GetDefault().GetWidgetInfos() ?? Array.Empty<WidgetInfo>();   // null when the host has none for us
        ProviderLog.Info($"RecoverWidgets: host reports {infos.Length} widget(s)");
        var known = new List<(WidgetRef, string?)>();
        foreach (var i in infos)
        {
            try
            {
                if (i?.WidgetContext is null) continue;
                known.Add((ToRef(i.WidgetContext), i.CustomState));   // CustomState may be null/empty: Parse falls back to defaults
            }
            catch (Exception ex) { ProviderLog.Error("RecoverWidgets(one widget)", ex); }
        }
        _controller.Recover(known);
    }

    private static WidgetRef ToRef(WidgetContext c) =>
        new(c.Id, c.DefinitionId, CardSizes.Parse(c.Size.ToString()), c.IsActive);

    private void Guard(string name, Action action)
    {
        try { _controller.Touch(); action(); }
        catch (Exception ex) { ProviderLog.Error(name, ex); }   // one widget / one callback can never take the process down
    }

    // ---- IWidgetProvider --------------------------------------------------------------------------------------
    public void CreateWidget(WidgetContext widgetContext)
    {
        Guard(nameof(CreateWidget), () =>
        {
            var w = ToRef(widgetContext);
            ProviderLog.Info($"CreateWidget id={w.Id} kind={w.DefinitionId} size={w.Size}");
            _controller.Create(w);
        });
    }

    public void DeleteWidget(string widgetId, string customState)
    {
        Guard(nameof(DeleteWidget), () =>
        {
            ProviderLog.Info($"DeleteWidget id={widgetId}");
            _controller.Delete(widgetId);
        });
    }

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        // Copy everything we need now; the args object must not be used after return.
        WidgetRef w; string verb; string? data;
        try { w = ToRef(actionInvokedArgs.WidgetContext); verb = actionInvokedArgs.Verb; data = actionInvokedArgs.Data; }
        catch (Exception ex) { ProviderLog.Error("OnActionInvoked(read args)", ex); return; }
        Guard(nameof(OnActionInvoked), () =>
        {
            ProviderLog.Info($"OnActionInvoked id={w.Id} verb={verb}");
            _controller.Action(w, verb, data);
        });
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
    {
        WidgetRef w;
        try { w = ToRef(contextChangedArgs.WidgetContext); }
        catch (Exception ex) { ProviderLog.Error("OnWidgetContextChanged(read args)", ex); return; }
        Guard(nameof(OnWidgetContextChanged), () =>
        {
            ProviderLog.Info($"OnWidgetContextChanged id={w.Id} size={w.Size} active={w.IsActive}");
            _controller.ContextChanged(w);
        });
    }

    public void Activate(WidgetContext widgetContext)
    {
        Guard(nameof(Activate), () =>
        {
            var w = ToRef(widgetContext);
            ProviderLog.Info($"Activate id={w.Id} size={w.Size}");
            _controller.Activate(w);
        });
    }

    public void Deactivate(string widgetId)
    {
        Guard(nameof(Deactivate), () =>
        {
            ProviderLog.Info($"Deactivate id={widgetId}");
            _controller.Deactivate(widgetId);
        });
    }

    // ---- IWidgetProvider2 -------------------------------------------------------------------------------------
    public void OnCustomizationRequested(WidgetCustomizationRequestedArgs customizationRequestedArgs)
    {
        WidgetRef w;
        try { w = ToRef(customizationRequestedArgs.WidgetContext); }
        catch (Exception ex) { ProviderLog.Error("OnCustomizationRequested(read args)", ex); return; }
        Guard(nameof(OnCustomizationRequested), () =>
        {
            ProviderLog.Info($"OnCustomizationRequested id={w.Id}");
            _controller.CustomizationRequested(w);
        });
    }
}
