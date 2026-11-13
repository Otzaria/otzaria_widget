// HostSim - plays the Widgets host against the real provider COM server (OtzariaWidget.exe -RegisterProcessAsComServer).
//
//   * CoCreateInstance(CLSID_Provider, LOCAL_SERVER, IID_IWidgetProvider) -> the packaged exe is started by COM, exactly as the host does.
//   * QI IWidgetProvider2, then drives CreateWidget / Activate / OnWidgetContextChanged / OnActionInvoked / OnCustomizationRequested /
//     Deactivate / DeleteWidget with hand-made WinRT objects (IWidgetContext, IWidgetActionInvokedArgs, ... implemented below with raw
//     vtables through ComWrappers, so no WinRT projection is needed).
//   * Each call prints its HRESULT. The provider's own log (provider.log) is what proves the behaviour.
//
// MUST run inside the package identity (an unpackaged process cannot QI IWidgetProvider: 0x80073D54):
//   packaging\run-hostsim.ps1   (Invoke-CommandInDesktopPackage)
//
// Widget ids are "hostsim-*": the provider will call WidgetManager.UpdateWidget for them and the real Widgets service rejects/ignores
// unknown ids (verified: UpdateWidget returns normally and GetWidgetInfos still reports 0 widgets afterwards); nothing is created on the board.
using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace HostSim;

internal static unsafe class Program
{
    static readonly Guid CLSID_Provider = new("EB0AF4C1-58BE-4FA6-9F1B-88F8188C6326");
    static readonly Guid IID_IWidgetProvider = new("5C5774CC-72A0-452D-B9ED-075C0DD25EED");
    static readonly Guid IID_IWidgetProvider2 = new("38C3A963-DD93-479D-9276-04BF84EE1816");
    static readonly Guid IID_IWidgetContext = new("903C518B-40BC-5BC6-88F7-AF9D81C0CDC1");
    static readonly Guid IID_IWidgetActionInvokedArgs = new("C593CC57-04B9-52CA-88AD-46FEA21EA340");
    static readonly Guid IID_IWidgetContextChangedArgs = new("2C226D54-2252-576B-A197-370B28D25C2F");
    static readonly Guid IID_IWidgetCustomizationRequestedArgs = new("41DEA311-DD9B-5B8B-B493-3A30552116B8");
    static readonly Guid IID_IInspectable = new("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90");

    const int Small = 0, Medium = 1, Large = 2;

