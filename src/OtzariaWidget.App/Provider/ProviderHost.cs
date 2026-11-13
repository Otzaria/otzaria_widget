// Agent D - COM server host for the widget provider (mode "-RegisterProcessAsComServer").
// Lifetime: alive while widgets exist; exits only after ~60 s with no widgets AND no host activity (see WidgetProvider.IdleExit).
// The host may release and re-activate the provider at any time while widgets stay pinned, so "no callbacks for a while" alone never
// ends the process, and a re-activation that races with the shutdown cancels it (grace period below).
using System.Runtime.InteropServices;
using OtzariaWidget.App.Provider.Cards;

namespace OtzariaWidget.App.Provider;

public static class ProviderHost
{
    private const uint CLSCTX_LOCAL_SERVER = 0x4;
    private const uint REGCLS_MULTIPLEUSE = 0x1;
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(2);

    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rclsid,
        [MarshalAs(UnmanagedType.IUnknown)] object pUnk,
        uint dwClsContext, uint flags, out uint lpdwRegister);

    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(uint dwRegister);

    /// <summary>Runs until the provider decides to shut down. Called from Program.Main on an MTA thread.</summary>
    public static int Run(string[] args)
    {
        ProviderLog.Info($"=== provider starting (args: {string.Join(' ', args)}) v{typeof(ProviderHost).Assembly.GetName().Version}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ProviderLog.Warn("UNHANDLED (terminating=" + e.IsTerminating + "): " + (e.ExceptionObject is Exception ex ? ProviderLog.Describe(ex) + Environment.NewLine + ex.StackTrace : "non-exception"));
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ProviderLog.Warn("UNOBSERVED TASK: " + ProviderLog.Describe(e.Exception.GetBaseException()));
            e.SetObserved();
        };
        // OS shutdown / logoff / taskkill without /f: stop timers and the file watchers cleanly.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            ProviderLog.Info("process exit");
            WidgetProvider.ShutdownEvent.Set();
            WidgetProvider.DisposeInstance();
        };

        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();

            var factory = new WidgetProviderFactory<WidgetProvider>(WidgetProvider.EnsureInstance);
            uint cookie = Register(factory);

            // Create the provider eagerly so widgets pinned earlier are recovered (GetWidgetInfos) even if the host
            // does not call CreateInstance again, and so we know whether we have to stay alive.
            WidgetProvider.EnsureInstance();

            while (true)
            {
                WidgetProvider.ShutdownEvent.WaitOne();
                ProviderLog.Info("shutdown requested; revoking class object");
                int rhr = CoRevokeClassObject(cookie);
                if (rhr < 0) ProviderLog.Warn($"CoRevokeClassObject failed 0x{rhr:X8}");

                // An activation (or a callback on an already-connected proxy) may have raced with the idle decision: if the host talked to us
                // during the grace period, cancel the shutdown and become available again.
                Thread.Sleep(ShutdownGrace);
                if (WidgetProvider.HasRecentActivity(ShutdownGrace + TimeSpan.FromSeconds(1)) || WidgetProvider.CurrentWidgetCount > 0)
                {
                    ProviderLog.Info("activity during shutdown grace period -> staying alive");
                    WidgetProvider.ShutdownEvent.Reset();
                    cookie = Register(factory);
                    continue;
                }
                break;
            }

            WidgetProvider.DisposeInstance();
            GC.KeepAlive(factory);
            return 0;
        }
        catch (Exception ex)
        {
            ProviderLog.Error("ProviderHost.Run", ex);
            return 1;
        }
    }

    private static uint Register(object factory)
    {
        int hr = CoRegisterClassObject(ProviderIds.Clsid, factory, CLSCTX_LOCAL_SERVER, REGCLS_MULTIPLEUSE, out uint cookie);
        Marshal.ThrowExceptionForHR(hr);
        ProviderLog.Info($"CoRegisterClassObject OK (cookie {cookie})");
        return cookie;
    }
}
