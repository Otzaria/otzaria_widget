using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Windows.Widgets.Providers;

namespace WidgetSpike;

/// <summary>Per-widget state. Serialised to WidgetUpdateRequestOptions.CustomState (survives provider/host restarts).</summary>
internal sealed class WidgetState
{
    public string? WidgetId { get; set; }
    public string? DefinitionId { get; set; }
    public int Count { get; set; }
    public string Greeting { get; set; } = "שלום עולם";
    public bool ShowOpen { get; set; } = true;

    // Not persisted:
    public bool InCustomization { get; set; }
    public bool IsActive { get; set; }

    public string ToCustomState() => JsonSerializer.Serialize(new { count = Count, greeting = Greeting, showOpen = ShowOpen });

    public void LoadCustomState(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            var n = JsonNode.Parse(json)?.AsObject();
            if (n is null) return;
            if (n["count"] is JsonNode c) Count = c.GetValue<int>();
            if (n["greeting"] is JsonNode g) Greeting = g.GetValue<string>();
            if (n["showOpen"] is JsonNode s) ShowOpen = s.GetValue<bool>();
        }
        catch (Exception ex) { Log.Write($"LoadCustomState failed for '{json}': {ex.Message}"); }
    }
}

internal sealed class WidgetProvider : IWidgetProvider, IWidgetProvider2
{
    // ---- lifetime ---------------------------------------------------------------------------------------------
    public static readonly ManualResetEvent ShutdownEvent = new(false);
    private static readonly TimeSpan IdleExit = TimeSpan.FromSeconds(60);
    private static WidgetProvider? _instance;
    private static readonly object InstanceGate = new();

    public static WidgetProvider EnsureInstance()
    {
        lock (InstanceGate) return _instance ??= new WidgetProvider();
    }

    private readonly Dictionary<string, WidgetState> _widgets = new();
    private readonly object _gate = new();
    private DateTime _lastActivity = DateTime.UtcNow;
    private readonly Timer _idleTimer;

