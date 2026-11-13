# Windows 11 Widgets Board - widget provider platform research

Spike code: `spikes/WidgetSpike` (provider + MSIX), `spikes/WidgetHostProbe` (diagnostic console).
Tested on Windows 11 build 26300, Widgets host `MicrosoftWindows.Client.WebExperience 526.28801.60.0`
(+ `Microsoft.WidgetsPlatformRuntime 1.6.19.0`), .NET SDK 10.0.404 targeting net8.0, Windows App SDK 1.8, non-admin user, Developer Mode on.

## 1. Verified vs not verified

| Item | Status |
|---|---|
| C# out-of-proc COM provider builds from CLI (`dotnet publish`, no VS) | VERIFIED |
| `makepri` + hand-written manifest + `Add-AppxPackage -Register <layout>\AppxManifest.xml` (Developer Mode loose-file), **no admin** | VERIFIED |
| `Get-AppxPackage Otzaria.WidgetSpike` shows the package, Status Ok | VERIFIED |
| System sees the widget extension: `AppExtensionCatalog.Open("com.microsoft.windows.widgets")` lists our provider with the parsed `Definition` (Id, DisplayName, AllowMultiple, IsCustomizable, screenshots, icons, CreateInstance ClassId) | VERIFIED (WidgetHostProbe) |
| Provider EXE is launched by COM (`CoCreateInstance(CLSID)` from another process) with args `-RegisterProcessAsComServer -Embedding`, `CoRegisterClassObject` succeeds, class factory `CreateInstance` runs | VERIFIED |
| `WidgetManager.GetDefault().GetWidgetInfos()` works inside the provider (returns `null` - not an empty array - when no widgets are pinned) | VERIFIED |
| Process stays alive while widgets exist / exits ~60 s after the last one is gone | VERIFIED for the idle-exit branch (no widgets); "stay alive with widgets" is by construction |
| Template/data binding, `$when`, `$host.*` expansion of both cards for medium and large (offline, AdaptiveCards.Templating) | VERIFIED (`WidgetHostProbe --expand`) |
| Max Adaptive Card version of the board renderer = 1.6 | VERIFIED from `AdaptiveCards.ObjectModel.WinUI3.dll` (only `1.0` and `1.6` version literals) + MS docs template uses 1.6 |
| Widget definition shown in the picker (Win+W -> Add widgets), screenshot/icon rendering | NOT VERIFIED - needs a human (see section 8). The Hosts API cannot be activated from a normal app (`0x80040154`). Driving the board via UI Automation was abandoned: content is a WebView2 and the flyout closes on focus loss; it would also have meant poking the user's live desktop. |
| `CreateWidget` / `OnActionInvoked` / `OnCustomizationRequested` / `CustomState` persistence / provider-restart recovery with real pinned widgets | NOT VERIFIED (needs a pinned widget). Code follows the MS guide; logs every host call. |
| Hebrew `rtl` rendering, `Action.OpenUrl` with `otzaria://` from the board | NOT VERIFIED (manual test prepared, section 8) |
| Signed MSIX install without admin | VERIFIED IMPOSSIBLE on this build (section 6) |
| Self-contained Windows App SDK variant | PARTIALLY verified: registers, provider starts, `WidgetManager` works. Host calls not tested. |

## 2. Decisions

