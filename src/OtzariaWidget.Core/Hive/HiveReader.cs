using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.Core.Hive;

/// <summary>
/// Read-only hive_ce box reader. Opens the file with FileShare.ReadWrite|Delete, loads it fully into memory
/// (retrying on IOException because Hive compaction renames .hivec over .hive) and parses in memory.
/// Never writes, never truncates, never throws for corrupt content (stops at the first bad frame).
/// </summary>
public sealed class HiveReader : IHiveReader
{
    private const int MaxAttempts = 4;
    /// <summary>Sanity cap: a real app_preferences.hive is KBs to a few MB (Hive compacts it). Never buffer more than this.</summary>
    internal const long MaxFileBytes = 256L * 1024 * 1024;

    public IReadOnlyDictionary<string, object?> Read(string hiveFilePath, Func<string, bool> keyFilter)
        => ReadDetailed(hiveFilePath, keyFilter).Values;

    /// <summary>Like <see cref="Read"/> but also reports frame statistics and why parsing stopped.</summary>
    public HiveParseResult ReadDetailed(string hiveFilePath, Func<string, bool>? keyFilter = null)
    {
        var bytes = TryReadAllBytes(hiveFilePath);
        if (bytes is null)
            return new HiveParseResult(new Dictionary<string, object?>(), 0, 0, 0, 0, 0, "file not readable");
        return HiveParser.Parse(bytes, keyFilter);
    }

    private static byte[]? TryReadAllBytes(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
                long len = fs.Length;
                if (len > MaxFileBytes) return null;
                var buf = new byte[(int)len];
                int off = 0;
                while (off < buf.Length)
                {
                    int r = fs.Read(buf, off, buf.Length - off);
                    if (r <= 0) break;
                    off += r;
                }
                if (off < buf.Length) Array.Resize(ref buf, off);
                return buf;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // Hive renames .hivec over .hive atomically, so "missing" normally means Otzaria isn't installed:
                // one short retry, not the full backoff (this runs on every snapshot refresh).
                if (attempt == 0) Thread.Sleep(30); else return null;
            }
            catch (IOException)
            {
                // sharing violation / transient error (AV scan, compaction): back off 50, 100, 200 ms
                if (attempt < MaxAttempts - 1) Thread.Sleep(50 << attempt);
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }
        return null;
    }
}
