using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OtzariaWidget.Core.Contracts;
using OtzariaWidget.Core.Launch;

namespace OtzariaWidget.Core.UserState;

/// <summary>Optional capability of the user-state reader: classify the DB (exists / readable / known schema).</summary>
public interface IUserStateSchemaProbe
{
    SourceStatus Probe(string userStateDbPath);
}

/// <summary>Optional capability: a cheap fingerprint that changes whenever Otzaria commits to the lists / window sessions.</summary>
public interface IUserStateChangeProbe
{
    /// <returns>null when the database cannot be read right now.</returns>
    string? GetChangeToken(string userStateDbPath);
}

/// <summary>
/// Reads Otzaria's user_state.db strictly read-only (storage-and-links.md §2).
/// I/O failures (after retries) propagate as exceptions; schema problems yield empty results
/// (use <see cref="Probe"/> to tell them apart).
/// </summary>
public sealed class UserStateReader : IUserStateReader, IUserStateSchemaProbe, IUserStateChangeProbe
{
    public const string SourceName = "user_state.db";
    private const int SupportedUserVersion = 1;

    private static readonly HashSet<string> KnownBookTypes = new(StringComparer.Ordinal)
    {
        "TextBook", "PdfBook", "DocxBook", "EpubBook", "DocumentBook", "ExternalLibraryBook",
    };

    private readonly IOtzariaLauncher _launcher;

    public UserStateReader() : this(new OtzariaLauncher()) { }

    public UserStateReader(IOtzariaLauncher launcher)
    {
        _launcher = launcher;
    }

    // ==========================================================================================
    //  Public API
    // ==========================================================================================
    public SourceStatus Probe(string userStateDbPath)
    {
        if (string.IsNullOrWhiteSpace(userStateDbPath) || !File.Exists(userStateDbPath))
            return new SourceStatus(SourceName, DataHealth.NotFound, "לא נמצא קובץ user_state.db");
        try
        {
            return WithConnection(userStateDbPath, conn =>
            {
                var problem = CheckSchema(conn);
                return problem is null
                    ? new SourceStatus(SourceName, DataHealth.Ok, null)
                    : new SourceStatus(SourceName, DataHealth.UnknownSchema, problem);
            });
        }
        catch (Exception ex)
        {
            return new SourceStatus(SourceName, DataHealth.Unreadable, "לא ניתן לקרוא את user_state.db: " + Describe(ex));
        }
    }