### Windows App SDK: framework-dependent 1.8 (chosen)
* MS guide's own walkthrough is framework-dependent; the 2026 version of the guide says "uses Microsoft.WindowsAppSDK 2.5.1 ... framework-dependent deployment" (2.x exists: `Microsoft.WindowsAppRuntime.2` 2.6.5 is installed here and `microsoft.windowsappsdk.widgets 2.6.5` is in the NuGet cache). 1.8 was chosen because it is the line that is already installed in many places, the NuGet cache here has it, and the Widgets API (`IWidgetProvider2` etc.) is identical. Switching to 2.x = change the three `PackageReference` versions and `PackageDependency Name="Microsoft.WindowsAppRuntime.2"` (+ matching MinVersion).
* You only need **three** packages, not the whole `Microsoft.WindowsAppSDK` metapackage (which drags WinUI, AI, ML...): `Microsoft.WindowsAppSDK.Widgets` (contains the precompiled CsWinRT projection `Microsoft.Windows.Widgets.Projection.dll` + winmd), `Microsoft.WindowsAppSDK.Base` (CsWinRT / build targets, pulls `Microsoft.Windows.CsWinRT`, `Microsoft.Windows.SDK.NET`), and `Microsoft.WindowsAppSDK.Runtime` (required by Base's targets when `WindowsAppSDKSelfContained=false`: build error otherwise).
* Framework-dependent means the machine needs the `Microsoft.WindowsAppRuntime.1.8` framework package (Store installs it automatically from the dependency; sideload users need the runtime installer - the four framework MSIX files (`Microsoft.WindowsAppRuntime.1.8.msix`, `.Main.`, `.DDLM.`, `.Singleton.`) ship in the `Microsoft.WindowsAppSDK.Runtime` nuget under `tools/MSIX/win10-x64/`, or use the official "Windows App SDK runtime installer" exe). `PackageDependency` is declared in the manifest, so a missing framework makes `Add-AppxPackage` fail with a clear message.
* Why not self-contained: it works at the registration/activation level (tested: copies `Microsoft.Windows.Widgets.dll` + `Microsoft.WindowsAppRuntime.dll` next to the exe; CsWinRT falls back to loading the DLL from the app folder; the OS-supplied proxy/stubs for `IWidgetProvider`/`IWidgetProvider2` come from `Microsoft.WidgetsPlatformRuntime`, so no `package.appxfragment` is strictly needed), but: bigger payload, makepri reports duplicate `Microsoft.UI.Xaml` asset conflicts (harmless, registration still succeeded), no servicing of the runtime, and it is the less-travelled path with the Widgets host. It's the fallback if "user must install a framework package" becomes a distribution problem. Switch: `-p:WindowsAppSDKSelfContained=true` and delete the `PackageDependency` line.
* .NET runtime itself is **self-contained** (`SelfContained=true`, `win-x64`) so no .NET install is required (output ~96 MB because `Microsoft.Windows.SDK.NET.dll` is 25 MB and trimming is off; see gotchas). Framework-dependent .NET would be smaller but would require the .NET 8 runtime on every machine (not tested).

### Packaging style
Single-folder "layout" + hand-written manifest, no `.wapproj`, no `winapp` CLI (the MS guide mentions the preview `winapp run`, not needed). `build.ps1` = publish -> copy assets/manifest -> `makepri` -> register (or pack+sign).

### Provider lifetime
`CoRegisterClassObject(CLSCTX_LOCAL_SERVER, REGCLS_MULTIPLEUSE)`; main thread blocks on `ManualResetEvent`. Rules used: stay alive while any widget is known; exit after 60 s idle with zero widgets (the MS guide's "exit when last widget is deleted" is flagged by MS itself as a simplified policy - the host can activate/release the provider while widgets remain pinned). A single provider instance is shared by all `CreateInstance` calls.

## 3. Working files (copied verbatim from the repo)

### spikes/WidgetSpike/WidgetSpike.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.19041.0</TargetPlatformMinVersion>
    <RootNamespace>WidgetSpike</RootNamespace>
    <AssemblyName>WidgetSpike</AssemblyName>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <Platforms>x64</Platforms>
    <Platform>x64</Platform>
    <PlatformTarget>x64</PlatformTarget>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <!-- The exe only ever runs with MSIX package identity (framework dependency in the manifest); no bootstrapper/auto-init. -->
    <WindowsPackageType>None</WindowsPackageType>
    <EnableMsixTooling>false</EnableMsixTooling>
    <UseWinUI>false</UseWinUI>
    <!-- Framework-dependent Windows App SDK: Microsoft.Windows.Widgets.dll + proxy/stubs come from Microsoft.WindowsAppRuntime.1.8 -->
    <WindowsAppSDKSelfContained>false</WindowsAppSDKSelfContained>
    <!-- Keep CsWinRT/ComWrappers path working: no trimming, no AOT, no single-file. -->
    <PublishTrimmed>false</PublishTrimmed>
    <PublishAot>false</PublishAot>
    <PublishSingleFile>false</PublishSingleFile>
    <DebugType>embedded</DebugType>
    <GenerateAssemblyInfo>true</GenerateAssemblyInfo>
    <!-- Package.appxmanifest + Templates are handled by build.ps1 -->
    <EnableDefaultNoneItems>false</EnableDefaultNoneItems>
  </PropertyGroup>

  <ItemGroup>
    <!-- Only the Widgets component (projection + winmd) is needed. -->
    <PackageReference Include="Microsoft.WindowsAppSDK.Widgets" Version="1.8.251231004" />
    <PackageReference Include="Microsoft.WindowsAppSDK.Base" Version="1.8.251216001" />
    <PackageReference Include="Microsoft.WindowsAppSDK.Runtime" Version="1.8.260921001" />
  </ItemGroup>

  <ItemGroup>
    <None Include="Templates\*.json" CopyToOutputDirectory="PreserveNewest" LinkBase="Templates" />
  </ItemGroup>

</Project>
```

### spikes/WidgetSpike/Package.appxmanifest
(`build.ps1` replaces `@VERSION@` / `@PUBLISHER@` and writes `layout\AppxManifest.xml`.)
```xml
<?xml version="1.0" encoding="utf-8"?>
<!--
  Hand-written MSIX manifest for the Otzaria widget-provider spike.
  build.ps1 copies this to <layout>\AppxManifest.xml (and substitutes @VERSION@ / @PUBLISHER@).
  The CLSID below MUST equal CLSID_WidgetProvider in Program.cs (it appears twice here).
-->
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3"
  xmlns:com="http://schemas.microsoft.com/appx/manifest/com/windows10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap uap3 com rescap">

  <Identity Name="Otzaria.WidgetSpike" Publisher="@PUBLISHER@" Version="@VERSION@" ProcessorArchitecture="x64" />

  <Properties>
    <DisplayName>Otzaria Widget Spike</DisplayName>
    <PublisherDisplayName>Otzaria Dev</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>

  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26100.0" />
    <!-- Framework-dependent Windows App SDK 1.8 (supplies Microsoft.Windows.Widgets.dll + IWidgetProvider2 proxy/stub). -->
    <PackageDependency Name="Microsoft.WindowsAppRuntime.1.8" MinVersion="8000.806.2252.0"
                       Publisher="CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US" />
  </Dependencies>

  <Resources>
    <Resource Language="en-US" />
    <Resource Language="he-IL" />
  </Resources>

  <Applications>
    <Application Id="App" Executable="WidgetSpike.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements DisplayName="Otzaria Widget Spike"
                          Description="Spike: Windows 11 widget provider (C#, out-of-proc COM)"
                          BackgroundColor="transparent"
                          Square150x150Logo="Assets\Square150x150Logo.png"
                          Square44x44Logo="Assets\Square44x44Logo.png">
        <uap:DefaultTile Wide310x150Logo="Assets\Wide310x150Logo.png" />
      </uap:VisualElements>

      <Extensions>
        <!-- 1) Out-of-proc COM server: lets the Widgets host CoCreateInstance our provider (packaged equivalent of HKCU\...\LocalServer32). -->
        <com:Extension Category="windows.comServer">
          <com:ComServer>
            <com:ExeServer Executable="WidgetSpike.exe" Arguments="-RegisterProcessAsComServer" DisplayName="Otzaria Widget Spike Provider">
              <com:Class Id="6C3B7E0A-2F4D-4E8B-9A51-0D7B7A5E1C42" DisplayName="Otzaria Widget Spike Provider" />
            </com:ExeServer>
          </com:ComServer>
        </com:Extension>

        <!-- 2) Widget provider registration (read by the Widgets host). -->
        <uap3:Extension Category="windows.appExtension">
          <uap3:AppExtension Name="com.microsoft.windows.widgets"
                             DisplayName="Otzaria Widget Spike"
                             Id="OtzariaWidgetSpike"
                             PublicFolder="Public">
            <uap3:Properties>
              <WidgetProvider>
                <ProviderIcons>
                  <Icon Path="ProviderAssets\Icon.png" />
                </ProviderIcons>
                <Activation>
                  <CreateInstance ClassId="6C3B7E0A-2F4D-4E8B-9A51-0D7B7A5E1C42" />
                </Activation>
                <Definitions>
                  <Definition Id="Otzaria_Spike_Widget"
                              DisplayName="Otzaria Widget Spike"
                              Description="Spike: Hebrew RTL card, counter, settings, otzaria:// deep link"
                              AllowMultiple="true"
                              IsCustomizable="true">
                    <Capabilities>
                      <Capability><Size Name="medium" /></Capability>
                      <Capability><Size Name="large" /></Capability>
                    </Capabilities>
                    <ThemeResources>
                      <Icons>
                        <Icon Path="ProviderAssets\Icon.png" />
                      </Icons>
                      <Screenshots>
                        <Screenshot Path="ProviderAssets\Screenshot.png" DisplayAltText="Otzaria widget spike preview" />
                      </Screenshots>
                      <DarkMode>
                        <Screenshots>
                          <Screenshot Path="ProviderAssets\Screenshot_dark.png" DisplayAltText="Otzaria widget spike preview (dark)" />
                        </Screenshots>
                      </DarkMode>
                      <LightMode>
                        <Screenshots>
                          <Screenshot Path="ProviderAssets\Screenshot.png" DisplayAltText="Otzaria widget spike preview (light)" />
                        </Screenshots>
                      </LightMode>
                    </ThemeResources>
                  </Definition>
                </Definitions>
              </WidgetProvider>
            </uap3:Properties>
          </uap3:AppExtension>
        </uap3:Extension>
      </Extensions>
    </Application>
  </Applications>

  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
