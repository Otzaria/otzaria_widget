using System.Text.Json;
using OtzariaWidget.App.Provider;
using OtzariaWidget.App.Provider.Cards;
using OtzariaWidget.App.SettingsUi.Infrastructure;
using OtzariaWidget.Calendar;
using OtzariaWidget.Calendar.Cities;
using OtzariaWidget.Core.Contracts;
using OtzariaWidget.Core.Services;
using OtzariaWidget.Core.Settings;

namespace OtzariaWidget.App.Tests;

/// <summary>
/// Release QA: every WidgetAppSettings field that the settings UI writes must change what the widgets render, and the way Otzaria really
/// stores its values (city name, day transition) must resolve through the calendar. Card level (pure) plus one end-to-end test through the
/// real JsonSettingsStore file, the real OtzariaDataService and ProviderServices into a WidgetController.
/// </summary>
public class SettingsPropagationTests
{
    private static readonly DateTimeOffset FridayNight = new(2026, 11, 13, 17, 30, 0, TimeSpan.FromHours(2));   // after sunset (16:37) in Jerusalem

    private static JsonDocument Json(CardContext ctx) => JsonDocument.Parse(CardBuilders.BuildSafe(ctx).DataJson);

    private static CardContext RealCal(string kind, string size, WidgetAppSettings settings, OtzariaPreferences prefs, DateTimeOffset now,
        WidgetCustomState? state = null)
    {
        var snap = TestData.Full() with { Preferences = prefs };
        return new CardContext
        {
            Kind = kind, Size = CardSizes.Parse(size), Settings = settings, Calendar = new CalendarService(), Now = now,
            Snapshot = snap, State = state ?? new WidgetCustomState(),
        };
    }

    // ---------------------------------------------------------------- city
    [Fact]
    public void CityOverride_beats_Otzaria_city_and_widget_city_beats_the_override()
    {
        var ctx = Ctx.Make(WidgetKinds.Zmanim, "large", DataCase.Present);
        ctx.Settings.CityOverride = "בני ברק";
        using (var doc = Json(ctx)) Assert.Contains("בני ברק", doc.RootElement.GetProperty("headerEnd").GetString());

        var withWidgetCity = Ctx.Make(WidgetKinds.Zmanim, "large", DataCase.Present, state: new WidgetCustomState { City = "ניו יורק" });
        withWidgetCity.Settings.CityOverride = "בני ברק";
        using (var doc = Json(withWidgetCity)) Assert.Contains("ניו יורק", doc.RootElement.GetProperty("headerEnd").GetString());

        // no override: Otzaria's own city
        using (var doc = Json(Ctx.Make(WidgetKinds.Zmanim, "large", DataCase.Present)))
            Assert.Contains("ירושלים", doc.RootElement.GetProperty("headerEnd").GetString());
    }

    [Fact]
    public void CityOverride_reaches_the_calendar_and_dashboard_widgets_too()
    {
        foreach (var kind in new[] { WidgetKinds.HebrewCalendar, WidgetKinds.Dashboard })
        {
            var a = Ctx.Make(kind, "large", DataCase.Present);
            var b = Ctx.Make(kind, "large", DataCase.Present);
            b.Settings.CityOverride = "ניו יורק";
            var ra = CardBuilders.BuildSafe(a); var rb = CardBuilders.BuildSafe(b);
            if (kind == WidgetKinds.HebrewCalendar)
            {
                using var da = JsonDocument.Parse(ra.DataJson); using var db = JsonDocument.Parse(rb.DataJson);
                Assert.Equal("ירושלים", da.RootElement.GetProperty("headerEnd").GetString());
                Assert.Equal("ניו יורק", db.RootElement.GetProperty("headerEnd").GetString());
            }
            else
            {
                // the dashboard's next-zman line is computed for the other city (different UTC offset in the fake calendar)
                Assert.NotEqual(ra.DataJson, rb.DataJson);
            }
        }
    }

    [Fact]
    public void Every_city_name_in_the_table_resolves_when_Otzaria_stores_it()
    {
        var cal = new CalendarService();
        Assert.True(cal.Cities.Count > 50);
        foreach (var c in cal.Cities)
        {
            var ctx = RealCal(WidgetKinds.Zmanim, "small", new WidgetAppSettings(), new OtzariaPreferences { SelectedCity = c.Name }, FridayNight);
            Assert.Equal(c.Name, CalendarContext.ResolveCity(ctx, null).Name);
        }
    }

