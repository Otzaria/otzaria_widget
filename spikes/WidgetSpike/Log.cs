using System.Text;

namespace WidgetSpike;

/// <summary>
/// Tiny file logger. Path: %LOCALAPPDATA%\OtzariaWidgetSpike\provider.log
/// NOTE: inside an MSIX package, AppData writes are redirected to
/// %LOCALAPPDATA%\Packages\&lt;PackageFamilyName&gt;\LocalCache\Local\OtzariaWidgetSpike\provider.log
/// </summary>
internal static class Log
{
    private static readonly object Gate = new();
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OtzariaWidgetSpike");
    public static readonly string FilePath = Path.Combine(Dir, "provider.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 512 * 1024)
                {
                    File.Copy(FilePath, FilePath + ".old", true);
                    File.Delete(FilePath);
                }
                File.AppendAllText(FilePath,
                    $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture)} [pid {Environment.ProcessId} t{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch { /* logging must never throw into COM callbacks */ }
    }

    public static void Error(string where, Exception ex) => Write($"ERROR in {where}: {ex}");
}