</Package>
```

### spikes/WidgetSpike/Program.cs
```csharp
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
```

### spikes/WidgetSpike/FactoryHelper.cs
```csharp
using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;
using WinRT;

namespace COM
{
    static class Guids
    {
        public const string IClassFactory = "00000001-0000-0000-C000-000000000046";
        public const string IUnknown = "00000000-0000-0000-C000-000000000046";
    }

    [ComImport, ComVisible(false), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid(COM.Guids.IClassFactory)]
    internal interface IClassFactory
    {
        [PreserveSig]
        int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);
        [PreserveSig]
        int LockServer(bool fLock);
    }

    /// <summary>Class factory that hands the widget host a CCW of our IWidgetProvider (+IWidgetProvider2).</summary>
    [ComVisible(true)]
    class WidgetProviderFactory<T> : IClassFactory where T : IWidgetProvider
    {
        private const int CLASS_E_NOAGGREGATION = unchecked((int)0x80040110);

        // The host may call CreateInstance more than once; the supplier must hand out ONE provider object so state is shared.
        private readonly Func<T> _supplier;
        public WidgetProviderFactory(Func<T> supplier) { _supplier = supplier; }

        public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
        {
            ppvObject = IntPtr.Zero;
            if (pUnkOuter != IntPtr.Zero) return CLASS_E_NOAGGREGATION;

            IntPtr provider = IntPtr.Zero;
            try
            {
                WidgetSpike.Log.Write($"IClassFactory.CreateInstance riid={riid}");
                T inst = _supplier();
                provider = MarshalInspectable<IWidgetProvider>.FromManaged(inst);
                return Marshal.QueryInterface(provider, in riid, out ppvObject);
            }
            catch (Exception ex)
            {
                WidgetSpike.Log.Error("CreateInstance", ex);
                return Marshal.GetHRForException(ex);
            }
            finally
            {
                if (provider != IntPtr.Zero) Marshal.Release(provider);
            }
        }

        int IClassFactory.LockServer(bool fLock) => 0;
    }
}
```

### spikes/WidgetSpike/Templates/main.json (the widget card)
```json
{
  "type": "AdaptiveCard",
  "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
  "version": "1.6",
  "rtl": true,
  "body": [
    {
      "type": "Container",
      "rtl": true,
      "items": [
        {
          "type": "TextBlock",
          "text": "${greeting}",
          "size": "large",
          "weight": "bolder",
          "wrap": true
        },
        {
          "type": "TextBlock",
          "text": "מספר הלחיצות: ${count}",
          "wrap": true,
          "spacing": "small"
        },
        {
          "type": "TextBlock",
          "text": "גודל: ${$host.widgetSize} | ערכת נושא: ${$host.hostTheme}",
          "isSubtle": true,
          "size": "small",
          "wrap": true
        },
        {
          "type": "TextBlock",
          "text": "Mixed LTR: Otzaria 1.0 - ספר ברכות דף ב׳ ע״א",
          "size": "small",
          "wrap": true,
          "$when": "${$host.widgetSize==\"large\"}"
        },
        {
          "type": "TextBlock",
          "text": "הווידג׳ט הגדול מציג שורה נוספת. הגדרות: כפתור פתיחה = ${showOpen}",
          "wrap": true,
          "size": "small",
          "isSubtle": true,
          "$when": "${$host.widgetSize==\"large\"}"
        }
      ]
    }
  ],
  "actions": [
    {
      "type": "Action.Execute",
      "title": "הוסף +1",
      "verb": "inc",
      "data": { "step": 1 },
      "$when": "${$host.isUserContextAuthenticated != false}"
    },
    {
      "type": "Action.OpenUrl",
      "title": "פתח באוצריא",
      "url": "otzaria://open?book=berachot&page=2",
      "$when": "${showOpen == 'true'}"
    },
    {
      "type": "Action.OpenUrl",
      "title": "אתר (https)",
      "url": "https://github.com/Otzaria/otzaria"
    }
  ]
}
```

### spikes/WidgetSpike/Templates/settings.json (customization card)
```json
{
  "type": "AdaptiveCard",
  "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
  "version": "1.6",
  "rtl": true,
  "body": [
    {
      "type": "TextBlock",
      "text": "הגדרות הווידג׳ט",
      "weight": "bolder",
      "size": "medium",
      "wrap": true
    },
    {
      "type": "Input.ChoiceSet",
      "id": "greeting",
      "label": "טקסט ברכה",
      "style": "compact",
      "value": "${greeting}",
      "choices": [
        { "title": "שלום עולם", "value": "שלום עולם" },
        { "title": "בוקר טוב", "value": "בוקר טוב" },
        { "title": "ברוכים הבאים לאוצריא", "value": "ברוכים הבאים לאוצריא" }
      ]
    },
    {
      "type": "Input.Toggle",
      "id": "showOpen",
      "title": "הצג כפתור פתיחה באוצריא",
      "value": "${showOpen}",
      "valueOn": "true",
      "valueOff": "false"
    }
  ],
  "actions": [
    { "type": "Action.Execute", "title": "שמור", "verb": "saveSettings" },
    { "type": "Action.Execute", "title": "ביטול", "verb": "exitCustomization" }
  ]
}
```

`WidgetProvider.cs` (IWidgetProvider + IWidgetProvider2, per-widget state in `CustomState`, recovery via `GetWidgetInfos`, file logging) and `Log.cs` are in the same folder - not duplicated here.

## 4. Build / register commands

```powershell
cd spikes\WidgetSpike
.\build.ps1                 # publish -> layout\ -> makepri -> Add-AppxPackage -Register (no admin)
.\build.ps1 -NoRegister     # layout only
.\build.ps1 -Pack           # + dist\*.msix signed with self-signed CurrentUser cert + dist\OtzariaDev.cer
.\unregister.ps1            # remove (use -CleanLogs to wipe logs)

