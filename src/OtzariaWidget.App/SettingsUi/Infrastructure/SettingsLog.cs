using System.IO;
using System.Globalization;
using System.Text;

namespace OtzariaWidget.App.SettingsUi.Infrastructure;

/// <summary>Tiny append-only log (settings-ui.log next to the provider's logs). Never throws, no personal data.</summary>
public static class SettingsLog
{
    private static readonly object Gate = new();
    private const long MaxBytes = 512 * 1024;

    public static string FilePath => Path.Combine(AppEnvironment.VirtualDataFolder, "settings-ui.log");

    public static void Info(string message) => Write("INFO ", message, null);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppEnvironment.VirtualDataFolder);
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > MaxBytes) fi.Delete();
                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                  .Append(' ').Append(level).Append(' ').Append(message);
                if (ex is not null) sb.Append(" :: ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
                sb.AppendLine();
                File.AppendAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
        }
        catch { /* logging must never break the UI */ }
    }
}
