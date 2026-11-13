namespace OtzariaWidget.App.SettingsUi.Infrastructure;

/// <summary>Named-mutex single instance; a second launch signals the first to come to the front.</summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\OtzariaWidget.Settings.Instance";
    private const string EventName = @"Local\OtzariaWidget.Settings.Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private readonly ManualResetEvent _stop = new(false);
    private Thread? _listener;

    public bool IsPrimary { get; }

    public SingleInstanceGuard()
    {
        _mutex = new Mutex(true, MutexName, out var created);
        IsPrimary = created;
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
    }

    public void SignalPrimary()
    {
        try { _activate.Set(); } catch { }
    }

    public void Listen(Action onActivate)
    {
        _listener = new Thread(() =>
        {
            var handles = new WaitHandle[] { _activate, _stop };
            while (WaitHandle.WaitAny(handles) == 0)
            {
                try { onActivate(); } catch { }
            }
        })
        { IsBackground = true, Name = "SettingsActivationListener" };
        _listener.Start();
    }

    public void Dispose()
    {
        try { _stop.Set(); } catch { }
        if (IsPrimary)
        {
            try { _mutex.ReleaseMutex(); } catch { }
        }
        _mutex.Dispose();
        _activate.Dispose();
    }
}