# what build.ps1 does, in raw form:
dotnet publish WidgetSpike.csproj -c Release -r win-x64 -o layout
makepri createconfig /cf obj\priconfig.xml /dq en-US /pv 10.0.0 /o
makepri new /pr layout /cf obj\priconfig.xml /mn layout\AppxManifest.xml /of layout\resources.pri /o
Add-AppxPackage -Register layout\AppxManifest.xml -ForceApplicationShutdown -ForceUpdateFromAnyVersion
makeappx pack /d layout /p dist\x.msix /o /nv ; signtool sign /fd SHA256 /sha1 <thumb> dist\x.msix
```
Tools: `C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\{makepri,makeappx,signtool}.exe`.
Placeholder assets: `python tools\make-assets.py` (Pillow; `build.ps1` calls it if PNGs are missing).

Diagnostics:
```powershell
dotnet run --project spikes\WidgetHostProbe                       # catalog + COM activation probe
dotnet run --project spikes\WidgetHostProbe -- --expand spikes\WidgetSpike\Templates\main.json large dark   # offline template expansion
Get-Content "$env:LOCALAPPDATA\Packages\Otzaria.WidgetSpike_94b2qg0k1ws4g\LocalCache\Local\OtzariaWidgetSpike\provider.log" -Wait
```

## 5. Gotchas (things that bit or will bite)

1. **Re-registering the same version fails** (`0x80073CFB ... already installed, reinstallation blocked`) for loose-file registration. `build.ps1` generates a new version every build (`0.1.<days>.<minute-of-day>`), so each build is an in-place update that keeps pinned widgets. A layout folder must stay on disk while registered (it *is* the install location). `Remove-AppxPackage` deletes the package's LocalCache (logs) and presumably pinned widgets.
2. **Never use a console subsystem exe** for the final product: the host launches the COM server and a console window would flash (`OutputType=WinExe`).
3. **CsWinRT / ComWrappers**: call `WinRT.ComWrappersSupport.InitializeComWrappers()` first, hand the host `MarshalInspectable<IWidgetProvider>.FromManaged(provider)` then `QueryInterface(riid)`. The classic `[ComImport] IClassFactory` is used, so **no trimming, no NativeAOT, no single-file** (`PublishTrimmed/PublishAot/PublishSingleFile=false`). `IWidgetProvider2` is exposed automatically because the managed class implements it - verified only up to class-factory level, because the host does the QI.
4. Do **not** add `[STAThread]`; stay MTA. Callbacks arrive on RPC threads (`t4`, `t6` in the log) - the provider guards its dictionary with a lock. Do not keep the callback argument objects after returning (MS remarks).
5. `GetWidgetInfos()` returns **null** (not empty array) with no widgets -> `?? Array.Empty<WidgetInfo>()`.
6. Exe is started with `-RegisterProcessAsComServer -Embedding` (the first comes from `Arguments=` on `com:ExeServer`, the second from COM). `Environment.CommandLine` shows the `.dll` path (apphost).
7. **AppData virtualization**: writes to `%LOCALAPPDATA%\OtzariaWidgetSpike\` from the packaged exe land in `%LOCALAPPDATA%\Packages\<PFN>\LocalCache\Local\OtzariaWidgetSpike\`. Use `ApplicationData.Current.LocalFolder` or a real path outside AppData if you need a non-virtualized location. Format timestamps with `CultureInfo.InvariantCulture` - this machine's culture is Hebrew calendar (`תשפ"ז-...`).
8. **Both CLSIDs must match** (`com:Class Id`, `CreateInstance ClassId`, `CLSID_WidgetProvider`). GUID is not case sensitive in the manifest.
9. `Application Executable` and `ExeServer Executable` are paths relative to the package root (exe at the root of the layout here; the MS sample uses a sub-folder).
10. **x64 only** in the spike (`ProcessorArchitecture="x64"`, `win-x64`). arm64 needs `-r win-arm64` + a second package/bundle; the framework dependency architecture must match.
11. Screenshots: 300x304 px recommended, transparent rounded corners; **`Icons` and `Screenshots` are mandatory** per Definition (else the definition does not show). Localized screenshots: `ProviderAssets\en-us\Screenshot.png` etc. resolved by resource loader; hence `resources.pri` is needed (makepri) even for loose registration.
12. Customization: MS docs note a known board bug - the ellipsis menu becomes unresponsive after the customization card is shown. `IsCustomizable="true"` on the Definition is what adds "Customize widget"; the card the provider sends while in customization mode is just another template (`Input.*` values come back as JSON in `args.Data`, as strings: toggle = `"true"`/`"false"`). Alternative without `IWidgetProvider2`: `$host.isSettingsPayload`.
13. `OnWidgetContextChanged` is only called for **size** changes. `Activate`/`Deactivate` windows can be very short; keep updates cheap.
14. Widgets/AdaptiveCards: `$host.isLightTheme` (asked for in the brief) is **not** a documented property. The documented one is `$host.hostTheme` ("light"/"dark") - the string `hostTheme` is in the board binary, `isLightTheme` is not.
15. Template language quirk seen: a text that is exactly `"${greeting}"` is emitted JSON-escaped (`\u05E9...`), semantically identical.
16. Signing/trust: see section 6.
17. Spike package family name: `Otzaria.WidgetSpike_94b2qg0k1ws4g` (hash derives from Publisher `CN=OtzariaDev`).
18. A pre-existing `NetFreeWidget` package on this machine (VS layout in `Downloads\NetFreeWidget`) is a second working example of the same manifest shape (it uses WindowsAppRuntime.1.6 + VCLibs; we do not need VCLibs because our exe is pure managed + self-contained .NET).

