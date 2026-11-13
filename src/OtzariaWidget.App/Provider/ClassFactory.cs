// Agent D - classic COM class factory handing the Widgets host a CCW of our IWidgetProvider (+IWidgetProvider2).
// Ported from the verified spike (spikes/WidgetSpike/FactoryHelper.cs).
using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;
using WinRT;

namespace OtzariaWidget.App.Provider;

[ComImport, ComVisible(false), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("00000001-0000-0000-C000-000000000046")]
internal interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);

    [PreserveSig]
    int LockServer(bool fLock);
}

[ComVisible(true)]
internal sealed class WidgetProviderFactory<T> : IClassFactory where T : IWidgetProvider
{
    private const int CLASS_E_NOAGGREGATION = unchecked((int)0x80040110);

    // The host may call CreateInstance more than once; hand out ONE provider object so state is shared.
    private readonly Func<T> _supplier;

    public WidgetProviderFactory(Func<T> supplier) { _supplier = supplier; }

    public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
    {
        ppvObject = IntPtr.Zero;
        if (pUnkOuter != IntPtr.Zero) return CLASS_E_NOAGGREGATION;

        IntPtr provider = IntPtr.Zero;
        try
        {
            ProviderLog.Info($"IClassFactory.CreateInstance riid={riid}");
            T inst = _supplier();
            provider = MarshalInspectable<IWidgetProvider>.FromManaged(inst);
            return Marshal.QueryInterface(provider, in riid, out ppvObject);
        }
        catch (Exception ex)
        {
            ProviderLog.Error("CreateInstance", ex);
            return Marshal.GetHRForException(ex);
        }
        finally
        {
            if (provider != IntPtr.Zero) Marshal.Release(provider);
        }
    }

    int IClassFactory.LockServer(bool fLock) => 0;
}
