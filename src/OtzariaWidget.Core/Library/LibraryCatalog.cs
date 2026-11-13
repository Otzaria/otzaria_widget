using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.Core.Library;

/// <summary>
/// Read-only access to seforim.db (4+ GB; never copied). Short-lived connections, indexed lookups only,
/// results cached in memory. Never throws: failures return null (and are not cached).
/// </summary>
public sealed class LibraryCatalog : ILibraryCatalog
{
    private readonly string _path;
    private readonly ConcurrentDictionary<int, int> _counts = new();
    private readonly ConcurrentDictionary<int, (string Title, string? CategoryPath)> _infos = new();
    private readonly ConcurrentDictionary<string, int> _titleIds = new(StringComparer.Ordinal);

    public LibraryCatalog(string seforimDbPath)
    {
        _path = seforimDbPath;
    }

    public string DatabasePath => _path;

    // ------------------------------------------------------------------------------------------
    public int? GetLearnableItemCount(int bookId)
    {
        if (_counts.TryGetValue(bookId, out var cached)) return cached;
        try
        {
            using var conn = Open();
            var n = CountLearnable(conn, bookId);
            if (n is int v) _counts[bookId] = v;
            return n;
        }
        catch
        {
            return null;
        }
    }

    private static int? CountLearnable(SqliteConnection conn, int bookId)
    {
        // TOC rows (id, parentId) of the book
        var parentOf = new Dictionary<long, long?>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, parentId FROM tocEntry WHERE bookId = $id";
            cmd.Parameters.AddWithValue("$id", bookId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                parentOf[r.GetInt64(0)] = r.IsDBNull(1) ? null : r.GetInt64(1);
        }

        // Otzaria (shamor_zachor_data_provider.dart `_buildSectionsFromToc`) only builds sections from entries whose parentId is NULL.
        // If the book has no such root (no TOC at all, or an orphaned forest) it has no sections and falls back to totalLines.
        var children = new Dictionary<long, List<long>>();
        var roots = new List<long>();
        foreach (var (id, parent) in parentOf)
        {
            if (parent is null) roots.Add(id);
            else
            {
                if (!children.TryGetValue(parent.Value, out var list)) children[parent.Value] = list = [];
                list.Add(id);
            }
        }

        if (roots.Count == 0)
        {
            // One item per line (max(totalLines,1)), doubled for Talmud (a "בבלי"/"ירושלמי" category in the chain).
            int totalLines;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT totalLines FROM book WHERE id = $id";
                cmd.Parameters.AddWithValue("$id", bookId);
                var o = cmd.ExecuteScalar();
                if (o is null or DBNull) return null; // unknown book
                totalLines = Convert.ToInt32(o, System.Globalization.CultureInfo.InvariantCulture);
            }
            var n = Math.Max(totalLines, 1);
            if (IsDafContent(conn, bookId)) n *= 2;
            return n;
        }

        // Leaves of the TOC forest reachable from the roots (that's what Otzaria's DFS visits).
        var leaves = 0;
        var stack = new Stack<long>(roots);
        var seen = new HashSet<long>();
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!seen.Add(id)) continue; // cycle guard
            if (children.TryGetValue(id, out var kids) && kids.Count > 0)
                foreach (var k in kids) stack.Push(k);
            else leaves++;
        }
        return leaves;
    }

    /// <summary>
    /// Mirrors `_buildRecursiveCategory`: walking the category chain top-down, a title containing בבלי/ירושלמי switches the content type to
    /// "דף" (two items per page), a title containing תנ"ך switches it back to text; otherwise the parent's type is inherited.
    /// </summary>
    private static bool IsDafContent(SqliteConnection conn, int bookId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT c.title FROM category_closure cc " +
            "JOIN category c ON c.id = cc.ancestorId " +
            "WHERE cc.descendantId = (SELECT categoryId FROM book WHERE id = $id) " +
            "ORDER BY c.level, c.id";
        cmd.Parameters.AddWithValue("$id", bookId);
        using var r = cmd.ExecuteReader();
        var daf = false;
        while (r.Read())
        {
            var t = r.IsDBNull(0) ? "" : r.GetString(0);
            if (t.Contains("בבלי", StringComparison.Ordinal) || t.Contains("ירושלמי", StringComparison.Ordinal)) daf = true;
            else if (t.Contains("תנ\"ך", StringComparison.Ordinal)) daf = false;
        }
        return daf;
    }

    // ------------------------------------------------------------------------------------------
    public (string Title, string? CategoryPath)? GetBookInfo(int bookId)
    {
        if (_infos.TryGetValue(bookId, out var cached)) return cached;
        try
        {
            using var conn = Open();
            string title;
            long categoryId;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT title, categoryId FROM book WHERE id = $id";
                cmd.Parameters.AddWithValue("$id", bookId);
                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;
                title = r.GetString(0);
                categoryId = r.IsDBNull(1) ? 0 : r.GetInt64(1);
            }

            var parts = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT c.title FROM category_closure cc JOIN category c ON c.id = cc.ancestorId " +
                    "WHERE cc.descendantId = $cat ORDER BY c.level, c.id";
                cmd.Parameters.AddWithValue("$cat", categoryId);
                using var r = cmd.ExecuteReader();
                while (r.Read()) if (!r.IsDBNull(0)) parts.Add(r.GetString(0));
            }

            var info = (title, parts.Count == 0 ? null : string.Join(", ", parts));
            _infos[bookId] = info;
            return info;
        }
        catch
        {
            return null;
        }
    }

    public int? FindBookIdByTitle(string title)
    {
        if (string.IsNullOrEmpty(title)) return null;
        if (_titleIds.TryGetValue(title, out var cached)) return cached;
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id FROM book WHERE title = $t ORDER BY id LIMIT 1";
            cmd.Parameters.AddWithValue("$t", title);
            var o = cmd.ExecuteScalar();
            if (o is null or DBNull) return null;
            var id = Convert.ToInt32(o, System.Globalization.CultureInfo.InvariantCulture);
            _titleIds[title] = id;
            return id;
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------------------------------------
    private SqliteConnection Open()
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();
        var conn = new SqliteConnection(cs);
        try
        {
            conn.Open();
            using var pragma = conn.CreateCommand();
            pragma.CommandText = "PRAGMA busy_timeout = 2000";
            pragma.ExecuteNonQuery();
            return conn;
        }
        catch
        {
            conn.Dispose();
            throw;
        }
    }
}