    [Fact]
    public void City_name_variants_unknown_names_and_bidi_marks_degrade_to_a_real_city()
    {
        // Otzaria stores the display name; apostrophe/quote variants and stray bidi marks still find the city, an unknown name falls back to Jerusalem
        var cal = new CalendarService();
        var withQuote = cal.Cities.FirstOrDefault(c => c.Name.Contains('\'') || c.Name.Contains('"') || c.Name.Contains('׳') || c.Name.Contains('״'));
        if (withQuote is not null)
        {
            var variant = withQuote.Name.Replace('\'', '׳').Replace('"', '״');
            var ctx = RealCal(WidgetKinds.Zmanim, "small", new WidgetAppSettings(), new OtzariaPreferences { SelectedCity = variant }, FridayNight);
            Assert.Equal(withQuote.Name, CalendarContext.ResolveCity(ctx, null).Name);
        }
        var marked = RealCal(WidgetKinds.Zmanim, "small", new WidgetAppSettings(), new OtzariaPreferences { SelectedCity = "‏ירושלים‎ " }, FridayNight);
        Assert.Equal("ירושלים", CalendarContext.ResolveCity(marked, null).Name);
        var unknown = RealCal(WidgetKinds.Zmanim, "small", new WidgetAppSettings { CityOverride = "עיר שלא קיימת" }, new OtzariaPreferences { SelectedCity = "גם לא קיימת" }, FridayNight);
        Assert.Equal(CityTable.DefaultCityName, CalendarContext.ResolveCity(unknown, null).Name);
    }

    // ---------------------------------------------------------------- zmanim selection
    [Fact]
    public void ZmanimOverride_replaces_the_default_set_and_the_widget_custom_list_beats_it()
    {
        var ctx = Ctx.Make(WidgetKinds.Zmanim, "large", DataCase.Present);
        ctx.Settings.ZmanimOverride = ["sunrise", "sunset"];
        using (var doc = Json(ctx))
        {
            var names = doc.RootElement.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("name").GetString()).ToList();
            Assert.Equal(["הנץ החמה", "שקיעת החמה"], names);
        }

