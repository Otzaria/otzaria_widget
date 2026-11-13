using System.Diagnostics;
using System.Text.RegularExpressions;
using OtzariaWidget.Core.Contracts;
using OtzariaWidget.Core.Paths;

namespace OtzariaWidget.Core.Launch;

/// <summary>
/// Builds otzaria:// links (storage-and-links.md §5.2) and launches Otzaria (§5.4 method A, falling back to B).
/// Never generates plugin/install URLs.
/// </summary>
public sealed partial class OtzariaLauncher : IOtzariaLauncher
{
    public const string Scheme = "otzaria://";

    private static readonly HashSet<string> TextTypes = new(StringComparer.Ordinal) { "TextBook", "DocxBook", "EpubBook", "DocumentBook" };
    private static readonly HashSet<string> SearchModes = new(StringComparer.Ordinal) { "advanced", "exact", "fuzzy", "smart" };

    // \z, not $: in .NET `$` also matches before a trailing newline
    [GeneratedRegex(@"^[\p{L}\p{N}._-]{1,64}\z")]
    private static partial Regex SlugRegex();

    /// <summary>BookSource.isValidSlug: letters, digits and ._- (1..64).</summary>
    public static bool IsValidSlug(string? slug) => slug is not null && SlugRegex().IsMatch(slug);

    /// <summary>Longest URI we hand to the shell / CreateProcess (command lines are limited to 32 767 chars).</summary>
    public const int MaxUriLength = 8192;
    private const int MaxQueryEscapedLength = 3000;

    private readonly Func<ProcessStartInfo, bool> _starter;
    private readonly Func<bool> _protocolRegistered;
    private readonly Func<string, bool> _fileExists;

    public OtzariaLauncher() : this(DefaultStart, DefaultProtocolRegistered, File.Exists) { }

    /// <summary>For tests: inject the process starter, the protocol-registered probe and the file-exists probe.</summary>
    public OtzariaLauncher(Func<ProcessStartInfo, bool> starter, Func<bool> protocolRegistered, Func<string, bool> fileExists)
    {
        _starter = starter;
        _protocolRegistered = protocolRegistered;
        _fileExists = fileExists;
    }

    // ------------------------------------------------------------------------------------------
    public string? BuildLink(ReadingEntry entry)
    {
        try
        {
            var book = entry?.Book;
            if (book is null) return null;
            if (book.Id is not int id || id <= 0) return null;

            var suffix = SourceParam(book.Source);
            if (suffix is null) return null; // unknown source kind => not linkable

            string kind;
            int index;
            if (book.IsPdf)
            {
                kind = "pdf";
                index = Math.Max(entry!.Index, 1);
            }
            else if (TextTypes.Contains(book.Type))
            {
                kind = "book";
                index = Math.Max(entry!.Index, 0);
            }
            else return null;

            var query = suffix.Length == 0 ? $"index={index}" : $"{suffix}&index={index}";
            return $"{Scheme}open/{kind}/{id}?{query}";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Link that just opens a book (no position), or null for non-positive ids.</summary>
    public static string? BuildBookOpenLink(int bookId)
        => bookId > 0 ? $"{Scheme}open/book/{bookId}" : null;

    public string BuildSearchLink(string query, string? mode)
    {
        var escaped = Uri.EscapeDataString(query ?? "");
        // An absurdly long query would exceed what hosts / the command line accept: just open the search screen.
        if (escaped.Length > MaxQueryEscapedLength) return $"{Scheme}open/search";
        var link = $"{Scheme}open/search?q={escaped}";
        if (mode is not null)
        {
            var m = mode.Trim().ToLowerInvariant();
            if (SearchModes.Contains(m)) link += "&mode=" + m;
        }
        return link;
    }

    /// <summary>"" for official, "source=user", "source=db:&lt;slug&gt;", or null when the source is unusable.</summary>
    private static string? SourceParam(string? source)
    {
        if (string.IsNullOrEmpty(source) || source == "o") return "";
        if (source == "u") return "source=user";
        if (source.StartsWith("d:", StringComparison.Ordinal))
        {
            var slug = source[2..];
            if (!IsValidSlug(slug)) return null;
            return "source=db:" + Uri.EscapeDataString(slug);
        }
        return null;
    }

    // ------------------------------------------------------------------------------------------
    public bool Launch(string? uri, OtzariaPaths paths)
    {
        try
        {
            if (uri is not null && !IsSafeOtzariaUri(uri)) return false;

            var exe = paths?.OtzariaExe;
            if (!string.IsNullOrWhiteSpace(exe) && _fileExists(exe))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
                };
                if (uri is not null) psi.ArgumentList.Add(uri);
                return _starter(psi);
            }

            if (uri is not null && _protocolRegistered())
            {
                var psi = new ProcessStartInfo { FileName = uri, UseShellExecute = true };
                return _starter(psi);
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Only <c>otzaria://open/...</c> URIs (what the widget generates) may be launched: no plugin installs / reindex / other hosts,
    /// no <c>open/plugin/...</c>, and no characters that could break out of a quoted command-line argument.
    /// </summary>
    public static bool IsSafeOtzariaUri(string? uri)
    {
        if (string.IsNullOrEmpty(uri) || uri.Length > MaxUriLength) return false;
        foreach (var ch in uri)
        {
            if (ch <= ' ' || ch == (char)0x7f || ch is '"' or '`' or '^' or '<' or '>' or '|' or (char)92 or '{' or '}')
                return false;
        }
        const string prefix = Scheme + "open/";
        if (!uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var rest = uri[prefix.Length..];
        var qEnd = rest.IndexOfAny(['?', '#']);
        var path = qEnd < 0 ? rest : rest[..qEnd];
        // any segment: Otzaria ignores empty segments and may normalise dot segments, and percent-decodes host/segments
        foreach (var seg in path.Split('/'))
        {
            var d = Uri.UnescapeDataString(seg);
            if (d is "plugin" or "." or ".." || d.Equals("plugin", StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private static bool DefaultStart(ProcessStartInfo psi)
    {
        using var p = Process.Start(psi);
        return p is not null || psi.UseShellExecute; // ShellExecute may legitimately return null
    }

    private static bool DefaultProtocolRegistered()
    {
        var env = SystemResolverEnvironment.Instance;
        return env.GetRegistryString(RegistryRoot.CurrentUser, OtzariaPathResolver.ProtocolCommandKey, null) is not null
            || env.GetRegistryString(RegistryRoot.LocalMachine, OtzariaPathResolver.ProtocolCommandKey, null) is not null;
    }
}
