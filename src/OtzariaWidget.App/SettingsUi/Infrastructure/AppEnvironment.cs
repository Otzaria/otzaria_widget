using System.IO;
using System.Reflection;

namespace OtzariaWidget.App.SettingsUi.Infrastructure;

/// <summary>Version / package / log-folder facts. Everything is wrapped: unpackaged runs must not throw.</summary>
public static class AppEnvironment
{
    private const string FolderName = "OtzariaWidget";

    /// <summary>Tests point this at a temp folder so they never write into the real %LOCALAPPDATA%\OtzariaWidget.</summary>
    public static string? DataFolderOverride { get; set; }

    /// <summary>The path the process sees (inside MSIX the OS redirects it transparently).</summary>
    public static string VirtualDataFolder
        => DataFolderOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName);

    public static bool IsPackaged
    {
        get
        {
            try { _ = Windows.ApplicationModel.Package.Current.Id; return true; }
            catch { return false; }
        }
    }

    /// <summary>
    /// The real on-disk path (Explorer cannot follow the MSIX redirection). Windows only redirects writes into
    /// <c>LocalCache\Local\OtzariaWidget</c> when that folder does not already exist in the real %LOCALAPPDATA% (verified: on a machine where an
    /// unpackaged build had created it, the packaged exe keeps writing to the real folder), so the redirected folder is used only if it exists.
    /// </summary>
    public static string RealDataFolder
    {
        get
        {
            if (DataFolderOverride is not null) return DataFolderOverride;
            try
            {
                if (IsPackaged)
                {
                    var cache = Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path;
                    var redirected = Path.Combine(cache, "Local", FolderName);
                    if (Directory.Exists(redirected)) return redirected;
                }
            }
            catch { /* fall through to the virtual path */ }
            return VirtualDataFolder;
        }
    }

    public static string Version
    {
        get
        {
            try
            {
                if (IsPackaged)
                {
                    var v = Windows.ApplicationModel.Package.Current.Id.Version;
                    return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
                }
            }
            catch { }
            try
            {
                var asm = typeof(AppEnvironment).Assembly;
                var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(info)) return info.Split('+')[0];
                return asm.GetName().Version?.ToString() ?? "1.0.0";
            }
            catch { return "1.0.0"; }
        }
    }
}