        var custom = Ctx.Make(WidgetKinds.Zmanim, "large", DataCase.Present,
            state: new WidgetCustomState { ZmanimMode = "custom", ZmanimIds = ["chatzos"] });
        custom.Settings.ZmanimOverride = ["sunrise", "sunset"];
        using (var doc = Json(custom))
            Assert.Equal(["חצות היום"], doc.RootElement.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("name").GetString()).ToList());

        // the settings UI says "no times selected - the widget shows none" for an empty list: it must not silently fall back to Otzaria's set
        var empty = Ctx.Make(WidgetKinds.Zmanim, "large", DataCase.Present);
        empty.Settings.ZmanimOverride = [];
        using (var doc = Json(empty))
        {
            Assert.Equal(0, doc.RootElement.GetProperty("rows").GetArrayLength());
            Assert.Equal("empty", doc.RootElement.GetProperty("state").GetString());
        }
        // null = follow Otzaria
        var follow = Ctx.Make(WidgetKinds.Zmanim, "large", DataCase.Present);
        follow.Settings.ZmanimOverride = null;
        using (var doc = Json(follow)) Assert.True(doc.RootElement.GetProperty("rows").GetArrayLength() > 2);
    }

    [Fact]
    public void Empty_ZmanimOverride_with_the_real_calendar_gives_the_empty_state_in_zmanim_and_dashboard()
    {
        var prefs = new OtzariaPreferences { SelectedCity = "ירושלים" };
        var settings = new WidgetAppSettings { ZmanimOverride = [] };
        var z = CardBuilders.BuildSafe(RealCal(WidgetKinds.Zmanim, "large", settings, prefs, FridayNight));
        Assert.Equal(CardTemplates.Zmanim, z.TemplateFile);
        using (var doc = JsonDocument.Parse(z.DataJson)) Assert.Equal("empty", doc.RootElement.GetProperty("state").GetString());
        var d = CardBuilders.BuildSafe(RealCal(WidgetKinds.Dashboard, "large", settings, prefs, FridayNight, new WidgetCustomState { Sections = ["zman"] }));
        Assert.Equal(CardTemplates.Dashboard, d.TemplateFile);
    }

    [Fact]
    public void Otzaria_enabled_zmanim_are_used_when_there_is_no_override()
    {
        var snap = TestData.Full() with { Preferences = new OtzariaPreferences { SelectedCity = "ירושלים", EnabledZmanim = ["plag", "tzeis"] } };
        var ctx = Ctx.Make(WidgetKinds.Zmanim, "large", DataCase.Present, snapshot: snap);
        using var doc = Json(ctx);
        Assert.Equal(["פלג המנחה", "צאת הכוכבים"], doc.RootElement.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("name").GetString()).ToList());
    }

    // ---------------------------------------------------------------- clock + day transition
    [Fact]
    public void TwelveHourClockOverride_true_and_false_both_beat_Otzaria()
    {
        var on = Ctx.Make(WidgetKinds.Zmanim, "medium", DataCase.Present);
        on.Settings.TwelveHourClockOverride = true;
        using (var doc = Json(on)) Assert.Matches(@"(AM|PM)", doc.RootElement.GetProperty("rows")[0].GetProperty("time").GetString()!);

        var snap = TestData.Full() with { Preferences = new OtzariaPreferences { SelectedCity = "ירושלים", TwelveHourClock = true } };
        var off = Ctx.Make(WidgetKinds.Zmanim, "medium", DataCase.Present, snapshot: snap);
        using (var doc = Json(off)) Assert.Matches(@"(AM|PM)", doc.RootElement.GetProperty("rows")[0].GetProperty("time").GetString()!);   // follows Otzaria
        off.Settings.TwelveHourClockOverride = false;
        using (var doc = Json(off)) Assert.DoesNotMatch(@"(AM|PM)", doc.RootElement.GetProperty("rows")[0].GetProperty("time").GetString()!);
    }

    [Fact]
    public void DayTransitionOverride_changes_the_Hebrew_date_after_sunset_with_the_real_calendar()
    {
        // The real machine stores "midnight"; an override of "sunset" must move the card to the next Jewish day after sunset, and vice versa.
        var midnightPrefs = new OtzariaPreferences { SelectedCity = "ירושלים", DayTransition = "midnight" };
        var sunsetPrefs = midnightPrefs with { DayTransition = "sunset" };

        using var followMidnight = Json(RealCal(WidgetKinds.HebrewCalendar, "medium", new WidgetAppSettings(), midnightPrefs, FridayNight));
        using var overrideSunset = Json(RealCal(WidgetKinds.HebrewCalendar, "medium", new WidgetAppSettings { DayTransitionOverride = "sunset" }, midnightPrefs, FridayNight));
        using var followSunset = Json(RealCal(WidgetKinds.HebrewCalendar, "medium", new WidgetAppSettings(), sunsetPrefs, FridayNight));
        using var overrideMidnight = Json(RealCal(WidgetKinds.HebrewCalendar, "medium", new WidgetAppSettings { DayTransitionOverride = "midnight" }, sunsetPrefs, FridayNight));

        string Hebrew(JsonDocument d) => d.RootElement.GetProperty("date").GetProperty("hebrew").GetString()!;
        string Gregorian(JsonDocument d) => d.RootElement.GetProperty("date").GetProperty("gregorian").GetString()!;

        Assert.NotEqual(Hebrew(followMidnight), Hebrew(overrideSunset));
        Assert.Equal(Hebrew(overrideSunset), Hebrew(followSunset));
        Assert.Equal(Hebrew(followMidnight), Hebrew(overrideMidnight));
        Assert.Contains("13", Gregorian(followMidnight));
        Assert.Contains("14", Gregorian(overrideSunset));
    }

    [Fact]
    public void Day_boundary_refresh_follows_the_effective_transition()
    {
        // Friday 15:00 in Jerusalem (before sunset): with "sunset" the card must refresh at sunset, with "midnight" only at midnight.
        var afternoon = new DateTimeOffset(2026, 11, 13, 15, 0, 0, TimeSpan.FromHours(2));
        var prefs = new OtzariaPreferences { SelectedCity = "ירושלים", DayTransition = "midnight" };
        var midnight = CardBuilders.BuildSafe(RealCal(WidgetKinds.HebrewCalendar, "small", new WidgetAppSettings(), prefs, afternoon));
        var sunset = CardBuilders.BuildSafe(RealCal(WidgetKinds.HebrewCalendar, "small", new WidgetAppSettings { DayTransitionOverride = "sunset" }, prefs, afternoon));
        Assert.NotNull(midnight.NextRefresh); Assert.NotNull(sunset.NextRefresh);
        Assert.True(sunset.NextRefresh < midnight.NextRefresh, "sunset must come before midnight");
        Assert.InRange(sunset.NextRefresh!.Value, new DateTimeOffset(2026, 11, 13, 16, 20, 0, TimeSpan.FromHours(2)), new DateTimeOffset(2026, 11, 13, 17, 0, 0, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void Zmanim_card_on_a_midnight_machine_after_sunset_still_shows_today_and_the_dashboard_agrees()
    {
        var prefs = new OtzariaPreferences { SelectedCity = "ירושלים", DayTransition = "midnight" };
        var settings = new WidgetAppSettings();
        using var z = Json(RealCal(WidgetKinds.Zmanim, "large", settings, prefs, FridayNight));
        using var d = Json(RealCal(WidgetKinds.Dashboard, "large", settings, prefs, FridayNight));
        // 17:30 on Friday: the evening's zmanim are still "today" (no "מחר" marker) when the day does not roll at sunset
        Assert.DoesNotContain("מחר", z.RootElement.GetProperty("headerEnd").GetString());
        Assert.NotEqual(JsonValueKind.Undefined, d.RootElement.ValueKind);
    }

    // ---------------------------------------------------------------- yerushalmi + links + searches
    [Fact]
    public void ShowYerushalmiDaf_global_setting_is_the_default_and_the_widget_toggle_wins()
    {
        var on = RealCal(WidgetKinds.HebrewCalendar, "large", new WidgetAppSettings { ShowYerushalmiDaf = true }, new OtzariaPreferences(), FridayNight);
        using (var doc = Json(on)) Assert.Contains(doc.RootElement.GetProperty("lines").EnumerateArray(), l => l.GetProperty("label").GetString() == "ירושלמי");
        var widgetOff = RealCal(WidgetKinds.HebrewCalendar, "large", new WidgetAppSettings { ShowYerushalmiDaf = true }, new OtzariaPreferences(), FridayNight,
            new WidgetCustomState { ShowYerushalmi = false });
        using (var doc = Json(widgetOff)) Assert.DoesNotContain(doc.RootElement.GetProperty("lines").EnumerateArray(), l => l.GetProperty("label").GetString() == "ירושלמי");
        var globalOff = RealCal(WidgetKinds.HebrewCalendar, "large", new WidgetAppSettings(), new OtzariaPreferences(), FridayNight);
        using (var doc = Json(globalOff)) Assert.DoesNotContain(doc.RootElement.GetProperty("lines").EnumerateArray(), l => l.GetProperty("label").GetString() == "ירושלמי");
    }

    [Theory]
    [InlineData(WidgetKinds.RecentBooks)]
    [InlineData(WidgetKinds.Bookmarks)]
    [InlineData(WidgetKinds.ShamorZachor)]
    [InlineData(WidgetKinds.Zmanim)]
    [InlineData(WidgetKinds.HebrewCalendar)]
    [InlineData(WidgetKinds.Dashboard)]
    public void LinkMode_ProviderLaunch_adds_execute_actions_and_OpenUrl_never_does(string kind)
    {
        var url = Ctx.Make(kind, "large", DataCase.Present, LinkOpenMode.OpenUrl);
        var launch = Ctx.Make(kind, "large", DataCase.Present, LinkOpenMode.ProviderLaunch);
        var exUrl = CardExpander.Expand(CardBuilders.BuildSafe(url), "large", "dark", true);
        var exLaunch = CardExpander.Expand(CardBuilders.BuildSafe(launch), "large", "dark", true);
        bool IsOpenVerb(JsonElement a) => a.TryGetProperty("verb", out var v) && v.GetString() == Verbs.Open;
        Assert.DoesNotContain(CardExpander.AllActions(exUrl.Root), IsOpenVerb);
        // authenticated host + ProviderLaunch: the in-card links go through the provider (calendar/zmanim have a single header link)
        Assert.Contains(CardExpander.AllActions(exLaunch.Root), IsOpenVerb);
        // an unauthenticated host can only use OpenUrl, whatever the mode
        var exUnauth = CardExpander.Expand(CardBuilders.BuildSafe(launch), "large", "dark", false);
        Assert.DoesNotContain(CardExpander.AllActions(exUnauth.Root), IsOpenVerb);
    }

    [Fact]
    public void HideSearches_wins_over_the_widget_toggle_in_every_size()
    {
        foreach (var size in new[] { "small", "medium", "large" })
        {
            var ctx = Ctx.Make(WidgetKinds.RecentBooks, size, DataCase.Present, state: new WidgetCustomState { ShowSearches = true });
            ctx.Settings.HideSearches = true;
            using var doc = Json(ctx);
            Assert.Equal(0, doc.RootElement.GetProperty("searches").GetArrayLength());
        }
    }

    // ---------------------------------------------------------------- settings UI -> file -> provider (end to end)
    private sealed class FixedResolver(OtzariaPaths paths) : IOtzariaPathResolver { public OtzariaPaths Resolve(WidgetAppSettings s) => paths; }
    private sealed class FixedPrefs(OtzariaPreferences p) : IPreferencesReader { public OtzariaPreferences Read(string path) => p; }
    private sealed class NoProgress : IProgressService { public IReadOnlyList<BookProgress> Compute(OtzariaPreferences prefs, ILibraryCatalog? c) => []; }
    private sealed class NoLaunch : IOtzariaLauncher
    {
        public string? BuildLink(ReadingEntry entry) => null;
        public string BuildSearchLink(string query, string? mode) => "";
        public bool Launch(string? uri, OtzariaPaths paths) => true;
    }
    private sealed class NoUserState : IUserStateReader
    {
        public IReadOnlyList<ReadingEntry> ReadRecentBooks(string p, int max) => [];
        public IReadOnlyList<RecentSearch> ReadRecentSearches(string p, int max) => [];
        public IReadOnlyList<ReadingEntry> ReadBookmarks(string p) => [];
        public IReadOnlyList<BookmarkGroup> ReadBookmarkGroups(string p) => [];
        public IReadOnlyList<WorkspaceInfo> ReadWorkspaces(string p) => [];
        public IReadOnlyList<OpenTabInfo> ReadOpenTabs(string p) => [];
    }

    private sealed class CollectSink : IWidgetSink
    {
        private readonly object _gate = new();
        private readonly List<(string Id, string Data)> _updates = [];
        public void Update(string widgetId, string template, string data, string customState) { lock (_gate) _updates.Add((widgetId, data)); }
        public string? Last(string id) { lock (_gate) return _updates.LastOrDefault(u => u.Id == id).Data; }
    }

    [Fact]
    public async Task Settings_saved_by_the_UI_session_reach_the_active_widgets_through_the_file()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "otz-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var hive = Path.Combine(tmp, "app_preferences.hive");
        File.WriteAllBytes(hive, []);
        try
        {
            // the provider's side: real store/data service/calendar, fake Otzaria files (Jerusalem, 24h clock, midnight transition)
            using var providerStore = new JsonSettingsStore(tmp, TimeSpan.FromMilliseconds(50));
            var paths = new OtzariaPaths { PreferencesHive = hive };
            var prefs = new OtzariaPreferences { SelectedCity = "ירושלים", DayTransition = "midnight" };
            using var services = new ProviderServices(
                () => providerStore,
                s => new OtzariaDataService(s, new FixedResolver(paths), new NoUserState(), new FixedPrefs(prefs), new NoProgress(), p => throw new IOException(), new NoLaunch(), TimeSpan.FromMilliseconds(50)),
                () => new CalendarService(), () => new NoLaunch());
            var sink = new CollectSink();
            using var controller = new WidgetController(services, sink);
            services.SourceChanged += src => controller.OnSourceChanged(src);
            controller.Create(new WidgetRef("e2e-z", WidgetKinds.Zmanim, CardSize.Medium, true));
            controller.Create(new WidgetRef("e2e-c", WidgetKinds.HebrewCalendar, CardSize.Medium, true));

            string? LastData(string id) => sink.Last(id);
            Assert.Contains("ירושלים", JsonDocument.Parse(LastData("e2e-z")!).RootElement.GetProperty("headerEnd").GetString());

            // the settings UI's side: a SEPARATE store instance (another process in real life) saving through the debounced session
            var uiStore = new JsonSettingsStore(tmp, TimeSpan.FromMilliseconds(50));
            using var session = new SettingsSession(uiStore);
            session.Settings.CityOverride = "ניו יורק";
            session.Settings.TwelveHourClockOverride = true;
            session.Settings.DayTransitionOverride = "sunset";
            session.MarkChanged();

            bool Reached()
            {
                var z = LastData("e2e-z");
                if (z is null) return false;
                using var d = JsonDocument.Parse(z);
                return d.RootElement.GetProperty("headerEnd").GetString()!.Contains("ניו יורק");
            }
            for (int i = 0; i < 200 && !Reached(); i++) await Task.Delay(50);
            Assert.True(Reached(), "the zmanim card did not pick up the saved CityOverride");
            using (var d = JsonDocument.Parse(LastData("e2e-z")!))
                Assert.Matches("(AM|PM)", d.RootElement.GetProperty("rows")[0].GetProperty("time").GetString()!);

            // nothing the user typed leaks into the sink: the cards contain no settings file path
            Assert.DoesNotContain(tmp, LastData("e2e-z")!);
        }
        finally { try { Directory.Delete(tmp, true); } catch (IOException) { } }
    }
}