    // Cached templates (shipped next to the exe in Templates\)
    private static readonly string MainTemplate = LoadTemplate("main.json");
    private static readonly string SettingsTemplate = LoadTemplate("settings.json");
    private static string LoadTemplate(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Templates", name));

    public WidgetProvider()
    {
        lock (InstanceGate) _instance ??= this;
        RecoverWidgets();
        // Only exit when no widgets exist AND nothing has happened for a while. With pinned widgets the process stays up.
        _idleTimer = new Timer(_ =>
        {
            bool idle;
            lock (_gate) idle = _widgets.Count == 0 && DateTime.UtcNow - _lastActivity > IdleExit;
            if (idle) { Log.Write("Idle with no widgets -> requesting shutdown"); ShutdownEvent.Set(); }
        }, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    /// <summary>Rebuild our in-memory map from the widget host's record (incl. CustomState) after a restart/crash.</summary>
    private void RecoverWidgets()
    {
        try
        {
            var infos = WidgetManager.GetDefault().GetWidgetInfos() ?? Array.Empty<WidgetInfo>(); // null when the host has no widgets for us
            Log.Write($"RecoverWidgets: host reports {infos.Length} widget(s)");
            lock (_gate)
            {
                foreach (var info in infos)
                {
                    var ctx = info.WidgetContext;
                    var st = new WidgetState { WidgetId = ctx.Id, DefinitionId = ctx.DefinitionId };
                    st.LoadCustomState(info.CustomState);
                    _widgets[ctx.Id] = st;
                    Log.Write($"  recovered id={ctx.Id} def={ctx.DefinitionId} size={ctx.Size} active={ctx.IsActive} customState='{info.CustomState}'");
                }
            }
        }
        catch (Exception ex) { Log.Error("RecoverWidgets", ex); }
    }

    private void Touch() => _lastActivity = DateTime.UtcNow;

    private WidgetState GetOrAdd(WidgetContext ctx)
    {
        lock (_gate)
        {
            if (!_widgets.TryGetValue(ctx.Id, out var st))
            {
                // Host called us for a widget we do not know (e.g. recovered lazily) - rebuild from the host record.
                st = new WidgetState { WidgetId = ctx.Id, DefinitionId = ctx.DefinitionId };
                try
                {
                    foreach (var i in WidgetManager.GetDefault().GetWidgetInfos() ?? Array.Empty<WidgetInfo>())
                        if (i.WidgetContext.Id == ctx.Id) { st.LoadCustomState(i.CustomState); break; }
                }
                catch (Exception ex) { Log.Error("GetOrAdd/GetWidgetInfos", ex); }
                _widgets[ctx.Id] = st;
            }
            return st;
        }
    }

    // ---- IWidgetProvider --------------------------------------------------------------------------------------
    public void CreateWidget(WidgetContext widgetContext)
    {
        Guard(nameof(CreateWidget), () =>
        {
            Log.Write($"CreateWidget id={widgetContext.Id} def={widgetContext.DefinitionId} size={widgetContext.Size}");
            var st = GetOrAdd(widgetContext);
            Update(st);
        });
    }

    public void DeleteWidget(string widgetId, string customState)
    {
        Guard(nameof(DeleteWidget), () =>
        {
            Log.Write($"DeleteWidget id={widgetId} customState='{customState}'");
            lock (_gate) _widgets.Remove(widgetId);
        });
    }

    public void OnActionInvoked(WidgetActionInvokedArgs args)
    {
        Guard(nameof(OnActionInvoked), () =>
        {
            var ctx = args.WidgetContext;
            Log.Write($"OnActionInvoked id={ctx.Id} verb={args.Verb} data='{args.Data}'");
            var st = GetOrAdd(ctx);
            switch (args.Verb)
            {
                case "inc":
                    lock (_gate) st.Count++;
                    break;

                case "saveSettings":
                    // args.Data is the JSON of all Input.* values: {"greeting":"...","showOpen":"true"}
                    try
                    {
                        var n = JsonNode.Parse(args.Data)?.AsObject();
                        lock (_gate)
                        {
                            if (n?["greeting"] is JsonNode g) st.Greeting = g.ToString();
                            if (n?["showOpen"] is JsonNode s) st.ShowOpen = s.ToString().Equals("true", StringComparison.OrdinalIgnoreCase);
                            st.InCustomization = false;
                        }
                    }
                    catch (Exception ex) { Log.Error("saveSettings parse", ex); st.InCustomization = false; }
                    break;

                case "exitCustomization":
                    st.InCustomization = false;
                    break;

                default:
                    Log.Write($"  (unknown verb '{args.Verb}')");
                    break;
            }
            Update(st);
        });
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs args)
    {
        Guard(nameof(OnWidgetContextChanged), () =>
        {
            var ctx = args.WidgetContext;
            Log.Write($"OnWidgetContextChanged id={ctx.Id} size={ctx.Size} active={ctx.IsActive}");
            Update(GetOrAdd(ctx));
        });
    }

    public void Activate(WidgetContext widgetContext)
    {
        Guard(nameof(Activate), () =>
        {
            Log.Write($"Activate id={widgetContext.Id} size={widgetContext.Size}");
            var st = GetOrAdd(widgetContext);
            st.IsActive = true;
            Update(st);
        });
    }

    public void Deactivate(string widgetId)
    {
        Guard(nameof(Deactivate), () =>
        {
            Log.Write($"Deactivate id={widgetId}");
            lock (_gate) { if (_widgets.TryGetValue(widgetId, out var st)) st.IsActive = false; }
        });
    }

    // ---- IWidgetProvider2 -------------------------------------------------------------------------------------
    public void OnCustomizationRequested(WidgetCustomizationRequestedArgs args)
    {
        Guard(nameof(OnCustomizationRequested), () =>
        {
            Log.Write($"OnCustomizationRequested id={args.WidgetContext.Id}");
            var st = GetOrAdd(args.WidgetContext);
            st.InCustomization = true;
            Update(st);
        });
    }

    // ---- helpers ----------------------------------------------------------------------------------------------
    private void Update(WidgetState st)
    {
        string template, data, customState;
        lock (_gate)
        {
            template = st.InCustomization ? SettingsTemplate : MainTemplate;
            data = JsonSerializer.Serialize(new
            {
                count = st.Count,
                greeting = st.Greeting,
                showOpen = st.ShowOpen ? "true" : "false",
            });
            customState = st.ToCustomState();
        }
        var opts = new WidgetUpdateRequestOptions(st.WidgetId)
        {
            Template = template,
            Data = data,
            CustomState = customState,
        };
        WidgetManager.GetDefault().UpdateWidget(opts);
        Log.Write($"  UpdateWidget id={st.WidgetId} custom={st.InCustomization} state={customState} (template {template.Length}B, data {data.Length}B)");
    }

    private void Guard(string name, Action a)
    {
        try { Touch(); a(); }
        catch (Exception ex) { Log.Error(name, ex); }
    }
}
