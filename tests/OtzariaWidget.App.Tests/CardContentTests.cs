using System.Text.Json;
using System.Xml.Linq;
using OtzariaWidget.App.Provider.Cards;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.Tests;

public class CardContentTests
{
    private static CardExpander.Expanded Expand(CardContext ctx, bool auth = true, string? theme = null)
    {
        var r = CardBuilders.BuildSafe(ctx);
        return CardExpander.Expand(r, ctx.Size.ToHostName(), theme ?? "dark", auth);
    }

    // ---------------------------------------------------------------- RecentBooks
    [Fact]
    public void RecentBooks_empty_state_text()
    {
        var ex = Expand(Ctx.Make(WidgetKinds.RecentBooks, "medium", DataCase.Empty));
        Assert.Contains("לא נמצאו ספרים אחרונים", CardExpander.TextBlocks(ex.Root));
    }

    [Fact]
    public void RecentBooks_missing_state_has_hebrew_message_and_open_settings_button()
    {
        var ex = Expand(Ctx.Make(WidgetKinds.RecentBooks, "medium", DataCase.Missing));
        var texts = CardExpander.TextBlocks(ex.Root).ToList();
        Assert.Contains(texts, t => t.Contains("לא נמצא")); // Core's own Hebrew status message is shown
        var settings = CardExpander.AllActions(ex.Root).Single(a => a.TryGetProperty("title", out var t) && t.GetString() == "פתח הגדרות");
        Assert.Equal("settings", settings.GetProperty("verb").GetString());
    }

    [Fact]
    public void RecentBooks_missing_with_no_status_uses_default_text()
    {
        var snap = new OtzariaSnapshot { ReadAt = TestData.Now, Paths = new OtzariaPaths() };
        var ex = Expand(Ctx.Make(WidgetKinds.RecentBooks, "large", DataCase.Missing, snapshot: snap));
        Assert.Contains(CardData.MissingText, CardExpander.TextBlocks(ex.Root));
    }

    [Theory]
    [InlineData("small", 0, 0)]
    [InlineData("medium", 2, 0)]
    [InlineData("large", 6, 0)]
    public void RecentBooks_row_caps_follow_size(string size, int expectedRows, int expectedSearches)
    {
        var ctx = Ctx.Make(WidgetKinds.RecentBooks, size, DataCase.Present);
        using var doc = JsonDocument.Parse(CardBuilders.BuildSafe(ctx).DataJson);
        Assert.Equal(expectedRows, doc.RootElement.GetProperty("rows").GetArrayLength());
        Assert.Equal(expectedSearches, doc.RootElement.GetProperty("searches").GetArrayLength());
        Assert.Equal("ברכות", doc.RootElement.GetProperty("hero").GetProperty("text").GetString());
        Assert.Equal("המשך קריאה", doc.RootElement.GetProperty("hero").GetProperty("label").GetString());
    }

