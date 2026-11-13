using Microsoft.Win32;

namespace OtzariaWidget.Core.Paths;

public enum RegistryRoot { CurrentUser, LocalMachine }

/// <summary>
/// Thin abstraction over the file system, environment and registry so path resolution
/// can be unit-tested with fakes. All members must be exception-free (return false/null on failure).
/// </summary>
public interface IResolverEnvironment
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    /// <summary>Whole file as text (UTF-8, BOM stripped by the caller), or null on any failure.</summary>
    string? ReadAllText(string path);
    string? GetEnvironmentVariable(string name);
    /// <summary>String registry value (null valueName = the key's default value), or null. HKLM is probed in both 64- and 32-bit views.</summary>
    string? GetRegistryString(RegistryRoot root, string subKey, string? valueName);
}

/// <summary>Real implementation. Read-only.</summary>
public sealed class SystemResolverEnvironment : IResolverEnvironment
{
    public static readonly SystemResolverEnvironment Instance = new();

    public bool FileExists(string path)
    {
        try { return File.Exists(path); } catch { return false; }
    }

    public bool DirectoryExists(string path)
    {
        try { return Directory.Exists(path); } catch { return false; }
    }

    public string? ReadAllText(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length > 1024 * 1024) return null; // these are tiny text/json files; refuse anything absurd
            using var sr = new StreamReader(fs, new System.Text.UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
            return sr.ReadToEnd();
        }
        catch { return null; }
    }

    public string? GetEnvironmentVariable(string name)
    {
        try { return Environment.GetEnvironmentVariable(name); } catch { return null; }
    }

    public string? GetRegistryString(RegistryRoot root, string subKey, string? valueName)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var hive = root == RegistryRoot.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
        var views = root == RegistryRoot.CurrentUser
            ? new[] { RegistryView.Default }
            : new[] { RegistryView.Registry64, RegistryView.Registry32 };
        foreach (var view in views)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(subKey, writable: false);
                if (key?.GetValue(valueName ?? "") is string s && !string.IsNullOrWhiteSpace(s)) return s;
            }
            catch { /* ignore, try next view */ }
        }
        return null;
    }
}