    [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr reserved, uint coInit);
    [DllImport("ole32.dll")] static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint ctx, ref Guid iid, out IntPtr obj);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetCurrentPackageFullName(ref uint len, StringBuilder name);
    [DllImport("combase.dll")] static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string s, uint len, out IntPtr h);
    [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr h);

    static StreamWriter? _out;
    static void Say(string s)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} HOSTSIM {s}";
        Console.WriteLine(line);
        lock (_out!) { _out.WriteLine(line); _out.Flush(); }
    }

    static IntPtr HStr(string s) { WindowsCreateString(s, (uint)s.Length, out var h); return h; }

    // ------------------------------------------------------------------------------------------------ fake WinRT objects
    sealed record Ctx(string Id, string Definition, int Size, bool Active);
    sealed record ActionArgs(Ctx Context, string Verb, string Data, string CustomState);
    sealed record ChangedArgs(Ctx Context);
    sealed record CustomizationArgs(Ctx Context, string CustomState);

    sealed class Wrappers : ComWrappers
    {
        public static readonly Wrappers Instance = new();
        private static ComInterfaceEntry* _ctxEntries, _actionEntries, _changedEntries, _custEntries;

        static void** MakeVtable(params IntPtr[] methods)
        {
            GetIUnknownImpl(out var qi, out var addRef, out var release);
            var v = (void**)NativeMemory.Alloc((nuint)((3 + 3 + methods.Length) * sizeof(void*)));
            v[0] = (void*)qi; v[1] = (void*)addRef; v[2] = (void*)release;
            v[3] = (void*)(delegate* unmanaged[Stdcall]<IntPtr, ulong*, Guid**, int>)&GetIids;
            v[4] = (void*)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)&GetRuntimeClassName;
            v[5] = (void*)(delegate* unmanaged[Stdcall]<IntPtr, int*, int>)&GetTrustLevel;
            for (int i = 0; i < methods.Length; i++) v[6 + i] = (void*)methods[i];
            return v;
        }

        static ComInterfaceEntry* MakeEntries(Guid iid, void** vtbl)
        {
            var e = (ComInterfaceEntry*)NativeMemory.Alloc((nuint)(2 * sizeof(ComInterfaceEntry)));
            e[0] = new ComInterfaceEntry { IID = iid, Vtable = (IntPtr)vtbl };
            GetIUnknownImpl(out var qi, out var addRef, out var release);
            var inspOnly = (void**)NativeMemory.Alloc((nuint)(6 * sizeof(void*)));
            inspOnly[0] = (void*)qi; inspOnly[1] = (void*)addRef; inspOnly[2] = (void*)release;
            inspOnly[3] = vtbl[3]; inspOnly[4] = vtbl[4]; inspOnly[5] = vtbl[5];
            e[1] = new ComInterfaceEntry { IID = IID_IInspectable, Vtable = (IntPtr)inspOnly };
            return e;
        }

        static Wrappers()
        {
            _ctxEntries = MakeEntries(IID_IWidgetContext, MakeVtable(
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)&Ctx_Id,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)&Ctx_DefinitionId,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, int*, int>)&Ctx_Size,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, byte*, int>)&Ctx_IsActive));
            _actionEntries = MakeEntries(IID_IWidgetActionInvokedArgs, MakeVtable(
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)&Action_WidgetContext,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)&Action_Verb,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)&Action_Data,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)&Action_CustomState));
            _changedEntries = MakeEntries(IID_IWidgetContextChangedArgs, MakeVtable(
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)&Changed_WidgetContext));
            _custEntries = MakeEntries(IID_IWidgetCustomizationRequestedArgs, MakeVtable(
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)&Cust_WidgetContext,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)&Cust_CustomState));
        }

        protected override ComInterfaceEntry* ComputeVtables(object obj, CreateComInterfaceFlags flags, out int count)
        {
            count = 2;
            return obj switch
            {
                Ctx => _ctxEntries,
                ActionArgs => _actionEntries,
                ChangedArgs => _changedEntries,
                CustomizationArgs => _custEntries,
                _ => throw new NotSupportedException(),
            };
        }

        protected override object? CreateObject(IntPtr externalComObject, CreateObjectFlags flags) => throw new NotSupportedException();
        protected override void ReleaseObjects(IEnumerable objects) { }
    }

    // IInspectable
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static int GetIids(IntPtr self, ulong* count, Guid** iids) { *count = 0; *iids = null; return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static int GetRuntimeClassName(IntPtr self, IntPtr* name) { *name = HStr("HostSim.FakeObject"); return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static int GetTrustLevel(IntPtr self, int* level) { *level = 0; return 0; }

    static T Self<T>(IntPtr p) where T : class => ComWrappers.ComInterfaceDispatch.GetInstance<T>((ComWrappers.ComInterfaceDispatch*)p);

    // IWidgetContext
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])] static int Ctx_Id(IntPtr s, IntPtr* r) { *r = HStr(Self<Ctx>(s).Id); return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])] static int Ctx_DefinitionId(IntPtr s, IntPtr* r) { *r = HStr(Self<Ctx>(s).Definition); return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])] static int Ctx_Size(IntPtr s, int* r) { *r = Self<Ctx>(s).Size; return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])] static int Ctx_IsActive(IntPtr s, byte* r) { *r = (byte)(Self<Ctx>(s).Active ? 1 : 0); return 0; }
    // IWidgetActionInvokedArgs
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])] static int Action_WidgetContext(IntPtr s, IntPtr* r) { *r = Iface(Self<ActionArgs>(s).Context, IID_IWidgetContext); return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])] static int Action_Verb(IntPtr s, IntPtr* r) { *r = HStr(Self<ActionArgs>(s).Verb); return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])] static int Action_Data(IntPtr s, IntPtr* r) { *r = HStr(Self<ActionArgs>(s).Data); return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])] static int Action_CustomState(IntPtr s, IntPtr* r) { *r = HStr(Self<ActionArgs>(s).CustomState); return 0; }
    // IWidgetContextChangedArgs
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])] static int Changed_WidgetContext(IntPtr s, IntPtr* r) { *r = Iface(Self<ChangedArgs>(s).Context, IID_IWidgetContext); return 0; }
    // IWidgetCustomizationRequestedArgs
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])] static int Cust_WidgetContext(IntPtr s, IntPtr* r) { *r = Iface(Self<CustomizationArgs>(s).Context, IID_IWidgetContext); return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])] static int Cust_CustomState(IntPtr s, IntPtr* r) { *r = HStr(Self<CustomizationArgs>(s).CustomState); return 0; }

    /// <summary>An AddRef'd interface pointer of the CCW for <paramref name="obj"/> (caller releases).</summary>
    static IntPtr Iface(object obj, Guid iid)
    {
        var unk = Wrappers.Instance.GetOrCreateComInterfaceForObject(obj, CreateComInterfaceFlags.None);
        try { Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unk, in iid, out var p)); return p; }
        finally { Marshal.Release(unk); }
    }

    // ------------------------------------------------------------------------------------------------ vtable calls
    static int V1(IntPtr obj, int slot, IntPtr a) => ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)(*(*(void***)obj + slot)))(obj, a);
    static int V2(IntPtr obj, int slot, IntPtr a, IntPtr b) => ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, int>)(*(*(void***)obj + slot)))(obj, a, b);

    static IntPtr _provider, _provider2;
    static int _failures;

    static void Step(string name, Func<int> call)
    {
        int hr;
        try { hr = call(); } catch (Exception ex) { hr = ex.HResult; Say($"{name}: EXCEPTION {ex.GetType().Name} {ex.Message}"); }
        Say($"{name} -> 0x{hr:X8}{(hr == 0 ? "" : "  <-- FAILED")}");
        if (hr != 0) _failures++;
    }

    static void CreateWidget(Ctx c) => Step($"CreateWidget {c.Id} {c.Definition}", () => { var p = Iface(c, IID_IWidgetContext); try { return V1(_provider, 6, p); } finally { Marshal.Release(p); } });
    static void Activate(Ctx c) => Step($"Activate {c.Id}", () => { var p = Iface(c, IID_IWidgetContext); try { return V1(_provider, 10, p); } finally { Marshal.Release(p); } });
    static void Deactivate(string id) => Step($"Deactivate {id}", () => { var h = HStr(id); try { return V1(_provider, 11, h); } finally { WindowsDeleteString(h); } });
    static void Delete(string id, string state) => Step($"DeleteWidget {id}", () =>
    {
        var a = HStr(id); var b = HStr(state);
        try { return V2(_provider, 7, a, b); } finally { WindowsDeleteString(a); WindowsDeleteString(b); }
    });
    static void ContextChanged(Ctx c) => Step($"OnWidgetContextChanged {c.Id} size={c.Size}", () => { var p = Iface(new ChangedArgs(c), IID_IWidgetContextChangedArgs); try { return V1(_provider, 9, p); } finally { Marshal.Release(p); } });
    static void Action(Ctx c, string verb, string data, string state = "") => Step($"OnActionInvoked {c.Id} verb={verb}", () => { var p = Iface(new ActionArgs(c, verb, data, state), IID_IWidgetActionInvokedArgs); try { return V1(_provider, 8, p); } finally { Marshal.Release(p); } });
    static void Customize(Ctx c) => Step($"OnCustomizationRequested {c.Id}", () => { var p = Iface(new CustomizationArgs(c, ""), IID_IWidgetCustomizationRequestedArgs); try { return V1(_provider2, 6, p); } finally { Marshal.Release(p); } });

    static void Pause(int ms) => Thread.Sleep(ms);

    public static int Main(string[] args)
    {
        var outPath = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "hostsim.out.txt");
        _out = new StreamWriter(outPath, append: false, new UTF8Encoding(false));
        bool waitIdle = args.Contains("--wait-idle");
        bool keep = args.Contains("--keep-provider");
        string scenario = args.FirstOrDefault(a => a.StartsWith("--scenario=", StringComparison.Ordinal))?[11..] ?? "full";

        Say($"start scenario={scenario} pid={Environment.ProcessId}");
        try
        {
            // package identity?
            uint len = 512; var sb = new StringBuilder(512);
            int pr = GetCurrentPackageFullName(ref len, sb);
            Say(pr == 0 ? "package identity: " + sb : $"NO package identity (GetCurrentPackageFullName -> {pr})");
        }
        catch { }

        CoInitializeEx(IntPtr.Zero, 0);   // MTA, like the host's worker threads

        var clsid = CLSID_Provider; var iid = IID_IWidgetProvider;
        int hr = CoCreateInstance(ref clsid, IntPtr.Zero, 0x4 /*CLSCTX_LOCAL_SERVER*/, ref iid, out _provider);
        Say($"CoCreateInstance(provider, IWidgetProvider) -> 0x{hr:X8}");
        if (hr != 0) { Say("ABORT (no package identity? provider not registered?)"); return 2; }
        var iid2 = IID_IWidgetProvider2;
        hr = Marshal.QueryInterface(_provider, in iid2, out _provider2);
        Say($"QueryInterface(IWidgetProvider2) -> 0x{hr:X8}");
        if (hr != 0) { _failures++; }

        var zmanim = new Ctx("hostsim-zmanim", "Zmanim", Medium, false);
        var recent = new Ctx("hostsim-recent", "RecentBooks", Large, true);

        if (scenario is "full")
        {
            // 1. host pins a widget: CreateWidget, then Activate when it becomes visible
            CreateWidget(zmanim); Pause(1500);
            CreateWidget(recent); Pause(1500);
            Activate(zmanim with { Active = true }); Pause(1500);
            // 2. resize
            ContextChanged(zmanim with { Size = Large, Active = true }); Pause(1000);
            // 3. card buttons: next/prev paging, refresh, rejected/unknown links and verbs
            var z = zmanim with { Size = Large, Active = true };
            Action(recent, "next", "{}"); Pause(600);
            Action(recent, "prev", "{}"); Pause(600);
            Action(recent, "refresh", "{}"); Pause(600);
            Action(recent, "open", "{\"url\":\"https://evil.example/x\"}"); Pause(300);
            Action(recent, "open", "{\"url\":\"otzaria://plugin/install?url=http://x\"}"); Pause(300);
            Action(recent, "bogusVerb", "{}"); Pause(300);
            // 4. customization round trip (IWidgetProvider2)
            Customize(recent); Pause(1000);
            Action(recent, "saveSettings", "{\"count\":\"3\",\"showSearches\":\"false\",\"showLocation\":\"true\",\"includePdfs\":\"true\"}"); Pause(1000);
            Customize(recent); Pause(600);
            Action(recent, "exitCustomization", "{}"); Pause(600);
            // 5. deactivate / reactivate (Activate must refresh)
            Deactivate("hostsim-zmanim"); Pause(600);
            Activate(z); Pause(1000);
            // 6. delete, then a late callback for the deleted widget must not resurrect it
            Delete("hostsim-zmanim", "{}"); Pause(600);
            Activate(z); Pause(600);
            Action(z, "refresh", "{}"); Pause(600);
            Delete("hostsim-recent", "{}"); Pause(600);
        }
        else if (scenario is "settings")
        {
            CreateWidget(zmanim); Pause(800);
            Action(zmanim, "settings", "{}"); Pause(3000);
            Delete(zmanim.Id, "{}"); Pause(500);
        }
        else if (scenario is "keep")
        {
            // leave one widget pinned (from the provider's point of view) to prove the process stays alive past the idle window
            CreateWidget(zmanim); Pause(800);
            Activate(zmanim with { Active = true });
        }

        Say($"sequence finished, failures={_failures}");

        if (!keep)
        {
            Marshal.Release(_provider2); Marshal.Release(_provider);   // the host releasing its references
            Say("released provider references");
        }
        if (waitIdle)
        {
            // observe the idle exit (60 s without widgets): is the process gone?
            for (int i = 0; i < 90; i++)
            {
                Thread.Sleep(1000);
                if (System.Diagnostics.Process.GetProcessesByName("OtzariaWidget").Length == 0) { Say($"provider process gone after {i + 1}s"); break; }
                if (i == 89) Say("provider process still alive after 90 s");
            }
        }
        Say("done");
        return _failures == 0 ? 0 : 1;
    }
}
