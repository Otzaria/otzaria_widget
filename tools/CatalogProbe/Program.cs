using System.Runtime.InteropServices;
using System.Xml.Linq;
using Windows.ApplicationModel.AppExtensions;
using Windows.Foundation.Collections;
using Windows.Foundation;
// Copy of spikes/WidgetHostProbe adapted to the real package (Otzaria.Widget_*, CLSID EB0AF4C1-...). Run: dotnet run --project tools/CatalogProbe
// Steps 2/3 are EXPECTED to fail from an unpackaged process (no Hosts API, QI of IWidgetProvider = 0x80073D54): use packaging/run-hostsim.ps1.
// --expand <template.json> [size] [theme]: offline Adaptive Cards template expansion (AdaptiveCards.Templating) with a fake $host.
// Catches template/binding/$when mistakes without needing the Widgets Board. (It does NOT prove the board renders it.)
if (args.Length >= 2 && args[0] == "--expand")
{
    var size = args.Length > 2 ? args[2] : "medium";
    var theme = args.Length > 3 ? args[3] : "dark";
    var data = "{\"count\":3,\"greeting\":\"שלום עולם\",\"showOpen\":\"true\"}";
    var host = $"{{\"widgetSize\":\"{size}\",\"hostTheme\":\"{theme}\",\"isUserContextAuthenticated\":true,\"isSettingsPayload\":false}}";
    var tpl = new AdaptiveCards.Templating.AdaptiveCardTemplate(File.ReadAllText(args[1]));
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    Console.WriteLine(tpl.Expand(new AdaptiveCards.Templating.EvaluationContext(data, host)));
    foreach (var w in tpl.GetLastTemplateExpansionWarnings()) Console.WriteLine("WARNING: " + w);
    return;
}

var clsid = new Guid(args.Length > 0 ? args[0] : "EB0AF4C1-58BE-4FA6-9F1B-88F8188C6326");

Console.WriteLine("== 1. AppExtensionCatalog com.microsoft.windows.widgets");
var catalog = AppExtensionCatalog.Open("com.microsoft.windows.widgets");
var exts = await catalog.FindAllAsync().AsTask();
foreach (var e in exts)
{
    bool ours = e.Package.Id.FamilyName.StartsWith("Otzaria.Widget_", StringComparison.OrdinalIgnoreCase);
    Console.WriteLine($"{(ours ? "*" : " ")} {e.Package.Id.FamilyName} | ext id={e.Id} | {e.DisplayName} | statusOK={e.Package.Status.VerifyIsOK()}");
    if (!ours) continue;
    var props = await e.GetExtensionPropertiesAsync().AsTask();
    if (props.TryGetValue("WidgetProvider", out var wp) && wp is IPropertySet ps)
    {
        Dump(ps, "    ");
    }
    else foreach (var kv in props) Console.WriteLine($"    prop {kv.Key} = {kv.Value}");
}

static void Dump(object o, string indent)
{
    if (o is IPropertySet ps)
        foreach (var kv in ps) { Console.WriteLine($"{indent}{kv.Key}"); Dump(kv.Value, indent + "  "); }
    else if (o is object[] arr) { int i = 0; foreach (var item in arr) { Console.WriteLine($"{indent}[{i++}]"); Dump(item, indent + "  "); } }
    else Console.WriteLine($"{indent}= {o}");
}

Console.WriteLine("\n== 2. Hosts.WidgetCatalog activation from this (unpackaged) process");
foreach (var cls in new[] { "Microsoft.Windows.Widgets.Hosts.WidgetCatalog", "Microsoft.Windows.Widgets.Internal.Hosts.WidgetCatalog" })
{
    int hr = RoGetActivationFactory(cls, typeof(IUnknownGuid).GUID, out var p);
    Console.WriteLine($"{cls}: HRESULT 0x{hr:X8} {(hr == 0 ? "(activatable)" : new System.ComponentModel.Win32Exception(hr).Message)}");
    if (p != IntPtr.Zero) Marshal.Release(p);
}

Console.WriteLine($"\n== 3. CoCreateInstance({clsid}) local server");
var iidUnk = new Guid("00000000-0000-0000-C000-000000000046");
int h = CoCreateInstance(ref clsid, IntPtr.Zero, 0x4 /*LOCAL_SERVER*/, ref iidUnk, out var unk);
Console.WriteLine($"CoCreateInstance(IUnknown): 0x{h:X8}");
if (h == 0)
{
    foreach (var (name, iid) in new[] {
        ("IWidgetProvider",  "5C5774CC-72A0-452D-B9ED-075C0DD25EED"),
        ("IWidgetProvider2", "38C3A963-DD93-479D-9276-04BF84EE1816") })
    {
        var g = new Guid(iid);
        int q = Marshal.QueryInterface(unk, in g, out var itf);
        Console.WriteLine($"QueryInterface({name}): 0x{q:X8}");
        if (itf != IntPtr.Zero) Marshal.Release(itf);
    }
    Marshal.Release(unk);
}
Console.WriteLine("Log: %LOCALAPPDATA%/OtzariaWidget/provider.log (or Packages/Otzaria.Widget_94b2qg0k1ws4g/LocalCache/Local/OtzariaWidget/ when Windows redirected it)");

[DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CharSet = CharSet.Unicode)]
static extern int WindowsCreateString(string src, int len, out IntPtr hstring);
[DllImport("api-ms-win-core-winrt-string-l1-1-0.dll")]
static extern int WindowsDeleteString(IntPtr hstring);
[DllImport("api-ms-win-core-winrt-l1-1-0.dll", EntryPoint = "RoGetActivationFactory")]
static extern int RoGetActivationFactoryRaw(IntPtr activatableClassId, in Guid iid, out IntPtr factory);
static int RoGetActivationFactory(string cls, in Guid iid, out IntPtr f)
{
    WindowsCreateString(cls, cls.Length, out var h);
    try { return RoGetActivationFactoryRaw(h, in iid, out f); } finally { WindowsDeleteString(h); }
}
[DllImport("ole32.dll")]
static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);
[Guid("00000000-0000-0000-C000-000000000046")] interface IUnknownGuid { }

