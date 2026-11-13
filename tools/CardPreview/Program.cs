// Card preview tool. Usage:
//   dotnet run --project tools\CardPreview -c Release [-- --out <dir>] [--shots] [--no-real] [--now 2026-11-13T10:30:00+02:00]
// Writes <out>\index.html (synthetic demo data - safe to share) and <out>\real.html (the machine's real Otzaria data - private, never commit).
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using OtzariaWidget.App.Provider.Cards;
using OtzariaWidget.Calendar;
using OtzariaWidget.Core.Contracts;
using OtzariaWidget.Core.Services;
using OtzariaWidget.Core.Settings;
using OtzariaWidget.App.Tests;
using AdaptiveCards.Templating;

namespace OtzariaWidget.CardPreview;

internal static class Program
{
    private const string AcVersion = "3.0.4";
    
    private sealed record Scenario(string Kind, string Tag, string Size, string Light, string Dark, string Data, string Warnings);

    private static int Main(string[] args)
    {
        string root = FindRepoRoot();
        string outDir = Path.Combine(root, "artifacts", "card-preview");
        bool shots = false, real = true, dump = false;
        string? onlyKind = null;
        DateTimeOffset? now = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out": outDir = Path.GetFullPath(args[++i]); break;
                case "--shots": shots = true; break;
                case "--no-real": real = false; break;
                case "--dump": dump = true; break;
                case "--kind": onlyKind = args[++i]; break;
                case "--now": now = DateTimeOffset.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
            }
        }
        if (dump) { Dump(now ?? DateTimeOffset.Now); return 0; }
        Directory.CreateDirectory(outDir);
        CardTemplates.Directory = Path.Combine(root, "src", "OtzariaWidget.App", "Templates");

        CopyAssets(outDir);
        DownloadVendor(outDir);

        var pages = new List<string>();
        WritePage(Path.Combine(outDir, "index.html"), "Otzaria Widget - cards (demo data)", DemoScenarios(now ?? TestData.Now).ToList());
        pages.Add("index");

        var appData = Environment.GetEnvironmentVariable("APPDATA");
        if (real && !string.IsNullOrEmpty(appData) && Directory.Exists(Path.Combine(appData, "otzaria")))
        {
            try
            {
                WritePage(Path.Combine(outDir, "real.html"), "Otzaria Widget - cards (REAL data, private)", RealScenarios(now ?? DateTimeOffset.Now));
                pages.Add("real");
            }
            catch (Exception ex) { Console.Error.WriteLine("real data scenarios failed: " + ex.GetType().Name + " " + ex.Message); }
        }
        Console.WriteLine("gallery: " + Path.Combine(outDir, "index.html"));

        if (shots) Screenshots(outDir, pages, onlyKind);
        return 0;
    }

    // ----------------------------------------------------------------------------------------------------- scenarios
    /// <summary>Expands template + data with a fake $host (what the board does) for both themes, using the same .NET engine as the tests.</summary>
    private static Scenario Make(string kind, string tag, string size, CardResult r)
    {
        string Expand(string theme, out string warnings)
        {
            var t = new AdaptiveCardTemplate(CardTemplates.Load(r.TemplateFile));
            var host = $"{{\"widgetSize\":\"{size}\",\"hostTheme\":\"{theme}\",\"isUserContextAuthenticated\":true,\"isSettingsPayload\":false}}";
            var json = t.Expand(new EvaluationContext(r.DataJson, host));
            warnings = string.Join(" | ", t.GetLastTemplateExpansionWarnings().Cast<object>().Select(o => o.ToString()));
            return json;
        }
        var light = Expand("light", out var w1);
        var dark = Expand("dark", out var w2);
        return new(kind, tag, size, light, dark, r.DataJson, w1 + w2);
    }

    private static CardResult Safe(CardContext ctx) =>
        CardBuilders.BuildSafe(ctx, e => Console.Error.WriteLine($"  [{ctx.Kind}/{ctx.Size}] {e.GetType().Name}: {e.Message}"));

    private static IEnumerable<Scenario> DemoScenarios(DateTimeOffset now)
    {
        var cal = new FakeCalendarService();
        CardContext Ctx(string kind, string size, OtzariaSnapshot snap, WidgetCustomState? st = null, DateTimeOffset? at = null) =>
            new() { Kind = kind, Size = CardSizes.Parse(size), Settings = new WidgetAppSettings(), Calendar = cal, Now = at ?? now, Snapshot = snap, State = st ?? new() };

        foreach (var kind in WidgetKinds.All)
            foreach (var size in new[] { "small", "medium", "large" })
            {
                yield return Make(kind, "ok", size, Safe(Ctx(kind, size, TestData.Full())));
                yield return Make(kind, "empty", size, Safe(Ctx(kind, size, TestData.Empty())));
                yield return Make(kind, "missing", size, Safe(Ctx(kind, size, TestData.Missing())));
                yield return Make(kind, "customize", size, CardBuilders.BuildCustomizationSafe(Ctx(kind, size, TestData.Full())));
            }
        foreach (var (tag, at) in new[] { ("night", new DateTimeOffset(2026, 11, 13, 21, 0, 0, TimeSpan.FromHours(2))),
                                          ("dawn", new DateTimeOffset(2026, 11, 14, 4, 0, 0, TimeSpan.FromHours(2))),
                                          ("afternoon", new DateTimeOffset(2026, 11, 13, 14, 0, 0, TimeSpan.FromHours(2))) })
            foreach (var kind in new[] { WidgetKinds.Zmanim, WidgetKinds.HebrewCalendar, WidgetKinds.Dashboard })
                foreach (var size in new[] { "small", "medium", "large" })
                    yield return Make(kind, tag, size, Safe(Ctx(kind, size, TestData.Full(), at: at)));
    }

    private static List<Scenario> RealScenarios(DateTimeOffset now)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "otzaria-widget-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var store = new JsonSettingsStore(tmp);
        using var data = new OtzariaDataService(store);
        var cal = new CalendarService();
        var snap = data.GetSnapshot();
        var list = new List<Scenario>();
        CardContext Ctx(string kind, string size) =>
            new() { Kind = kind, Size = CardSizes.Parse(size), Settings = store.Load(), Calendar = cal, Now = now, Snapshot = snap, State = new() };
        foreach (var kind in WidgetKinds.All)
            foreach (var size in new[] { "small", "medium", "large" })
            {
                list.Add(Make(kind, "ok", size, Safe(Ctx(kind, size))));
                list.Add(Make(kind, "customize", size, CardBuilders.BuildCustomizationSafe(Ctx(kind, size))));
            }
        try { Directory.Delete(tmp, true); } catch (IOException) { }
        return list;
    }

    // ----------------------------------------------------------------------------------------------------- html
    private static void WritePage(string path, string title, List<Scenario> scenarios)
    {
        var arr = new JsonArray();
        foreach (var s in scenarios)
            arr.Add(new JsonObject
            {
                ["kind"] = s.Kind, ["tag"] = s.Tag, ["size"] = s.Size, ["name"] = WidgetKinds.DisplayName(s.Kind),
                ["light"] = s.Light, ["dark"] = s.Dark, ["warnings"] = s.Warnings,
            });
        var json = arr.ToJsonString(CardTemplates.JsonOptions).Replace("</", "<\\/");
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"he\" dir=\"rtl\"><head><meta charset=\"utf-8\"><title>" + title + "</title>");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine("<link rel=\"stylesheet\" href=\"gallery.css\">");
        sb.AppendLine($"<script src=\"vendor/adaptivecards-{AcVersion}.min.js\"></script>");
        sb.AppendLine("</head><body>");
        sb.AppendLine("<h1 id=\"title\">" + title + "</h1>");
        sb.AppendLine("<div id=\"root\"></div>");
        sb.AppendLine("<script>window.SCENARIOS = " + json + ";</script>");
        sb.AppendLine("<script src=\"gallery.js\"></script>");
        sb.AppendLine("</body></html>");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static void CopyAssets(string outDir)
    {
        var src = Path.Combine(AppContext.BaseDirectory, "assets");
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(outDir, Path.GetFileName(f)), true);
    }

    private static void DownloadVendor(string outDir)
    {
        var vendor = Path.Combine(outDir, "vendor");
        Directory.CreateDirectory(vendor);
        using var http = new HttpClient();
        foreach (var (name, url) in new[]
        {
            ($"adaptivecards-{AcVersion}.min.js", $"https://unpkg.com/adaptivecards@{AcVersion}/dist/adaptivecards.min.js")
        })
        {
            var target = Path.Combine(vendor, name);
            if (File.Exists(target) && new FileInfo(target).Length > 1000) continue;
            try { File.WriteAllBytes(target, http.GetByteArrayAsync(url).GetAwaiter().GetResult()); }
            catch (Exception ex) { Console.Error.WriteLine("download failed " + url + ": " + ex.Message); }
        }
    }

    // ----------------------------------------------------------------------------------------------------- screenshots
    private static void Screenshots(string outDir, List<string> pages, string? onlyKind)
    {
        const string edge = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
        if (!File.Exists(edge)) { Console.Error.WriteLine("msedge not found"); return; }
        var shotDir = Path.Combine(outDir, "shots");
        Directory.CreateDirectory(shotDir);
        foreach (var page in pages)
            foreach (var kind in WidgetKinds.All.Where(k => onlyKind is null || k == onlyKind))
                foreach (var (group, h) in new[] { ("ok", 1000), ("states", 2000), ("times", 1500), ("custom", 1000) })
                {
                    if (group == "times" && (page == "real" || kind is not (WidgetKinds.Zmanim or WidgetKinds.HebrewCalendar or WidgetKinds.Dashboard))) continue;
                    if (group == "states" && page == "real") continue;
                    var png = Path.Combine(shotDir, $"{page}-{kind}-{group}.png");
                    var url = new Uri(Path.Combine(outDir, page + ".html")).AbsoluteUri + $"?kind={kind}&group={group}";
                    var psi = new ProcessStartInfo(edge) { UseShellExecute = false, CreateNoWindow = true };
                    foreach (var a in new[] { "--headless=new", "--disable-gpu", "--hide-scrollbars", "--allow-file-access-from-files", "--force-device-scale-factor=1",
                                              "--virtual-time-budget=4000", $"--screenshot={png}", $"--window-size=1240,{h}", url })
                        psi.ArgumentList.Add(a);
                    using var p = Process.Start(psi)!;
                    p.WaitForExit(60000);
                }
        Console.WriteLine("screenshots: " + shotDir);
    }

    /// <summary>Console dump of the real calendar/progress shapes (private: do not paste into reports).</summary>
    private static void Dump(DateTimeOffset now)
    {
        var cal = new CalendarService();
        var city = cal.FindCity("ירושלים")!;
        var date = cal.GetEffectiveDate(now, city, "sunset");
        Console.WriteLine("city tz=" + city.TimeZoneId + " date=" + date);
        var ids = cal.ZmanimRegistry.Select(z => z.Id).ToList();
        foreach (var z in cal.GetZmanim(date, city, ids))
            Console.WriteLine($"{z.Id}	{z.Title}	{z.Time:O}	[{z.DisplayText}]");
        Console.WriteLine("defaults: " + string.Join(",", cal.DefaultEnabledZmanim));
        var di = cal.GetDayInfo(date, city);
        Console.WriteLine($"day: {di.HebrewDate} | {di.HebrewDayOfWeek} | {di.GregorianDate} | parasha={di.Parasha} | holidays={string.Join(";", di.Holidays)} | daf={di.DafYomiBavli} | yer={di.DafYomiYerushalmi} | omer={di.Omer} | cl={di.CandleLighting} | hv={di.Havdalah}");
        var tmp = Path.Combine(Path.GetTempPath(), "otzaria-widget-dump-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        using var data = new OtzariaDataService(new JsonSettingsStore(tmp));
        var snap = data.GetSnapshot();
        foreach (var st in snap.Status) Console.WriteLine($"status {st.Name} {st.Health} {st.Message}");
        Console.WriteLine($"recent={snap.RecentBooks.Count} bookmarks={snap.Bookmarks.Count} groups={snap.BookmarkGroups.Count} tabs={snap.OpenTabs.Count} progress={snap.Progress.Count} city={snap.Preferences.SelectedCity} 12h={snap.Preferences.TwelveHourClock} enabledZmanim={snap.Preferences.EnabledZmanim.Count}");
        foreach (var r in snap.RecentBooks.Take(8)) Console.WriteLine($"  recent: {r.Book.Title} | ref={r.Ref} | {r.DeepLink} | {r.CreatedAt}");
        foreach (var b in snap.Bookmarks.Take(4)) Console.WriteLine($"  bm: {b.Book.Title} | label={b.Label} | ref={b.Ref} | {b.DeepLink}");
        foreach (var p in snap.Progress.Take(6))
            Console.WriteLine($"  prog: {p.Title} head={p.Headline:F2} done={p.IsCompleted} inprog={p.IsInProgress} tracked={p.IsTracked} total={p.TotalItems} status={p.StatusText} cycle={p.CurrentCycleText} cols=" +
                              string.Join(",", p.VisibleColumns.Select(c => $"{c.Label}:{c.Done}/{c.Ratio:F2}")));
    }

    private static string FindRepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "OtzariaWidget.sln"))) d = d.Parent;
        return d?.FullName ?? Directory.GetCurrentDirectory();
    }
}