## 6. Installing: what works without admin

| Method | Result |
|---|---|
| `Add-AppxPackage -Register layout\AppxManifest.xml` (Developer Mode) | **Works, no admin, no signing.** Use for development. |
| Signed `.msix`, self-signed cert only in `CurrentUser\My` | `0x800B0109` root certificate must be trusted |
| ... plus cert imported into `CurrentUser\TrustedPeople` | **Still `0x800B0109`** - MSIX trust is checked against LocalMachine stores |
| ... cert into `LocalMachine\TrustedPeople` | Needs admin (`Import-Certificate` denied) - the working route for sideload testing: `Import-Certificate dist\OtzariaDev.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople` from an elevated shell, then `Add-AppxPackage dist\*.msix` |
| `Add-AppxPackage -AllowUnsigned` | With a normal publisher: `0x80073D2C` (not in unsigned namespace). With publisher `OID.2.25.311729368913984317654407730594956997722=1`: `0x80073D2B` **"an unsigned package cannot include Executable activations"** -> unusable for a COM EXE widget provider. |

Distribution options: (a) Microsoft Store (auto-signs, auto-installs the framework dependency; also the only way into the "Widgets" Store collection), (b) a real code-signing certificate chained to a public root (OV/EV, or Azure Trusted Signing) - then no cert import is needed, (c) sideload with admin-installed dev cert (testers only). Signing publisher string in the manifest must equal the cert Subject exactly.