    [Fact]
    public void RecentBooks_hero_is_not_repeated_in_rows()
    {
        var ctx = Ctx.Make(WidgetKinds.RecentBooks, "large", DataCase.Present);
        using var doc = JsonDocument.Parse(CardBuilders.BuildSafe(ctx).DataJson);
        var rows = doc.RootElement.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("text").GetString()).ToList();
        Assert.DoesNotContain("ברכות", rows);
        Assert.Contains("שולחן ערוך אורח חיים", rows);
    }

    [Fact]
    public void RecentBooks_searches_respect_custom_state_and_privacy_setting()
    {
        var st = new WidgetCustomState { ShowSearches = true };
        var ctx = Ctx.Make(WidgetKinds.RecentBooks, "large", DataCase.Present, state: st);
        using var doc = JsonDocument.Parse(CardBuilders.BuildSafe(ctx).DataJson);
        Assert.Equal(2, doc.RootElement.GetProperty("searches").GetArrayLength());

        ctx.Settings.HideSearches = true;
        using var doc2 = JsonDocument.Parse(CardBuilders.BuildSafe(ctx).DataJson);
        Assert.Equal(0, doc2.RootElement.GetProperty("searches").GetArrayLength());
    }

    [Fact]
    public void RecentBooks_count_override_is_clamped_to_the_size_maximum()
    {
        var ctx = Ctx.Make(WidgetKinds.RecentBooks, "medium", DataCase.Present, state: new WidgetCustomState { Count = 12 });
        using var doc = JsonDocument.Parse(CardBuilders.BuildSafe(ctx).DataJson);
        Assert.Equal(3, doc.RootElement.GetProperty("rows").GetArrayLength());   // max 4 incl. hero
    }

    [Fact]
    public void RecentBooks_paging_shows_next_page_rows()
    {
        var p0 = JsonDocument.Parse(CardBuilders.BuildSafe(Ctx.Make(WidgetKinds.RecentBooks, "large", DataCase.Present, page: 0)).DataJson);
        var p1 = JsonDocument.Parse(CardBuilders.BuildSafe(Ctx.Make(WidgetKinds.RecentBooks, "large", DataCase.Present, page: 1)).DataJson);
        var t0 = p0.RootElement.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("text").GetString()).ToList();
        var t1 = p1.RootElement.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("text").GetString()).ToList();
        Assert.NotEmpty(t1);
        Assert.Empty(t0.Intersect(t1));
        Assert.True(p0.RootElement.GetProperty("pager").GetProperty("show").GetBoolean());
        // the pager appears on the expanded card for authenticated hosts only
        var ex = Expand(Ctx.Make(WidgetKinds.RecentBooks, "large", DataCase.Present));
        Assert.Contains(CardExpander.Elements(ex.Root, "Action.Execute"), a => a.GetProperty("verb").GetString() == "next");
        var exNo = Expand(Ctx.Make(WidgetKinds.RecentBooks, "large", DataCase.Present), auth: false);
        Assert.DoesNotContain(CardExpander.Elements(exNo.Root, "Action.Execute"), _ => true);
    }

    [Fact]
    public void Clickable_rows_use_openurl_in_default_mode_and_execute_open_in_provider_mode()
    {
        var ex = Expand(Ctx.Make(WidgetKinds.RecentBooks, "large", DataCase.Present));
        var selects = CardExpander.Descendants(ex.Root)
            .Where(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("selectAction", out _))
            .Select(e => e.GetProperty("selectAction")).ToList();
        Assert.NotEmpty(selects);
        Assert.All(selects, a => Assert.Equal("Action.OpenUrl", a.GetProperty("type").GetString()));
        Assert.Contains(selects, a => a.GetProperty("url").GetString() == "otzaria://open/book/1938?index=140");

        var ex2 = Expand(Ctx.Make(WidgetKinds.RecentBooks, "large", DataCase.Present, LinkOpenMode.ProviderLaunch));
        var selects2 = CardExpander.Descendants(ex2.Root)
            .Where(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("selectAction", out _))
            .Select(e => e.GetProperty("selectAction")).ToList();
        Assert.Contains(selects2, a => a.GetProperty("type").GetString() == "Action.Execute" && a.GetProperty("verb").GetString() == "open");

        // unauthenticated host: only OpenUrl even in provider mode
        var ex3 = Expand(Ctx.Make(WidgetKinds.RecentBooks, "large", DataCase.Present, LinkOpenMode.ProviderLaunch), auth: false);
        Assert.DoesNotContain(CardExpander.AllActions(ex3.Root), a => a.GetProperty("type").GetString() == "Action.Execute");
    }

    // ---------------------------------------------------------------- Bookmarks
    [Fact]
    public void Bookmarks_group_filter()
    {
        int Count(string group, string size = "large")
        {
            var ctx = Ctx.Make(WidgetKinds.Bookmarks, size, DataCase.Present, state: new WidgetCustomState { Group = group });
            using var doc = JsonDocument.Parse(CardBuilders.BuildSafe(ctx).DataJson);
            return doc.RootElement.GetProperty("rows").GetArrayLength();
        }
        Assert.Equal(5, Count("all"));
        Assert.Equal(2, Count("g1"));
        Assert.Equal(1, Count("g2"));
        Assert.Equal(2, Count("none"));    // bookmarks[1] (ברכות PDF) and [4] (תניא) are in no group
        Assert.Equal(5, Count("missing-group"));
    }

    [Fact]
    public void Bookmarks_empty_group_shows_group_specific_empty_text()
    {
        var snap = TestData.Full();
        snap = snap with { BookmarkGroups = [new BookmarkGroup { Id = "e", Name = "ריקה" }] };
        var ex = Expand(Ctx.Make(WidgetKinds.Bookmarks, "large", DataCase.Present, state: new WidgetCustomState { Group = "e" }, snapshot: snap));
        Assert.Contains("אין סימניות בקבוצה זו", CardExpander.TextBlocks(ex.Root));
        var ex2 = Expand(Ctx.Make(WidgetKinds.Bookmarks, "large", DataCase.Empty));
        Assert.Contains("לא נמצאו סימניות", CardExpander.TextBlocks(ex2.Root));
    }

    [Fact]
    public void Bookmark_label_is_the_headline_and_book_goes_below()
    {
        var ctx = Ctx.Make(WidgetKinds.Bookmarks, "large", DataCase.Present);
        using var doc = JsonDocument.Parse(CardBuilders.BuildSafe(ctx).DataJson);
        var first = doc.RootElement.GetProperty("rows")[0];
        Assert.Equal("הלכות תפילה", first.GetProperty("text").GetString());
        Assert.Contains("שולחן ערוך אורח חיים", first.GetProperty("sub").GetString());
        var second = doc.RootElement.GetProperty("rows")[1];
        Assert.Equal("ברכות", second.GetProperty("text").GetString());   // no label => book title
        Assert.Equal("PDF", second.GetProperty("meta").GetString());
    }

    // ---------------------------------------------------------------- ShamorZachor
    [Fact]
    public void ShamorZachor_filters_and_bars()
    {
        int Rows(string filter)
        {
            var ctx = Ctx.Make(WidgetKinds.ShamorZachor, "large", DataCase.Present, state: new WidgetCustomState { SzFilter = filter, Count = 8 });
            using var doc = JsonDocument.Parse(CardBuilders.BuildSafe(ctx).DataJson);
            return doc.RootElement.GetProperty("rows").GetArrayLength();
        }
        Assert.Equal(5, Rows("inProgress"));    // 5 of 6 are tracked/in progress and not completed
        Assert.Equal(1, Rows("completed"));
        Assert.Equal(6, Rows("all"));

        var data = JsonDocument.Parse(CardBuilders.BuildSafe(Ctx.Make(WidgetKinds.ShamorZachor, "medium", DataCase.Present)).DataJson);
        var row = data.RootElement.GetProperty("rows")[0];
        Assert.Equal("ברכות", row.GetProperty("text").GetString());
        Assert.Equal("45%", row.GetProperty("meta").GetString());
        var bar = row.GetProperty("bar");
        int total = CardData.BarSegmentsMedium;
        Assert.Equal(total, bar.GetProperty("fill").GetString()!.Length + bar.GetProperty("rest").GetString()!.Length);
        Assert.Equal((int)Math.Round(0.45 * total, MidpointRounding.AwayFromZero), bar.GetProperty("fill").GetString()!.Length);
        Assert.Equal("accent", bar.GetProperty("color").GetString());
    }

    [Fact]
    public void ShamorZachor_bar_edge_cases_never_emit_zero_weight()
    {
        foreach (var r in new[] { 0.0, 0.004, 0.996, 1.0, 1.5, -1 })
        {
            var bar = CardData.Bar(r);
            Assert.InRange(bar.Count, 1, 2);
            Assert.All(bar, s => Assert.True(s!["w"]!.GetValue<int>() > 0));
            Assert.Equal(100, bar.Sum(s => s!["w"]!.GetValue<int>()));
        }
    }

    [Fact]
    public void ShamorZachor_all_columns_on_large_shows_labelled_bars_and_expands_to_progress_containers()
    {
        var ctx = Ctx.Make(WidgetKinds.ShamorZachor, "large", DataCase.Present, state: new WidgetCustomState { ShowAllColumns = true });
        var ex = Expand(ctx);
        var texts = CardExpander.TextBlocks(ex.Root).ToList();
        Assert.Contains("לימוד", texts);
        Assert.Contains("חזרה ראשונה", texts);
        // bars are glyph TextBlocks: the filled part colored, the rest subtle - no styled containers (their padding is host defined)
        var glyphs = CardExpander.Elements(ex.Root, "TextBlock")
            .Where(t => t.GetProperty("text").GetString()!.All(c => c.ToString() == CardFormat.BarGlyph || c.ToString() == CardFormat.BarTrackGlyph)).ToList();
        Assert.NotEmpty(glyphs);
        Assert.Contains(glyphs, b => b.TryGetProperty("color", out var c) && c.GetString() == "accent");
        Assert.Contains(glyphs, b => b.TryGetProperty("isSubtle", out var c) && c.GetBoolean());
        Assert.Empty(CardExpander.Elements(ex.Root, "Container").Where(c => c.TryGetProperty("style", out _)));
    }

    [Fact]
    public void ShamorZachor_empty_texts_per_filter()
    {
        var ex = Expand(Ctx.Make(WidgetKinds.ShamorZachor, "medium", DataCase.Empty));
        Assert.Contains("אין ספרים בתהליך", CardExpander.TextBlocks(ex.Root));
        var ex2 = Expand(Ctx.Make(WidgetKinds.ShamorZachor, "medium", DataCase.Empty, state: new WidgetCustomState { SzFilter = "completed" }));
        Assert.Contains("אין ספרים שהושלמו", CardExpander.TextBlocks(ex2.Root));
    }

    // ---------------------------------------------------------------- Zmanim
    [Fact]
    public void Zmanim_next_is_highlighted_and_past_is_subtle()
    {
        var ex = Expand(Ctx.Make(WidgetKinds.Zmanim, "large", DataCase.Present));   // 10:30 => next = chatzos 11:37
        var tbs = CardExpander.Elements(ex.Root, "TextBlock").ToList();
        var next = tbs.Where(t => t.GetProperty("text").GetString() is "חצות היום" or "11:37").ToList();
        Assert.Equal(2, next.Count);
        Assert.All(next, t => { Assert.Equal("bolder", t.GetProperty("weight").GetString()); Assert.Equal("accent", t.GetProperty("color").GetString()); });
        var past = tbs.Where(t => t.GetProperty("text").GetString() is "משיכיר" or "05:33").ToList();
        Assert.Equal(2, past.Count);
        Assert.All(past, t => Assert.True(t.GetProperty("isSubtle").GetBoolean()));
        var future = tbs.Single(t => t.GetProperty("text").GetString() == "מנחה גדולה");
        Assert.False(future.GetProperty("isSubtle").GetBoolean());
    }

    [Fact]
    public void Zmanim_small_shows_only_the_next_zman_big()
    {
        var ex = Expand(Ctx.Make(WidgetKinds.Zmanim, "small", DataCase.Present));
        var texts = CardExpander.TextBlocks(ex.Root).ToList();
        Assert.Contains("חצות היום", texts);
        Assert.Contains("11:37", texts);
        Assert.DoesNotContain("עלות השחר", texts);
        Assert.DoesNotContain("מנחה גדולה", texts);
        var big = CardExpander.Elements(ex.Root, "TextBlock").Single(t => t.GetProperty("text").GetString() == "11:37");
        Assert.Equal("extraLarge", big.GetProperty("size").GetString());
    }

    [Fact]
    public void Zmanim_after_the_last_zman_shows_tomorrow()
    {
        // 15:00: still the 13th (the fake day flips at sunset 16:37) but every enabled zman (last = mincha ketana 14:52) is over
        var late = new DateTimeOffset(2026, 11, 13, 15, 0, 0, TimeSpan.FromHours(2));
        var ctx = Ctx.Make(WidgetKinds.Zmanim, "medium", DataCase.Present, now: late);
        using var doc = JsonDocument.Parse(CardBuilders.BuildSafe(ctx).DataJson);
        Assert.Equal("עלות השחר", doc.RootElement.GetProperty("next").GetProperty("name").GetString());
        Assert.Contains("מחר", doc.RootElement.GetProperty("next").GetProperty("label").GetString());
        Assert.Contains("מחר", doc.RootElement.GetProperty("subtitle").GetString());
    }

    [Fact]
    public void Zmanim_uses_city_override_12h_clock_and_custom_ids()
    {
        var st = new WidgetCustomState { City = "ניו יורק", ZmanimMode = "custom", ZmanimIds = ["sunrise", "sunset"] };
        var ctx = Ctx.Make(WidgetKinds.Zmanim, "large", DataCase.Present, state: st);
        ctx.Settings.TwelveHourClockOverride = true;
        var r = CardBuilders.BuildSafe(ctx);
        using var doc = JsonDocument.Parse(r.DataJson);
        var rows = doc.RootElement.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Contains("ניו יורק", doc.RootElement.GetProperty("subtitle").GetString());
        Assert.All(rows, x => Assert.Matches(@"^\d{1,2}:\d\d (AM|PM)$", x.GetProperty("time").GetString()!));
        Assert.NotNull(r.NextRefresh);
    }

    [Fact]
    public void Zmanim_no_data_gives_empty_state()
    {
        var ex = Expand(Ctx.Make(WidgetKinds.Zmanim, "medium", DataCase.Present, cal: new FakeCalendarService { NoZmanim = true }));
        Assert.Contains("אין זמנים להצגה", CardExpander.TextBlocks(ex.Root));
    }

    [Fact]
    public void Zmanim_next_refresh_is_just_after_the_next_zman()
    {
        var r = CardBuilders.BuildSafe(Ctx.Make(WidgetKinds.Zmanim, "medium", DataCase.Present));
        // 10:30, zman at 11:37: the countdown text ("בעוד 1:05 שעות", 5-minute grid) changes first
        Assert.Equal(new DateTimeOffset(2026, 11, 13, 10, 35, 0, TimeSpan.FromHours(2)), r.NextRefresh);
        // inside the last hour the countdown ticks every minute; the last tick never overshoots the zman
        var late = CardBuilders.BuildSafe(Ctx.Make(WidgetKinds.Zmanim, "medium", DataCase.Present, now: new DateTimeOffset(2026, 11, 13, 11, 36, 30, TimeSpan.FromHours(2))));
        Assert.Equal(new DateTimeOffset(2026, 11, 13, 11, 37, 0, TimeSpan.FromHours(2)), late.NextRefresh);
    }

    [Fact]
    public void Calendar_refresh_is_at_the_jewish_day_transition()
    {
        // 15:00 Israel time: effective date flips at sunset (16:37 in the fake)
        var now = new DateTimeOffset(2026, 11, 13, 15, 0, 0, TimeSpan.FromHours(2));
        var r = CardBuilders.BuildSafe(Ctx.Make(WidgetKinds.HebrewCalendar, "medium", DataCase.Present, now: now));
        Assert.NotNull(r.NextRefresh);
        var diff = Math.Abs((r.NextRefresh!.Value - new DateTimeOffset(2026, 11, 13, 16, 37, 0, TimeSpan.FromHours(2))).TotalSeconds);
        Assert.True(diff <= 3, $"refresh {r.NextRefresh} differs by {diff}s");
    }

    // ---------------------------------------------------------------- HebrewCalendar
    [Fact]
    public void Calendar_medium_shows_hebrew_date_weekday_gregorian_and_priority_lines()
    {
        var ex = Expand(Ctx.Make(WidgetKinds.HebrewCalendar, "medium", DataCase.Present));
        var texts = CardExpander.TextBlocks(ex.Root).ToList();
        Assert.Contains("3׳ בכסלו", texts);                                  // day + month is the big text
        Assert.Contains(texts, t => t.Contains("יום שישי") && t.Contains("תשפ״ז"));   // weekday and year below
        Assert.Contains("13 בנובמבר 2026", texts);
        Assert.Contains("ערב שבת", texts);             // holiday first
        Assert.Contains("15:57", texts);               // candle lighting (Friday) second
        Assert.DoesNotContain("בבא קמא ד", texts);     // medium caps at 3 lines: holiday, candles, parasha
        Assert.Contains("ויצא", texts);
    }

    [Fact]
    public void Calendar_customization_toggles_hide_lines()
    {
        var st = new WidgetCustomState { ShowCandle = false, ShowDaf = true, ShowYerushalmi = true };
        var ex = Expand(Ctx.Make(WidgetKinds.HebrewCalendar, "large", DataCase.Present, state: st));
        var texts = CardExpander.TextBlocks(ex.Root).ToList();
        Assert.DoesNotContain("15:57", texts);
        Assert.Contains("בבא קמא ד", texts);
        Assert.Contains("שבת ה", texts);
        var st2 = new WidgetCustomState { ShowDaf = false };
        var texts2 = CardExpander.TextBlocks(Expand(Ctx.Make(WidgetKinds.HebrewCalendar, "large", DataCase.Present, state: st2)).Root).ToList();
        Assert.DoesNotContain("בבא קמא ד", texts2);
        Assert.DoesNotContain("שבת ה", texts2);   // global ShowYerushalmiDaf default false
    }

    [Fact]
    public void Calendar_follows_global_yerushalmi_setting()
    {
        var ctx = Ctx.Make(WidgetKinds.HebrewCalendar, "large", DataCase.Present);
        ctx.Settings.ShowYerushalmiDaf = true;
        Assert.Contains("שבת ה", CardExpander.TextBlocks(Expand(ctx).Root));
    }

    [Fact]
    public void Calendar_uses_effective_jewish_date_after_sunset()
    {
        var ctx = Ctx.Make(WidgetKinds.HebrewCalendar, "medium", DataCase.Present, now: new DateTimeOffset(2026, 11, 13, 17, 0, 0, TimeSpan.FromHours(2)));
        Assert.Contains("4׳ בכסלו", CardExpander.TextBlocks(Expand(ctx).Root));
    }

    // ---------------------------------------------------------------- Dashboard
    [Fact]
    public void Dashboard_large_shows_all_sections_medium_at_most_three()
    {
        var large = CardExpander.TextBlocks(Expand(Ctx.Make(WidgetKinds.Dashboard, "large", DataCase.Present)).Root).ToList();
        foreach (var t in new[] { "תאריך", "הזמן הבא", "קריאה", "שמור וזכור", "סימניות" })
            Assert.Contains(t, large);

        var medium = CardExpander.TextBlocks(Expand(Ctx.Make(WidgetKinds.Dashboard, "medium", DataCase.Present)).Root).ToList();
        Assert.Contains("תאריך", medium);                            // lead label at the start of the section (compact)
        Assert.DoesNotContain("סימניות", medium);
        Assert.DoesNotContain("שמור וזכור", medium);
    }

    [Fact]
    public void Dashboard_respects_section_order_and_selection()
    {
        var st = new WidgetCustomState { Sections = ["bookmarks", "date"] };
        using var doc = JsonDocument.Parse(CardBuilders.BuildSafe(Ctx.Make(WidgetKinds.Dashboard, "large", DataCase.Present, state: st)).DataJson);
        var keys = doc.RootElement.GetProperty("sections").EnumerateArray().Select(s => s.GetProperty("key").GetString()).ToList();
        Assert.Equal(["bookmarks", "date"], keys);
    }

    [Fact]
    public void Dashboard_with_no_sections_selected_shows_hint()
    {
        var ex = Expand(Ctx.Make(WidgetKinds.Dashboard, "large", DataCase.Present, state: new WidgetCustomState { Sections = [] }));
        Assert.Contains(CardExpander.TextBlocks(ex.Root), t => t.Contains("לא נבחרו חלקים"));
    }

    [Fact]
    public void Dashboard_survives_missing_otzaria_data_but_keeps_calendar_sections()
    {
        var ex = Expand(Ctx.Make(WidgetKinds.Dashboard, "large", DataCase.Missing));
        var texts = CardExpander.TextBlocks(ex.Root).ToList();
        Assert.Contains("תאריך", texts);
        Assert.Contains("הזמן הבא", texts);
        Assert.DoesNotContain("סימניות", texts);
    }

    [Fact]
    public void Small_cards_are_a_single_touch_target_without_pager_or_buttons()
    {
        foreach (var kind in new[] { WidgetKinds.RecentBooks, WidgetKinds.ShamorZachor, WidgetKinds.Zmanim, WidgetKinds.HebrewCalendar })
        {
            var ex = Expand(Ctx.Make(kind, "small", DataCase.Present));
            Assert.Empty(CardExpander.Elements(ex.Root, "ActionSet"));
            Assert.True(!ex.Root.TryGetProperty("actions", out var a) || a.GetArrayLength() == 0, kind + " has footer buttons on small");
        }
    }

    // ---------------------------------------------------------------- notice / theme / payload
    [Fact]
    public void Notice_is_rendered_when_set_and_absent_otherwise()
    {
        foreach (var kind in WidgetKinds.All)
        {
            var with = Expand(Ctx.Make(kind, "large", DataCase.Present, notice: "אוצריא לא נמצא — בדוק את ההגדרות"));
            Assert.Contains("אוצריא לא נמצא — בדוק את ההגדרות", CardExpander.TextBlocks(with.Root));
            var without = Expand(Ctx.Make(kind, "large", DataCase.Present));
            Assert.DoesNotContain("אוצריא לא נמצא — בדוק את ההגדרות", CardExpander.TextBlocks(without.Root));
        }
    }

    [Fact]
    public void Payloads_stay_small_even_with_long_lists()
    {
        foreach (var kind in WidgetKinds.All)
            foreach (var size in CardExpander.Sizes)
            {
                var r = CardBuilders.BuildSafe(Ctx.Make(kind, size, DataCase.Present, state: new WidgetCustomState { Count = 12, ShowSearches = true }));
                Assert.True(r.DataJson.Length < 6000, $"{kind}/{size}: {r.DataJson.Length} chars");
            }
    }

    // ---------------------------------------------------------------- packaging consistency
    [Fact]
    public void Manifest_matches_code_clsid_definitions_and_executable()
    {
        var path = FindUp("packaging", "Package.appxmanifest");
        var text = File.ReadAllText(path);
        var doc = XDocument.Parse(text.Replace("@VERSION@", "1.0.0.0").Replace("@PUBLISHER@", "CN=Test"));

        // CLSID: 3 places in the manifest + the code constant
        var clsid = ProviderIds.Clsid.ToString().ToUpperInvariant();
        Assert.Equal(2, doc.Descendants().Count(e => e.Attributes().Any(a => a.Value.Equals(clsid, StringComparison.OrdinalIgnoreCase))));
        Assert.Equal(1, doc.Descendants().Count(e => e.Name.LocalName == "Class" && (string?)e.Attribute("Id") == clsid));
        Assert.Equal(1, doc.Descendants().Count(e => e.Name.LocalName == "CreateInstance" && (string?)e.Attribute("ClassId") == clsid));

        var defs = doc.Descendants().Where(e => e.Name.LocalName == "Definition").ToList();
        Assert.Equal(WidgetKinds.All.OrderBy(x => x), defs.Select(d => (string)d.Attribute("Id")!).OrderBy(x => x));
        foreach (var d in defs)
        {
            var id = (string)d.Attribute("Id")!;
            Assert.Equal("true", (string?)d.Attribute("IsCustomizable"));
            Assert.Equal("true", (string?)d.Attribute("AllowMultiple"));
            Assert.True(WidgetKinds.IsKnown(id));
            Assert.True(ExtensionHasHebrew((string)d.Attribute("DisplayName")!));
            Assert.True(ExtensionHasHebrew((string)d.Attribute("Description")!));
            var sizes = d.Descendants().Where(e => e.Name.LocalName == "Size").Select(e => (string)e.Attribute("Name")!).ToList();
            Assert.NotEmpty(sizes);
            Assert.All(sizes, s => Assert.Contains(s, new[] { "small", "medium", "large" }));
            // every referenced asset exists
            foreach (var p in d.Descendants().Where(e => e.Attribute("Path") is not null).Select(e => (string)e.Attribute("Path")!))
                Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, p)), "missing asset " + p);
        }
        Assert.Contains(defs, d => (string)d.Attribute("Id")! == WidgetKinds.RecentBooks &&
                                   d.Descendants().Count(e => e.Name.LocalName == "Size") == 3);
        Assert.Equal(2, defs.Single(d => (string)d.Attribute("Id")! == WidgetKinds.Dashboard).Descendants().Count(e => e.Name.LocalName == "Size"));

        var identity = doc.Root!.Elements().Single(e => e.Name.LocalName == "Identity");
        Assert.Equal(ProviderIds.PackageName, (string?)identity.Attribute("Name"));
        Assert.Equal("ווידג'ט אוצריא", doc.Descendants().Single(e => e.Name.LocalName == "DisplayName" && e.Parent!.Name.LocalName == "Properties").Value);
        var comExe = doc.Descendants().Single(e => e.Name.LocalName == "ExeServer");
        Assert.Equal("-RegisterProcessAsComServer", (string?)comExe.Attribute("Arguments"));
        Assert.Equal("OtzariaWidget.exe", (string?)comExe.Attribute("Executable"));
        Assert.Contains(doc.Descendants(), e => e.Name.LocalName == "Protocol" && (string?)e.Attribute("Name") == ProviderIds.SettingsProtocol);
        Assert.Equal("Microsoft.WindowsAppRuntime.1.8", (string?)doc.Descendants().Single(e => e.Name.LocalName == "PackageDependency").Attribute("Name"));
    }

    [Fact]
    public void Templates_are_valid_json_with_expected_files()
    {
        foreach (var f in CardTemplates.All)
        {
            var txt = CardTemplates.Load(f);
            using var d = JsonDocument.Parse(txt);
            Assert.Equal("1.6", d.RootElement.GetProperty("version").GetString());
            Assert.True(d.RootElement.GetProperty("rtl").GetBoolean());
        }
    }

    private static bool ExtensionHasHebrew(string s) => CardExpander.HasHebrew(s);

    private static string FindUp(string folder, string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var p = Path.Combine(dir.FullName, folder, file);
            if (File.Exists(p)) return p;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(file);
    }
}
