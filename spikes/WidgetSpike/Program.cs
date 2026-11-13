using System.Runtime.InteropServices;
using COM;
using WidgetSpike;

internal static class Program
{
    // Must match: Package.appxmanifest (com:Class Id AND CreateInstance ClassId).
    private static readonly Guid CLSID_WidgetProvider = new("6C3B7E0A-2F4D-4E8B-9A51-0D7B7A5E1C42");

    private const uint CLSCTX_LOCAL_SERVER = 0x4;
    private const uint REGCLS_MULTIPLEUSE = 0x1;

    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rclsid,
        [MarshalAs(UnmanagedType.IUnknown)] object pUnk,
        uint dwClsContext, uint flags, out uint lpdwRegister);

    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(uint dwRegister);

    // No [STAThread]: stay in the MTA so COM callbacks from the host are not serialised on a message loop we do not pump.
    public static int Main(string[] args)
    {
        Log.Write($"=== WidgetSpike starting. args=[{string.Join(' ', args)}] cmdline={Environment.CommandLine}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write($"UNHANDLED: {e.ExceptionObject}");

        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();

            var factory = new WidgetProviderFactory<WidgetProvider>(WidgetProvider.EnsureInstance);
            int hr = CoRegisterClassObject(CLSID_WidgetProvider, factory, CLSCTX_LOCAL_SERVER, REGCLS_MULTIPLEUSE, out uint cookie);
            Marshal.ThrowExceptionForHR(hr);
            Log.Write($"CoRegisterClassObject OK, cookie={cookie}");

            // Create the provider eagerly so pinned widgets are recovered (GetWidgetInfos) even before the host
            // calls CreateInstance, and so we know whether to stay alive.
            WidgetProvider.EnsureInstance();

            // Stay alive while widgets exist; exit after a quiet period with no widgets.
            WidgetProvider.ShutdownEvent.WaitOne();
            Log.Write("Shutdown event set; revoking class object");

            int rhr = CoRevokeClassObject(cookie);
            if (rhr < 0) Log.Write($"CoRevokeClassObject failed 0x{rhr:X8}");
            GC.KeepAlive(factory);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("Main", ex);
            return 1;
        }
    }
}