    /// <summary>
    /// `lists.updated_at` (epoch ms of the last write of each whole list) and `window_sessions.updated_at` change on every Otzaria
    /// commit. Needed because NTFS sends no change notification for appends to the -wal file that Otzaria holds open.
    /// </summary>
    public string? GetChangeToken(string userStateDbPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(userStateDbPath) || !File.Exists(userStateDbPath)) return null;
            return WithConnection(userStateDbPath, conn =>
            {
                if (CheckSchema(conn) is not null) return null;
                var sb = new System.Text.StringBuilder();
                if (HasTable(conn, "lists"))
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT box, key, updated_at FROM lists WHERE box IN ('history','bookmarks','workspaces') ORDER BY box, key";
                    using var r = cmd.ExecuteReader();
                    while (r.Read()) sb.Append(r.GetString(0)).Append('/').Append(r.GetString(1)).Append('=').Append(r.GetInt64(2)).Append(';');
                }
                if (HasTable(conn, "window_sessions"))
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT slot, updated_at, current_index FROM window_sessions WHERE slot > 0 ORDER BY slot";
                    using var r = cmd.ExecuteReader();
                    while (r.Read()) sb.Append('s').Append(r.GetInt64(0)).Append('=').Append(r.GetInt64(1)).Append(':').Append(r.IsDBNull(2) ? 0 : r.GetInt64(2)).Append(';');
                }
                return sb.ToString();
            });
        }
        catch { return null; }
    }

    public IReadOnlyList<ReadingEntry> ReadRecentBooks(string userStateDbPath, int max)
    {
        if (max <= 0) return [];
        var result = new List<ReadingEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in ParseList(ReadList(userStateDbPath, "history", "history")))
        {
            if (item.IsSearch) continue;
            if (!seen.Add(item.Identity)) continue; // keep newest (first) occurrence per book identity
            result.Add(item.Entry);
            if (result.Count >= max) break;
        }
        return result;
    }

    public IReadOnlyList<RecentSearch> ReadRecentSearches(string userStateDbPath, int max)
    {
        if (max <= 0) return [];
        var result = new List<RecentSearch>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in ParseList(ReadList(userStateDbPath, "history", "history")))
        {
            if (!item.IsSearch) continue;
            var query = item.Entry.Book.Title;
            if (string.IsNullOrWhiteSpace(query)) continue;
            if (!seen.Add(query + "\u0001" + item.SearchMode)) continue;
            result.Add(new RecentSearch
            {
                Query = query,
                Mode = item.SearchMode,
                DisplayText = string.IsNullOrWhiteSpace(item.Entry.Ref) ? null : item.Entry.Ref,
                DeepLink = SafeSearchLink(query, item.SearchMode),
            });
            if (result.Count >= max) break;
        }
        return result;
    }

    public IReadOnlyList<ReadingEntry> ReadBookmarks(string userStateDbPath)
    {
        var items = ParseList(ReadList(userStateDbPath, "bookmarks", "key-bookmarks"))
            .Where(i => !i.IsSearch)
            .Select(i => i.Entry)
            .ToList();
        items.Reverse(); // stored oldest-first
        return items;
    }

    public IReadOnlyList<BookmarkGroup> ReadBookmarkGroups(string userStateDbPath)
    {
        var groups = new List<BookmarkGroup>();
        using var doc = ReadList(userStateDbPath, "bookmarks", "key-bookmark-groups");
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Array) return groups;
        foreach (var g in doc.RootElement.EnumerateArray())
        {
            try
            {
                if (g.ValueKind != JsonValueKind.Object) continue;
                var id = Str(g, "id");
                if (id is null) continue;
                var items = new List<ReadingEntry>();
                if (g.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var it in arr.EnumerateArray())
                    {
                        var p = TryParseBookmark(it);
                        if (p is { IsSearch: false }) items.Add(p.Entry);
                    }
                }
                groups.Add(new BookmarkGroup
                {
                    Id = id,
                    Name = Str(g, "name") ?? "",
                    Items = items,
                    CreatedAt = ParseDate(Str(g, "createdAt")),
                });
            }
            catch { /* skip broken group */ }
        }
        groups.Reverse(); // newest first
        return groups;
    }

    public IReadOnlyList<WorkspaceInfo> ReadWorkspaces(string userStateDbPath)
    {
        var activeId = WithConnection(userStateDbPath, conn =>
        {
            if (CheckSchema(conn) is not null || !HasTable(conn, "window_sessions")) return null;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT active_workspace_id FROM window_sessions WHERE slot = 1";
            var o = cmd.ExecuteScalar();
            return o is string s && s.Length > 0 ? s : null;
        });

        var result = new List<WorkspaceInfo>();
        using var doc = ReadList(userStateDbPath, "workspaces", "key-workspaces");
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Array) return result;
        foreach (var w in doc.RootElement.EnumerateArray())
        {
            try
            {
                if (w.ValueKind != JsonValueKind.Object) continue;
                // Workspace.fromJson requires `name` (a nameless entry fails to decode) but accepts a missing id (it generates one)
                var wsName = Str(w, "name");
                if (wsName is null) continue;
                var id = Str(w, "id") ?? ("#" + result.Count.ToString(CultureInfo.InvariantCulture));
                var titles = new List<string>();
                var count = 0;
                if (w.TryGetProperty("tabs", out var tabs) && tabs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in tabs.EnumerateArray())
                    {
                        // Workspace.fromJson skips broken/unknown tabs and never stores PdfCommentatorsTab
                        if (t.ValueKind != JsonValueKind.Object) continue;
                        var tt = Str(t, "type");
                        if (tt is null || !KnownTabTypes.Contains(tt) || tt == "PdfCommentatorsTab") continue;
                        count++;
                        try { titles.Add(TabTitle(t)); } catch { titles.Add(""); }
                    }
                }
                result.Add(new WorkspaceInfo
                {
                    Id = id,
                    Name = wsName,
                    TabCount = count,
                    TabTitles = titles,
                    IsPinned = Bool(w, "isPinned"),
                    IsActive = activeId is not null && activeId == id,
                });
            }
            catch { /* skip broken workspace */ }
        }
        return result;
    }

    public IReadOnlyList<OpenTabInfo> ReadOpenTabs(string userStateDbPath)
    {
        var row = WithConnection(userStateDbPath, conn =>
        {
            if (CheckSchema(conn) is not null || !HasTable(conn, "window_sessions")) return ((string, int)?)null;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT tabs_json, current_index FROM window_sessions WHERE slot = 1";
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            return (r.IsDBNull(0) ? "[]" : r.GetString(0), r.IsDBNull(1) ? 0 : (int)r.GetInt64(1));
        });
        var result = new List<OpenTabInfo>();
        if (row is null) return result;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(row.Value.Item1, DocOptions); }
        catch (JsonException) { return result; }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
            var i = 0;
            var tabCount = doc.RootElement.GetArrayLength();
            var current = ResolveCurrentIndex(doc.RootElement, row.Value.Item2, tabCount);
            foreach (var tab in doc.RootElement.EnumerateArray())
            {
                var index = i++;
                try
                {
                    if (tab.ValueKind != JsonValueKind.Object) continue;
                    var type = Str(tab, "type");
                    if (type is null) continue; // OpenedTab.fromJson cannot decode a tab without a type
                    var reading = ResolveTabReading(tab, 0);
                    string? link = reading is null ? null : reading.DeepLink;
                    if (reading is null && (type is "SearchingTabWindow" or "SearchingTab"))
                    {
                        var q = Str(tab, "searchText");
                        if (!string.IsNullOrWhiteSpace(q)) link = SafeSearchLink(q, null);
                    }
                    result.Add(new OpenTabInfo
                    {
                        Title = TabTitle(tab),
                        TabType = type,
                        Reading = reading,
                        IsCurrent = index == current,
                        TabIndex = index,
                        DeepLink = link,
                    });
                }
                catch { /* skip broken tab */ }
            }
        }
        return result;
    }

    /// <summary>TabsRepository._resolvePersistedCurrentTabIndex: clamp, then fall back to the nearest restorable tab (previous first).</summary>
    private static int ResolveCurrentIndex(JsonElement tabs, int saved, int count)
    {
        if (count == 0) return -1;
        var restorable = new bool[count];
        var n = 0;
        foreach (var t in tabs.EnumerateArray())
        {
            restorable[n] = t.ValueKind == JsonValueKind.Object && Str(t, "type") is not null;
            n++;
        }
        var cur = Math.Clamp(saved, 0, count - 1);
        if (restorable[cur]) return cur;
        for (var k = cur - 1; k >= 0; k--) if (restorable[k]) return k;
        for (var k = cur + 1; k < count; k++) if (restorable[k]) return k;
        return -1;
    }

    // ==========================================================================================
    //  Parsing
    // ==========================================================================================
    private static readonly HashSet<string> KnownTabTypes = new(StringComparer.Ordinal)
    {
        "TextBookTab", "PdfBookTab", "CombinedTab", "ToolTab", "SearchingTabWindow", "SearchingTab", "SemanticSearchTab",
        "CommentatorsTab", "PdfCommentatorsTab",
    };

    private static readonly JsonDocumentOptions DocOptions = new() { AllowTrailingCommas = true };

    private sealed record Parsed(ReadingEntry Entry, bool IsSearch, string? SearchMode, string Identity);

    private JsonDocument? ReadList(string path, string box, string key)
    {
        var payload = WithConnection(path, conn =>
        {
            if (CheckSchema(conn) is not null || !HasTable(conn, "lists")) return null;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT payload_json FROM lists WHERE box = $b AND key = $k";
            cmd.Parameters.AddWithValue("$b", box);
            cmd.Parameters.AddWithValue("$k", key);
            return cmd.ExecuteScalar() as string;
        });
        if (string.IsNullOrWhiteSpace(payload)) return null;
        try { return JsonDocument.Parse(payload, DocOptions); }
        catch (JsonException) { return null; }
    }

    /// <summary>Parses every element of a JSON array payload, skipping broken ones. Materializes before the document is disposed.</summary>
    private List<Parsed> ParseList(JsonDocument? doc)
    {
        var list = new List<Parsed>();
        if (doc is null) return list;
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var p = TryParseBookmark(el);
                if (p is not null) list.Add(p);
            }
        }
        return list;
    }

    private Parsed? TryParseBookmark(JsonElement el)
    {
        try
        {
            if (el.ValueKind != JsonValueKind.Object) return null;
            if (!el.TryGetProperty("book", out var bookEl) || bookEl.ValueKind != JsonValueKind.Object) return null;

            var isSearch = Bool(el, "isSearch");
            var book = ParseBook(bookEl, allowAnyType: isSearch, out var identity);
            if (book is null) return null;

            var index = Int(el, "index")
                ?? (el.TryGetProperty("readingAnchor", out var a) && a.ValueKind == JsonValueKind.Object ? Int(a, "line") : null)
                ?? 0;

            var commentators = new List<string>();
            if (el.TryGetProperty("commentatorsToShow", out var cs) && cs.ValueKind == JsonValueKind.Array)
                foreach (var c in cs.EnumerateArray())
                    if (c.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(c.GetString())) commentators.Add(c.GetString()!);

            var targetKind = Str(el, "targetKind") == "commentators" ? "commentators" : "book";

            var entry = new ReadingEntry
            {
                Book = book,
                Ref = Str(el, "ref") ?? "",
                Index = index,
                TargetKind = targetKind,
                Commentators = commentators,
                Label = Str(el, "label"),
                CreatedAt = ParseDate(Str(el, "createdAt")),
                WorkspaceName = Str(el, "workspaceName"),
            };
            if (!isSearch) entry = entry with { DeepLink = SafeBuildLink(entry) };

            string? mode = null;
            if (isSearch && Str(el, "searchMode") is { } m) mode = m.Trim().ToLowerInvariant();

            return new Parsed(entry, isSearch, mode, identity);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Parses the polymorphic book object (§2.3.1). Returns null for unknown types (unless allowAnyType) or titleless books.</summary>
    private static BookRef? ParseBook(JsonElement b, bool allowAnyType, out string identity)
    {
        identity = "";
        var type = Str(b, "type") ?? (allowAnyType ? "TextBook" : null);
        if (type is null) return null;
        if (!allowAnyType && !KnownBookTypes.Contains(type)) return null;

        var title = Str(b, "title") ?? Str(b, "bookName");
        if (string.IsNullOrWhiteSpace(title)) return null;

        var isExternal = type == "ExternalLibraryBook";
        // Otzaria's ExternalLibraryBook keeps the catalog id in `otzarId` (parsed as its `id`); it is not an Otzaria book id,
        // so it is used for the identity only and never exposed as BookRef.Id (such books are not deep-linkable).
        int? id = Int(b, "id");
        int? identityId = isExternal ? (id ?? Int(b, "otzarId")) : id;
        if (isExternal) id = null;
        if (id is <= 0) id = null;

        // BookSource.fromJson: only "o" / "u" / "d:<valid slug>" are recognised; anything else falls back to the legacy flag.
        var source = Str(b, "source");
        if (!(source is "o" or "u" || (source is not null && source.StartsWith("d:", StringComparison.Ordinal) && OtzariaLauncher.IsValidSlug(source[2..]))))
            source = Bool(b, "isUserBook") ? "u" : "o";

        var externalId = NullIfEmpty(Str(b, "externalLibraryId"));
        if (externalId is null && isExternal)
        {
            externalId = b.TryGetProperty("otzarId", out var oz)
                ? oz.ValueKind switch
                {
                    JsonValueKind.String => NullIfEmpty(oz.GetString()),
                    JsonValueKind.Number => oz.GetRawText(),
                    _ => null,
                }
                : null;
        }

        var path = NullIfEmpty(Str(b, "path"));
        var filePath = NullIfEmpty(Str(b, "filePath"));
        var fileType = Str(b, "fileType");
        var versionTitle = type == "TextBook" ? Str(b, "versionTitle") : null; // Dart: only TextBook has versionTitle
        var categoryId = Int(b, "categoryId");

        // bookIdentity (lib/bookmarks/models/bookmark.dart); `path` only counts for FileBook types (not TextBook / external)
        var isFileBook = type is "PdfBook" or "DocxBook" or "EpubBook" or "DocumentBook";
        string baseId;
        if (identityId is not null) baseId = $"id:{identityId}" + (type == "PdfBook" ? "|pdf" : "");
        else if (externalId is not null) baseId = $"external:{externalId}";
        else if (isFileBook && path is not null) baseId = $"file:{path}";
        else if (filePath is not null) baseId = $"filePath:{filePath}";
        else if (categoryId is not null) baseId = $"category:{categoryId}|title:{title}|type:{fileType}";
        else baseId = $"title:{title}";
        if (source != "o") baseId += $"|src:{source}";
        if (versionTitle is not null) baseId += $"|version:{versionTitle}";
        identity = baseId;

        return new BookRef
        {
            Id = id,
            Title = title,
            Type = type,
            Source = source,
            Author = Str(b, "author"),
            CategoryPath = Str(b, "categoryPath"),
            FileType = fileType,
            Path = path,
            ExternalLibraryId = externalId,
            VersionTitle = versionTitle,
        };
    }

    // ---- tabs -----------------------------------------------------------------------------------
    private ReadingEntry? ResolveTabReading(JsonElement tab, int depth)
    {
        if (depth > 4 || tab.ValueKind != JsonValueKind.Object) return null;
        var type = Str(tab, "type");
        switch (type)
        {
            case "TextBookTab":
            {
                if (!tab.TryGetProperty("book", out var b) || b.ValueKind != JsonValueKind.Object) return null;
                var book = ParseBook(b, false, out _);
                if (book is null) return null;
                // bookmark_from_tab.dart: `anchor?.line ?? tab.index` (a valid anchor wins over the stored tab index)
                var idx = AnchorLine(tab) ?? Int(tab, "initalIndex") ?? Int(tab, "initialIndex") ?? 0;
                var commentators = StrList(tab, "commentators");
                return Link(new ReadingEntry { Book = book, Index = idx, Commentators = commentators });
            }
            case "PdfBookTab":
            {
                BookRef? book = null;
                if (tab.TryGetProperty("book", out var b) && b.ValueKind == JsonValueKind.Object)
                    book = ParseBook(b, false, out _);
                if (book is null) return null;
                var page = Math.Max(Int(tab, "pageNumber") ?? 1, 1);
                return Link(new ReadingEntry { Book = book, Index = page, Commentators = StrList(tab, "activeCommentators") });
            }
            case "CombinedTab":
            {
                foreach (var side in new[] { "rightTab", "leftTab" })
                {
                    if (tab.TryGetProperty(side, out var inner))
                    {
                        var r = ResolveTabReading(inner, depth + 1);
                        if (r is not null) return r;
                    }
                }
                return null;
            }
            case "CommentatorsTab":
            case "PdfCommentatorsTab":
            {
                if (!tab.TryGetProperty("sourceTab", out var src)) return null;
                var r = ResolveTabReading(src, depth + 1);
                if (r is null) return null;
                var selected = StrList(tab, "selectedCommentators");
                // CommentatorsTab.currentIndex (persisted as `initialIndex`) is where the commentary view is, not the book tab's position
                var pos = type == "CommentatorsTab" && Int(tab, "initialIndex") is { } ci && ci >= 0 ? ci : r.Index;
                return Link(r with
                {
                    TargetKind = "commentators",
                    Index = pos,
                    Commentators = selected.Count > 0 ? selected : r.Commentators,
                });
            }
            default:
                return null;
        }
    }

    /// <summary>ReadingAnchor.tryFromJson: `line` must be an int &gt;= 0 (invalid anchors are treated as absent).</summary>
    private static int? AnchorLine(JsonElement obj)
    {
        if (!obj.TryGetProperty("readingAnchor", out var a) || a.ValueKind != JsonValueKind.Object) return null;
        var line = Int(a, "line");
        return line is >= 0 ? line : null;
    }

    private ReadingEntry Link(ReadingEntry e) => e with { DeepLink = SafeBuildLink(e) };

    private static string TabTitle(JsonElement tab)
    {
        if (tab.ValueKind != JsonValueKind.Object) return "";
        var title = Str(tab, "title");
        if (!string.IsNullOrWhiteSpace(title)) return title;
        if (tab.TryGetProperty("book", out var b) && b.ValueKind == JsonValueKind.Object)
        {
            var t = Str(b, "title") ?? Str(b, "bookName");
            if (!string.IsNullOrWhiteSpace(t)) return t;
        }
        var bt = Str(tab, "bookTitle") ?? Str(tab, "searchText");
        if (!string.IsNullOrWhiteSpace(bt)) return bt;
        foreach (var name in new[] { "sourceTab", "rightTab", "leftTab" })
        {
            if (tab.TryGetProperty(name, out var inner) && inner.ValueKind == JsonValueKind.Object)
            {
                var t = TabTitle(inner);
                if (t.Length > 0) return t;
            }
        }
        return Str(tab, "type") ?? "";
    }

    // ---- JSON helpers ---------------------------------------------------------------------------
    private static string? Str(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static int? Int(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var v)) return null;
        switch (v.ValueKind)
        {
            case JsonValueKind.Number:
                if (v.TryGetInt32(out var i)) return i;
                if (v.TryGetDouble(out var d) && double.IsFinite(d) && d >= int.MinValue && d <= int.MaxValue && Math.Abs(d - Math.Round(d)) < 1e-9)
                    return (int)Math.Round(d);
                return null;
            case JsonValueKind.String:
                return int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : null;
            default:
                return null;
        }
    }

    private static bool Bool(JsonElement obj, string name)
        => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static List<string> StrList(JsonElement obj, string name)
    {
        var list = new List<string>();
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var e in arr.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(e.GetString())) list.Add(e.GetString()!);
        return list;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private static DateTimeOffset? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d) ? d : null;
    }

    private string? SafeBuildLink(ReadingEntry e)
    {
        try { return _launcher.BuildLink(e); } catch { return null; }
    }

    private string? SafeSearchLink(string q, string? mode)
    {
        try { return _launcher.BuildSearchLink(q, mode); } catch { return null; }
    }

    // ==========================================================================================
    //  SQLite access
    // ==========================================================================================
    private static string? CheckSchema(SqliteConnection conn)
    {
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA user_version";
            var v = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (v > SupportedUserVersion)
                return $"גרסת הסכמה לא מוכרת של user_state.db ({v}); ייתכן שנדרש עדכון של הווידג'ט";
        }
        if (!HasTable(conn, "lists") && !HasTable(conn, "window_sessions"))
            return "מבנה user_state.db לא מוכר (חסרות הטבלאות lists/window_sessions)";
        return null;
    }

    private static bool HasTable(SqliteConnection conn, string name)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $n";
        cmd.Parameters.AddWithValue("$n", name);
        return cmd.ExecuteScalar() is not null;
    }

    private static T WithConnection<T>(string path, Func<SqliteConnection, T> work)
    {
        try
        {
            return WithRetry(() => RunOn(path, work));
        }
        catch (SqliteException ex) when ((ex.SqliteErrorCode is 14 or 8) && File.Exists(path))
        {
            // Directory not writable / -shm cannot be created: read a private copy (db + wal + shm) instead.
            return RunOnCopy(path, work);
        }
    }

    private static T RunOn<T>(string path, Func<SqliteConnection, T> work)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();
        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA busy_timeout = 1500";
            pragma.ExecuteNonQuery();
        }
        return work(conn);
    }

    private const string CopyDirPrefix = "OtzariaWidget-uscopy-";

    internal static T RunOnCopy<T>(string path, Func<SqliteConnection, T> work)
    {
        CleanStaleCopies();
        var dir = Path.Combine(Path.GetTempPath(), CopyDirPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var target = Path.Combine(dir, "user_state.db");
            CopyConsistent(path, target);
            return WithRetry(() => RunOn(target, work));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Copies user_state.db and its -wal (NOT the -shm: a copied wal-index can disagree with the copied wal; SQLite rebuilds
    /// it from the -wal in the writable temp folder). Retries while Otzaria writes/checkpoints during the copy.
    /// </summary>
    private static void CopyConsistent(string path, string target)
    {
        for (var attempt = 1; ; attempt++)
        {
            var before = (Signature(path), Signature(path + "-wal"));
            CopyShared(path, target);
            if (File.Exists(path + "-wal")) CopyShared(path + "-wal", target + "-wal");
            else if (File.Exists(target + "-wal")) File.Delete(target + "-wal");
            var after = (Signature(path), Signature(path + "-wal"));
            if (before == after || attempt >= 4) return;
            Thread.Sleep(50 * attempt);
        }
    }

    private static (long Length, long Ticks) Signature(string file)
    {
        try
        {
            var fi = new FileInfo(file);
            return fi.Exists ? (fi.Length, fi.LastWriteTimeUtc.Ticks) : (-1, 0);
        }
        catch { return (-2, 0); }
    }

    /// <summary>A crash between copy and delete would leave personal data in %TEMP%: remove old leftovers.</summary>
    private static void CleanStaleCopies()
    {
        try
        {
            foreach (var d in Directory.EnumerateDirectories(Path.GetTempPath(), CopyDirPrefix + "*"))
            {
                try
                {
                    if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(d) > TimeSpan.FromMinutes(10))
                        Directory.Delete(d, recursive: true);
                }
                catch { }
            }
        }
        catch { }
    }

    private static void CopyShared(string from, string to)
    {
        using var src = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var dst = new FileStream(to, FileMode.Create, FileAccess.Write, FileShare.None);
        src.CopyTo(dst);
    }

    private static T WithRetry<T>(Func<T> action)
    {
        const int attempts = 4;
        for (var i = 1; ; i++)
        {
            try { return action(); }
            catch (SqliteException ex) when ((ex.SqliteErrorCode is 5 or 6) && i < attempts)
            {
                Thread.Sleep(80 * i);
            }
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        SqliteException s => $"SQLite {s.SqliteErrorCode}",
        _ => ex.GetType().Name,
    };
}
