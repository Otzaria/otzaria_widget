using System.Text.Json;
using System.Text.RegularExpressions;
using OtzariaWidget.App.Provider.Cards;
using OtzariaWidget.Calendar;
using OtzariaWidget.Core.Contracts;
using OtzariaWidget.Core.Services;
using OtzariaWidget.Core.Settings;

namespace OtzariaWidget.App.Tests;

/// <summary>
/// Builds every card from the machine's REAL Otzaria snapshot (JsonSettingsStore in a temp dir, real OtzariaDataService, real CalendarService).
/// Skipped silently when %APPDATA%\otzaria does not exist. Nothing personal is printed: assertion messages carry only kinds/sizes.
/// </summary>
public sealed class RealDataCardTests : IDisposable
{
    private readonly string? _tmp;
    private readonly JsonSettingsStore? _store;
    private readonly OtzariaDataService? _data;
    private readonly CalendarService _cal = new();
    private readonly OtzariaSnapshot? _snap;

    public RealDataCardTests()
    {
        var appData = Environment.GetEnvironmentVariable("APPDATA");
        if (string.IsNullOrEmpty(appData) || !Directory.Exists(Path.Combine(appData, "otzaria"))) return;
        _tmp = Path.Combine(Path.GetTempPath(), "otzaria-widget-cards-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
        _store = new JsonSettingsStore(_tmp);
        _data = new OtzariaDataService(_store);
        _snap = _data.GetSnapshot();
    }

    public void Dispose()
    {
        _data?.Dispose();
        if (_tmp is not null) { try { Directory.Delete(_tmp, true); } catch (IOException) { } }
    }

    private CardContext Ctx(string kind, string size, WidgetCustomState? st = null, DateTimeOffset? now = null) => new()
    {
        Kind = kind, Size = CardSizes.Parse(size), Settings = _store!.Load(), Calendar = _cal,
        Now = now ?? DateTimeOffset.Now, Snapshot = _snap, State = st ?? new WidgetCustomState(),
    };

    private static CardExpander.Expanded Expand(CardResult r, string size, string theme = "dark", bool auth = true) =>
        CardExpander.Expand(r, size, theme, auth);

    [Fact]
    public void Every_kind_size_theme_validates_with_real_data()
    {
        if (_snap is null) return;
        foreach (var kind in WidgetKinds.All)
            foreach (var size in CardExpander.Sizes)
            {
                var result = CardBuilders.BuildSafe(Ctx(kind, size), e => Assert.Fail($"{kind}/{size} threw {e.GetType().Name}: {e.Message}"));
                Assert.NotEqual(CardTemplates.Error, result.TemplateFile);
                foreach (var theme in CardExpander.Themes)
                    foreach (var auth in new[] { true, false })
                        CardExpander.Validate(Expand(result, size, theme, auth), false, auth, $"REAL {kind}/{size}/{theme}/auth={auth}");

                var custom = CardBuilders.BuildCustomizationSafe(Ctx(kind, size));
                Assert.Equal(CardTemplates.Customize, custom.TemplateFile);
                CardExpander.Validate(Expand(custom, size), true, true, $"REAL {kind}/{size}/customize");
            }
    }

    [Fact]
    public void Real_recent_books_show_real_titles_and_deep_links()
    {
        if (_snap is null || _snap.RecentBooks.Count == 0) return;
        var ex = Expand(CardBuilders.BuildSafe(Ctx(WidgetKinds.RecentBooks, "large")), "large");
        var texts = CardExpander.TextBlocks(ex.Root).ToList();
        Assert.Contains(texts, CardExpander.HasHebrew);
        var tab = _snap.OpenTabs.FirstOrDefault(t => t.IsCurrent && t.Reading is not null);
        var hero = tab?.Reading ?? _snap.RecentBooks[0];
        Assert.Contains(CardFormat.Clip(hero.Book.Title, 80), texts);

        // every link is either one of the entries' own deep links or a known Otzaria landing URL
        var known = _snap.RecentBooks.Select(b => b.DeepLink).Concat(_snap.OpenTabs.Select(t => t.DeepLink)).Where(u => u is not null).ToHashSet();
        var urls = CardExpander.Elements(ex.Root, "Action.OpenUrl").Select(a => a.GetProperty("url").GetString()!)
            .Concat(CardExpander.Descendants(ex.Root).Where(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("selectAction", out _))
                .Select(e => e.GetProperty("selectAction").GetProperty("url").GetString()!)).ToList();
        Assert.NotEmpty(urls);
        Assert.All(urls, u => Assert.True(known.Contains(u) || u.StartsWith("otzaria://open/", StringComparison.Ordinal), $"unexpected link kind {u.Split('?')[0]}"));
        // the hero is linkable: /book/<id>?index=<n> (or /pdf/...) and the card links to exactly that URL
        var heroLink = tab?.DeepLink ?? hero.DeepLink;
        if (heroLink is not null)
        {
            Assert.Matches(@"^otzaria://open/(book|pdf)/\d+\?index=\d+", heroLink);
            Assert.Contains(heroLink, urls);
        }
    }

    [Fact]
    public void Real_calendar_cards_show_todays_hebrew_date_and_city()
    {
        if (_snap is null) return;
        var ctx = Ctx(WidgetKinds.HebrewCalendar, "large");
        var city = CalendarContext.ResolveCity(ctx, null);
        var di = _cal.GetDayInfo(CalendarContext.Today(ctx, city), city);
        Assert.False(string.IsNullOrWhiteSpace(di.HebrewDate));
        var (day, _) = HebrewCalendarBuilder.SplitHebrewDate(di.HebrewDate);

        var cal = CardExpander.TextBlocks(Expand(CardBuilders.BuildSafe(ctx), "large").Root).ToList();
        Assert.Contains(day, cal);
        Assert.Contains(city.Name, cal);

        foreach (var size in new[] { "medium", "large" })
        {
            var zm = CardExpander.TextBlocks(Expand(CardBuilders.BuildSafe(Ctx(WidgetKinds.Zmanim, size)), size).Root).ToList();
            Assert.Contains(zm, t => t.Contains(city.Name));
            Assert.Contains(zm, t => Regex.IsMatch(t, @"^(\d{2}:\d{2}|\d{1,2}:\d{2} (AM|PM))$"));
            // our formatter, not Otzaria's DisplayText: no bidi isolates / seconds markers
            Assert.DoesNotContain(zm, t => t.Any(c => c is >= '⁦' and <= '⁩'));
            Assert.DoesNotContain(zm, t => Regex.IsMatch(t, @"^\d{2}:\d{2}[:.]$"));
        }
    }

    [Fact]
    public void Real_zmanim_selection_variants_all_render()
    {
        if (_snap is null) return;
        var ids = _cal.ZmanimRegistry.Select(z => z.Id).ToList();
        var st = new WidgetCustomState { ZmanimMode = "custom", ZmanimIds = ids };   // all ~98, including null-time and other-day entries
        foreach (var size in CardExpander.Sizes)
        {
            var r = CardBuilders.BuildSafe(Ctx(WidgetKinds.Zmanim, size, st));
            Assert.Equal(CardTemplates.Zmanim, r.TemplateFile);
            Assert.True(r.DataJson.Length < 8000, $"{size}: {r.DataJson.Length}");
            CardExpander.Validate(Expand(r, size), false, true, $"REAL zmanim-all/{size}");
        }
        foreach (var preset in new[] { ZmanimBuilder.CoreZmanimIds, _cal.DefaultEnabledZmanim })
        {
            var r = CardBuilders.BuildSafe(Ctx(WidgetKinds.Zmanim, "large", new WidgetCustomState { ZmanimMode = "custom", ZmanimIds = preset.ToList() }));
            CardExpander.Validate(Expand(r, "large"), false, true, "REAL zmanim-preset");
        }
    }

    [Fact]
    public void Real_shamor_zachor_dashboard_and_bookmarks_render()
    {
        if (_snap is null) return;
        foreach (var filter in new[] { "inProgress", "completed", "all" })
            foreach (var size in CardExpander.Sizes)
            {
                var r = CardBuilders.BuildSafe(Ctx(WidgetKinds.ShamorZachor, size, new WidgetCustomState { SzFilter = filter, ShowAllColumns = true }));
                CardExpander.Validate(Expand(r, size), false, true, $"REAL SZ/{filter}/{size}");
            }
        foreach (var size in new[] { "medium", "large" })
        {
            CardExpander.Validate(Expand(CardBuilders.BuildSafe(Ctx(WidgetKinds.Dashboard, size)), size), false, true, $"REAL dashboard/{size}");
            CardExpander.Validate(Expand(CardBuilders.BuildSafe(Ctx(WidgetKinds.Bookmarks, size)), size), false, true, $"REAL bookmarks/{size}");
        }
    }

    [Fact]
    public void Real_data_service_reports_the_source_names_the_cards_match_on()
    {
        if (_snap is null) return;
        var names = _snap.Status.Select(s => s.Name).ToHashSet();
        Assert.Contains(SourceNames.UserState, names);
        Assert.Contains(SourceNames.Preferences, names);
        Assert.Contains(SourceNames.Library, names);
        Assert.Contains(SourceNames.Progress, names);
    }
}
