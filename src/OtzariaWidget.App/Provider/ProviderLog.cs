// Agent D - tiny file logger for the provider process. Never throws into COM callbacks.
// Path: %LOCALAPPDATA%\OtzariaWidget\provider.log. Inside the MSIX Windows redirects NEW folders to
// %LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalCache\Local\OtzariaWidget\, but a folder that already exists in the real
// %LOCALAPPDATA% (e.g. created by an unpackaged dev build) is written through (verified on the dev machine).
// PRIVACY: log widget ids / kinds / sizes / verbs / sizes of payloads only - no titles, no search text, no notes. Exception text is
// reduced to the type + HRESULT, and a message is only kept when it contains no file path (paths can reveal the user name / library).
using System.IO;
using System.Globalization;
using System.Text;

namespace OtzariaWidget.App.Provider;

internal static class ProviderLog
{
    /// <summary>Rotation threshold: when the file grows past this it becomes provider.log.old (one generation), so the total is capped
    /// at roughly twice this value.</summary>
    internal const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();
    private static readonly string DefaultDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OtzariaWidget");

    /// <summary>Tests point this at a temp folder so unit tests never write into the real provider.log.</summary>
    internal static string? DirectoryOverride { get; set; }

    private static string Dir => DirectoryOverride ?? DefaultDir;

    public static string FilePath => Path.Combine(Dir, "provider.log");

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string where, Exception ex) =>
        Write("ERROR", $"{where}: {Describe(ex)}{Environment.NewLine}{ex.StackTrace}");

    /// <summary>"Type (0xHRESULT): message" where the message is dropped if it might contain a path.</summary>
    internal static string Describe(Exception ex)
    {
        var msg = ex.Message ?? "";
        if (msg.Contains(":\\") || msg.Contains("\\\\") || msg.Contains(":/")) msg = "<omitted: contains a path>";
        else if (msg.Length > 300) msg = msg[..300] + "...";
        return $"{ex.GetType().Name} (0x{ex.HResult:X8}): {msg}";
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > MaxBytes)
                {
                    try { File.Move(FilePath, FilePath + ".old", overwrite: true); }
                    catch { File.Delete(FilePath); }
                }
                var line = string.Create(CultureInfo.InvariantCulture,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}/{Environment.CurrentManagedThreadId}] {level} {message}{Environment.NewLine}");
                File.AppendAllText(FilePath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // logging must never throw
        }
    }
}