## 7. Adaptive Cards in the Widgets Board

Sources: MS Learn (widgets-create-a-template, design fundamentals, picker integration, provider manifest) + inspection of the host binaries shipped in `MicrosoftWindows.Client.WebExperience_526...` (`AdaptiveCards.ObjectModel.WinUI3.dll`, `Widgets.dll`).

* **Version**: `"version": "1.6"` (MS's current template) - renderer's max is 1.6. The old sample uses 1.5 / 1.0; fine. No 1.6+ elements (Charts, Badge, CodeBlock, Icon, ProgressBar, Rating, CompoundButton, Popover) exist in the renderer's object model - avoid them.
* **Elements present in the object model**: TextBlock, RichTextBlock, Media, Image, Container, ColumnSet/Column, Table, ActionSet, Carousel (strings found in the binary; the usual Image/ImageSet/FactSet are assumed, not grepped); **Inputs**: Input.Text, Input.Number, Input.Date, Input.Time, Input.Toggle, Input.ChoiceSet (compact/expanded/filtered styles). **Actions** in the renderer: Execute, OpenUrl, ShowCard, Submit, ToggleVisibility. MS docs only guarantee Action.Execute and Action.OpenUrl for widgets. Rating input is absent.
* **Authentication context**: `$host.isUserContextAuthenticated == false` -> **only Action.OpenUrl** works; hide Execute buttons (the main card has `$when` for this).
* **`rtl`**: AC supports `"rtl": true` on the card root, `Container`, `ColumnSet`, `Table` (since 1.5). The spike sets it on both the root and the main `Container`, and does not set `horizontalAlignment` so the renderer decides. Whether the Widgets Board WinUI renderer honors it (and how mixed Hebrew/Latin runs and `wrap` look) must be checked visually. If it doesn't, fallback: write the TextBlocks with `horizontalAlignment: right` and rely on Unicode bidi in the text itself (add U+200F RLM where punctuation is misplaced).
* **`$host` properties** (documented): `widgetSize` ("small"|"medium"|"large"), `hostTheme` ("light"|"dark"), `isSettingsPayload`, `isHeaderSupported`, `isHeader`, `isWebSupported`, `isUserContextAuthenticated`. Use in templates: `"$when": "${$host.widgetSize==\"large\"}"`. These are not part of the data JSON and not available in the Adaptive Cards Designer.
* **Sizes**: manifest `Size Name="small|medium|large"`; no Definition capabilities -> large only. MS publishes no pixel sizes for the card area; documented layout: 16 px margins, 48 px header/attribution area, 4 px gutter, 4-px multiples, type ramp (Caption 12/16, Body 14/20, Subtitle 20/28, Title 28/36 with AC size small/default/medium/large/extraLarge). Small should have 1 touch target, medium 2-3, large 3-4. Measure real pixel sizes empirically if needed (not done).
* **Images**: use `https` URLs (host fetches them), `backgroundImage` supported; image/`url` over `data:`/`ms-appx` URIs is NOT verified here - test before relying on bundled images. Profile pictures 24/32/48/96 px circles. Picker screenshot 300x304.
* **Limits** (data payload size, template size, update rate): **not documented by MS** in the pages checked; unverified. Practical advice: keep template+data small (a few KB), do not push updates faster than user-visible events (Activate, action, size change, a timer while `isActive`), and rely on `WidgetManager.UpdateWidget` being rate-limited by the platform.
* **Update model**: provider pushes `UpdateWidget(WidgetUpdateRequestOptions{Template, Data, CustomState})`; template and data may be sent separately (send Data only to change values). `CustomState` is stored by the widget service per widget and returned by `GetWidgetInfos()` and `DeleteWidget(widgetId, customState)` - this is the persistence used by the spike (`{"count":3,"greeting":"...","showOpen":true}`). `IsPlaceholderContent` (SDK 1.6.6+) marks non-personalized content.

## 8. Manual verification (what a human must do)

1. `cd spikes\WidgetSpike; .\build.ps1` (or just use the already registered package: `Get-AppxPackage Otzaria.WidgetSpike`).
2. Optional deep-link test handler: `.\tools\register-test-scheme.ps1` (HKCU, no admin; appends clicks to `%TEMP%\otzaria-deeplink.log`).
3. Win+W -> "+" (Add widgets) -> look for **"Otzaria Widget Spike"** (may sit under the provider name; the picker caches, if absent sign out/in or restart `WidgetService`/`explorer`). Pin the Medium, resize to Large.
4. Expect: Hebrew title "שלום עולם" right-aligned (RTL), counter text, size/theme line; buttons "הוסף +1" (counter), "פתח באוצריא" (otzaria://), "אתר (https)". Large shows two extra lines.
5. "..." -> **Customize widget** -> pick greeting / toggle -> "שמור"; the main card reflects it. Restart: `Stop-Process -Name WidgetSpike` - the host re-launches the provider on the next interaction; the counter and settings must be intact (log line `recovered id=... customState=...`).
6. Check the log (path in section 4): every host call (`CreateWidget`, `Activate`, `OnActionInvoked verb=... data=...`, `UpdateWidget ...`) is recorded.
7. Unpin -> `DeleteWidget` in log; 60 s later the process exits.
If the widget is missing from the picker: check `dotnet run --project spikes\WidgetHostProbe` still shows the `*` entry, and that `ThemeResources` has both Icons and Screenshots with existing files.

## 9. Deep links with `Action.OpenUrl` and a custom scheme (otzaria://)

* The card uses `{"type":"Action.OpenUrl","url":"otzaria://open?book=berachot&page=2"}`. The board will hand the URL to the shell (`Launcher`/ShellExecute); it is **unverified** whether the board filters non-http(s) schemes - the manual test above answers that (the button is in the card; the first launch may show Windows' "choose an app" prompt).
* Register the scheme in the *Otzaria app*, not the widget provider (unless they are the same package):
  * **Packaged (MSIX) Otzaria**: in its manifest `<uap:Extension Category="windows.protocol"><uap:Protocol Name="otzaria"><uap:DisplayName>Otzaria</uap:DisplayName></uap:Protocol></uap:Extension>` (namespace `uap`). The app receives the URI via `AppInstance.GetActivatedEventArgs()` / command line `otzaria://...` depending on framework. For a Flutter/Win32 app with `msix` pub package: `protocol_activation: otzaria`.
  * **Unpackaged / installer**: `HKCU\Software\Classes\otzaria` with `URL Protocol` and `shell\open\command` = `"C:\path\otzaria.exe" "%1"` (what `tools/register-test-scheme.ps1` does, no admin).
* Single instance: the already-running Otzaria should receive the URL (named mutex/pipe forwarding, or `AppInstance.RedirectActivationToAsync`), otherwise each click spawns a new instance.
* Keep URLs short, ASCII, percent-encode Hebrew (`otzaria://open?book=%D7%91%D7%A8%D7%9B%D7%95%D7%AA&page=2`) - Hebrew in the query string should work after encoding, but verify end-to-end.
* Alternative that avoids the scheme question: `Action.Execute` -> provider's `OnActionInvoked` -> provider launches Otzaria itself (`Process.Start` / `Launcher.LaunchUriAsync` from the provider process). Works even when `OpenUrl` filtering turns out to be strict, at the cost of a round trip through the provider.

## 10. Risks / open questions for the next phase
* Verify in the picker + rtl + OpenUrl custom scheme (section 8) before building the real widget.
* Decide 1.8 vs 2.x runtime before first public package (changing later = new dependency name).
* Decide whether the provider EXE lives inside the main Otzaria MSIX (single package, shared protocol registration and data folder - recommended) or stays a separate package (what this spike is).
* Widget data source: provider cannot read Otzaria's data if sandbox/virtualization differs; same-package design avoids that.
