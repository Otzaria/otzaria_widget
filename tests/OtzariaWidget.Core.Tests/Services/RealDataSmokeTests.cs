using OtzariaWidget.Core.Contracts;
using OtzariaWidget.Core.Paths;
using OtzariaWidget.Core.Services;
using OtzariaWidget.Core.Settings;
using OtzariaWidget.Core.UserState;
using Xunit.Abstractions;

namespace OtzariaWidget.Core.Tests.Services;

/// <summary>Runs against the real %APPDATA%\otzaria data if present (otherwise silently passes). Prints counts only.</summary>
public class RealDataSmokeTests(ITestOutputHelper output)
{
    private static string? RealRoot()
    {
        var appData = Environment.GetEnvironmentVariable("APPDATA");
        if (string.IsNullOrEmpty(appData)) return null;
        var root = Path.Combine(appData, "otzaria");
        return Directory.Exists(root) ? root : null;
    }

    [Fact]
    public void Real_paths_resolve()
    {
        if (RealRoot() is null) return;
        var p = new OtzariaPathResolver().Resolve(new WidgetAppSettings());
        output.WriteLine($"dataRoot={(p.DataRoot is null ? "null" : "found")} userState={(p.UserStateDb is null ? "null" : "found")} " +
                         $"notes={(p.PersonalNotesDb is null ? "null" : "found")} hive={(p.PreferencesHive is null ? "null" : "found")} " +
                         $"library={(p.LibraryDb is null ? "null" : "found")} exe={(p.OtzariaExe is null ? "null" : "found")} portable={p.IsPortable}");
        foreach (var d in p.Diagnostics) output.WriteLine("  diag: " + d.Length + " chars");
        Assert.NotNull(p.DataRoot);
        Assert.NotNull(p.UserStateDb);
        Assert.NotNull(p.PreferencesHive);
        Assert.NotEmpty(p.Diagnostics);
    }

    [Fact]
    public void Real_user_state_reads()
    {
        if (RealRoot() is null) return;
        var p = new OtzariaPathResolver().Resolve(new WidgetAppSettings());
        if (p.UserStateDb is null) return;
        var r = new UserStateReader();
        Assert.Equal(DataHealth.Ok, r.Probe(p.UserStateDb).Health);

        var books = r.ReadRecentBooks(p.UserStateDb, 200);
        var searches = r.ReadRecentSearches(p.UserStateDb, 200);
        var bookmarks = r.ReadBookmarks(p.UserStateDb);
        var groups = r.ReadBookmarkGroups(p.UserStateDb);
        var ws = r.ReadWorkspaces(p.UserStateDb);
        var tabs = r.ReadOpenTabs(p.UserStateDb);
        output.WriteLine($"recentBooks={books.Count} (linkable {books.Count(b => b.DeepLink is not null)}, pdf {books.Count(b => b.Book.IsPdf)}) " +
                         $"searches={searches.Count} bookmarks={bookmarks.Count} groups={groups.Count} workspaces={ws.Count} tabs={tabs.Count} (current {tabs.Count(t => t.IsCurrent)})");

        Assert.True(books.Count <= 200);
        Assert.Equal(books.Count, books.Select(b => (b.Book.Id, b.Book.Type, b.Book.Source, b.Book.Title)).Distinct().Count());
        Assert.All(books, b => Assert.False(string.IsNullOrWhiteSpace(b.Book.Title)));
        Assert.All(books.Where(b => b.DeepLink is not null), b => Assert.StartsWith("otzaria://open/", b.DeepLink));
        Assert.All(searches, s => Assert.False(string.IsNullOrWhiteSpace(s.Query)));
        Assert.All(searches, s => Assert.StartsWith("otzaria://open/search?q=", s.DeepLink));
        Assert.True(tabs.Count(t => t.IsCurrent) <= 1);
    }

    [Fact]
    public void Real_data_service_snapshot_does_not_crash()
    {
        if (RealRoot() is null) return;
        var dir = Path.Combine(Path.GetTempPath(), "otz-real-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new JsonSettingsStore(dir);
            using var svc = new OtzariaDataService(store);
            var s = svc.GetSnapshot();
            output.WriteLine($"snapshot: recent={s.RecentBooks.Count} searches={s.RecentSearches.Count} bookmarks={s.Bookmarks.Count} " +
                             $"workspaces={s.Workspaces.Count} tabs={s.OpenTabs.Count} progress={s.Progress.Count}");
            foreach (var st in s.Status) output.WriteLine($"  status {st.Name}: {st.Health}");
            Assert.NotNull(s.Paths.DataRoot);
            Assert.Contains(s.Status, st => st.Name == "user_state.db" && st.Health == DataHealth.Ok);
            Assert.True(s.RecentBooks.Count <= 30 && s.RecentSearches.Count <= 20);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
