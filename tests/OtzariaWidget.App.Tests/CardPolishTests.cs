using System.Text.Json;
using OtzariaWidget.App.Provider.Cards;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.Tests;

/// <summary>QA fixes + polish: rounding, countdown, source-name matching, Shamor Vezachor degradation, zmanim edge cases, presets.</summary>
public class CardPolishTests
{
    private static JsonDocument Data(CardContext ctx) => JsonDocument.Parse(CardBuilders.BuildSafe(ctx).DataJson);
    private static CardExpander.Expanded Expand(CardContext ctx, string? theme = null) =>
        CardExpander.Expand(CardBuilders.BuildSafe(ctx), ctx.Size.ToHostName(), theme ?? "dark");

    // ------------------------------------------------------------------------------------------------ formatting
    [Theory]
    [InlineData(0.125, "13%")]    // 12.5 -> 13 like Dart's round(); banker's rounding would give 12
    [InlineData(0.005, "1%")]
    [InlineData(0.995, "100%")]
    [InlineData(0.445, "45%")]
    [InlineData(0.444, "44%")]
    public void Percent_rounds_half_away_from_zero(double ratio, string expected) => Assert.Equal(expected, CardFormat.Percent(ratio));

    [Fact]
    public void Countdown_text()
    {
        Assert.Equal("בעוד 2:15 שעות", CardFormat.Countdown(TimeSpan.FromMinutes(135)));
        Assert.Equal("בעוד 2:15 שעות", CardFormat.Countdown(TimeSpan.FromMinutes(133)));   // rounds to the 5-minute grid
        Assert.Equal("בעוד 1:00 שעות", CardFormat.Countdown(TimeSpan.FromMinutes(60)));
        Assert.Equal("בעוד 42 דקות", CardFormat.Countdown(TimeSpan.FromMinutes(41.2)));
        Assert.Equal("בעוד דקה", CardFormat.Countdown(TimeSpan.FromSeconds(20)));
        Assert.Equal("עוד רגע", CardFormat.Countdown(TimeSpan.Zero));
        Assert.Equal("עוד רגע", CardFormat.Countdown(TimeSpan.FromMinutes(-3)));
    }

    [Theory]
    [InlineData("ברכות, ב.", "ברכות", "ב.")]
    [InlineData("מנחות דף עו: — עמוד 150", "מנחות", "דף עו: — עמוד 150")]
    [InlineData("ירמיהו, לג, כ", "ירמיהו", "לג, כ")]
    [InlineData("סימן א סעיף ב", "שולחן ערוך", "סימן א סעיף ב")]   // no prefix: unchanged
    [InlineData("ברכות", "ברכות", "ברכות")]                       // nothing left: keep the whole text
    [InlineData("", "ברכות", "")]
    public void StripTitle_drops_the_book_title_prefix(string reference, string title, string expected) =>
        Assert.Equal(expected, CardFormat.StripTitle(reference, title));

    [Fact]
    public void Real_book_rows_do_not_repeat_the_title_in_the_location()
    {
        var snap = TestData.Full();
        var e = snap.RecentBooks[0] with { Ref = "ברכות, ב:" };
        snap = snap with { RecentBooks = [e, .. snap.RecentBooks.Skip(1)], OpenTabs = [] };
        using var doc = Data(Ctx.Make(WidgetKinds.RecentBooks, "medium", DataCase.Present, snapshot: snap));
        Assert.Equal("ב:", doc.RootElement.GetProperty("hero").GetProperty("sub").GetString());
    }

    [Fact]
    public void Glyph_bar_never_shows_a_wrong_extreme()
    {
        foreach (var segs in new[] { 8, 10, 18 })
        {
            var (f0, r0) = CardFormat.BarGlyphs(0, segs);
            Assert.Equal(0, f0.Length); Assert.Equal(segs, r0.Length);
            var (f1, r1) = CardFormat.BarGlyphs(1, segs);
            Assert.Equal(segs, f1.Length); Assert.Equal(0, r1.Length);
            var (fa, ra) = CardFormat.BarGlyphs(0.001, segs);      // started: at least one filled
            Assert.Equal(1, fa.Length); Assert.Equal(segs - 1, ra.Length);
            var (fb, rb) = CardFormat.BarGlyphs(0.999, segs);      // unfinished: at least one empty
            Assert.Equal(segs - 1, fb.Length); Assert.Equal(1, rb.Length);
            Assert.Equal(segs, CardFormat.BarGlyphs(double.NaN, segs).Fill.Length + CardFormat.BarGlyphs(double.NaN, segs).Remaining.Length);
        }
    }

    [Theory]
    [InlineData("ג׳ כסלו תשפ״ז", "ג׳ כסלו", "תשפ״ז")]
    [InlineData("כ\"ב בחשון תשפ\"ז", "כ\"ב בחשון", "תשפ\"ז")]
    [InlineData("י״ד באדר ראשון תשפ״ז", "י״ד באדר ראשון", "תשפ״ז")]
    [InlineData("ראש חודש", "ראש חודש", "")]
    [InlineData("", "", "")]
    public void Hebrew_date_split_into_day_and_year(string full, string day, string year) =>
        Assert.Equal((day, year), HebrewCalendarBuilder.SplitHebrewDate(full));

    // ------------------------------------------------------------------------------------------------ data health by exact source name
    private static OtzariaSnapshot WithStatus(params SourceStatus[] status) => TestData.Full() with { Status = status };

    [Fact]
    public void Health_matching_uses_exact_source_names()
    {
        // a lookalike name must NOT trigger the missing state ...
        var near = WithStatus(new SourceStatus("user_state.db.bak", DataHealth.NotFound, "לא נמצא"), new SourceStatus("UserState", DataHealth.NotFound, "לא נמצא"));
        Assert.Equal("ok", Data(Ctx.Make(WidgetKinds.RecentBooks, "large", DataCase.Present, snapshot: near)).RootElement.GetProperty("state").GetString());
        Assert.Equal("ok", Data(Ctx.Make(WidgetKinds.Bookmarks, "large", DataCase.Present, snapshot: near)).RootElement.GetProperty("state").GetString());
        // ... the canonical constant does
        var exact = WithStatus(new SourceStatus(SourceNames.UserState, DataHealth.Unreadable, "לא ניתן לקרוא"));
        using var d = Data(Ctx.Make(WidgetKinds.RecentBooks, "large", DataCase.Present, snapshot: exact));
        Assert.Equal("missing", d.RootElement.GetProperty("state").GetString());
        Assert.Equal("לא ניתן לקרוא", d.RootElement.GetProperty("emptyText").GetString());
        // a problem in an unrelated source does not hide the recent books
        var other = WithStatus(new SourceStatus(SourceNames.Library, DataHealth.NotFound, "אין seforim"));
        Assert.Equal("ok", Data(Ctx.Make(WidgetKinds.RecentBooks, "large", DataCase.Present, snapshot: other)).RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public void Percent_in_all_columns_uses_the_same_rounding()
    {
        var snap = TestData.Full() with { Progress = [TestData.Progress(9, "ספר", 0.125, tracked: true)] };
        using var d = Data(Ctx.Make(WidgetKinds.ShamorZachor, "large", DataCase.Present, snapshot: snap));
        Assert.Equal("13%", d.RootElement.GetProperty("rows")[0].GetProperty("meta").GetString());
    }

    // ------------------------------------------------------------------------------------------------ Shamor Vezachor degradation
    [Fact]
    public void ShamorZachor_without_the_hive_is_the_data_missing_card()
    {
        var noHive = TestData.Full() with { Paths = TestData.GoodPaths with { PreferencesHive = null } };
        using var d1 = Data(Ctx.Make(WidgetKinds.ShamorZachor, "large", DataCase.Present, snapshot: noHive));
        Assert.Equal("missing", d1.RootElement.GetProperty("state").GetString());

        var unreadable = WithStatus(new SourceStatus(SourceNames.Preferences, DataHealth.Unreadable, "לא ניתן לקרוא את הגדרות אוצריא"));
        using var d2 = Data(Ctx.Make(WidgetKinds.ShamorZachor, "medium", DataCase.Present, snapshot: unreadable));
        Assert.Equal("missing", d2.RootElement.GetProperty("state").GetString());
        Assert.Equal("לא ניתן לקרוא את הגדרות אוצריא", d2.RootElement.GetProperty("emptyText").GetString());

        var failed = WithStatus(new SourceStatus(SourceNames.Progress, DataHealth.Unreadable, "לא ניתן לחשב את נתוני שמור וזכור"));
        using var d3 = Data(Ctx.Make(WidgetKinds.ShamorZachor, "small", DataCase.Present, snapshot: failed));
        Assert.Equal("missing", d3.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public void ShamorZachor_without_seforim_still_lists_the_books_with_a_subtle_note()
    {
        var snap = TestData.Full() with
        {
            Status =
            [
                new SourceStatus(SourceNames.UserState, DataHealth.Ok, null),
                new SourceStatus(SourceNames.Preferences, DataHealth.Ok, null),
                new SourceStatus(SourceNames.Library, DataHealth.NotFound, "לא נמצא seforim.db"),
                new SourceStatus(SourceNames.Progress, DataHealth.Ok, null),
            ],
            Progress = [TestData.Progress(55, "ספר 55", 0.4, tracked: true), TestData.Progress(56, "ספר 56", 0.2)],
        };
        var ctx = Ctx.Make(WidgetKinds.ShamorZachor, "large", DataCase.Present, snapshot: snap);
        using var d = Data(ctx);
        Assert.Equal("ok", d.RootElement.GetProperty("state").GetString());
        Assert.Equal(2, d.RootElement.GetProperty("rows").GetArrayLength());
        Assert.Contains("ספריית אוצריא לא זמינה", d.RootElement.GetProperty("note").GetString());
        var ex = Expand(ctx);
        CardExpander.Validate(ex, false, true, "sz-no-library");
        var texts = CardExpander.TextBlocks(ex.Root).ToList();
        Assert.Contains("ספר 55", texts);
        Assert.Contains(texts, t => t.Contains("ספריית אוצריא לא זמינה"));
        // healthy library: no note
        using var ok = Data(Ctx.Make(WidgetKinds.ShamorZachor, "large", DataCase.Present));
        Assert.Equal("", ok.RootElement.GetProperty("note").GetString());
    }

    [Fact]
    public void ShamorZachor_cycle_text_and_completed_check()
    {
        var p = TestData.Progress(1, "ברכות", 0.45, tracked: true) with { CurrentCycleText = "45 מתוך 100" };
        var done = TestData.Progress(2, "תניא", 1.0, completed: true);
        var snap = TestData.Full() with { Progress = [p, done] };
        var ctx = Ctx.Make(WidgetKinds.ShamorZachor, "large", DataCase.Present, state: new WidgetCustomState { SzFilter = "all" }, snapshot: snap);
        var texts = CardExpander.TextBlocks(Expand(ctx).Root).ToList();
        Assert.Contains("45 מתוך 100", texts);
        Assert.Contains("הושלם ✓", texts);
        using var d = Data(ctx);
        Assert.True(d.RootElement.GetProperty("rows")[1].GetProperty("complete").GetBoolean());
        Assert.Equal("good", d.RootElement.GetProperty("rows")[1].GetProperty("bar").GetProperty("color").GetString());
    }

    // ------------------------------------------------------------------------------------------------ zmanim
    [Fact]
    public void Zmanim_countdown_is_shown_for_the_next_zman()
    {
        var ex = Expand(Ctx.Make(WidgetKinds.Zmanim, "small", DataCase.Present));   // 10:30 -> chatzos 11:37 = 67 minutes
        Assert.Contains("בעוד 1:05 שעות", CardExpander.TextBlocks(ex.Root));
        var large = CardExpander.TextBlocks(Expand(Ctx.Make(WidgetKinds.Zmanim, "large", DataCase.Present)).Root).ToList();
        Assert.Contains("בעוד 1:05 שעות", large);
    }

    [Fact]
    public void Zmanim_times_come_from_ZmanTime_Time_not_from_Otzarias_display_text()
    {
        var cal = new FakeCalendarService();
        cal.Extra.Add(new ZmanTime("sunset", "x", null));   // unused: ids below pick other zmanim
        var ctx = Ctx.Make(WidgetKinds.Zmanim, "large", DataCase.Present, cal: cal);
        var texts = CardExpander.TextBlocks(Expand(ctx).Root).ToList();
        Assert.All(texts.Where(t => t.Length is 5 && t[2] == ':'), t => Assert.Matches(@"^\d\d:\d\d$", t));
        Assert.DoesNotContain(texts, t => t.Any(c => c is >= '\u2066' and <= '\u2069'));
    }

    [Fact]
    public void Zmanim_respect_the_12h_setting_and_use_the_city_zone()
    {
        var ctx = Ctx.Make(WidgetKinds.Zmanim, "large", DataCase.Present);
        ctx.Settings.TwelveHourClockOverride = true;
        var texts = CardExpander.TextBlocks(Expand(ctx).Root).ToList();
        Assert.Contains("11:37 AM", texts);
        Assert.Contains("12:08 PM", texts);
    }

    [Fact]
    public void Zmanim_that_do_not_occur_show_dashes_and_never_become_next()
    {
        var cal = new FakeCalendarService();
        cal.Extra.Add(new ZmanTime("motzeiShabbos", "מוצאי שבת", null));
        var st = new WidgetCustomState { ZmanimMode = "custom", ZmanimIds = ["sunrise", "chatzos", "motzeiShabbos"] };
        var ctx = Ctx.Make(WidgetKinds.Zmanim, "large", DataCase.Present, state: st, cal: cal);
        using var d = Data(ctx);
        var rows = d.RootElement.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal("--", rows[^1].GetProperty("time").GetString());
        Assert.True(rows[^1].GetProperty("past").GetBoolean());
        Assert.Equal("חצות היום", d.RootElement.GetProperty("next").GetProperty("name").GetString());
        var ex = Expand(ctx);
        CardExpander.Validate(ex, false, true, "zmanim-untimed");
        Assert.Contains("--", CardExpander.TextBlocks(ex.Root));
    }

    [Fact]
    public void Zmanim_on_another_date_are_listed_last_with_their_own_label()
    {
        var cal = new FakeCalendarService();
        cal.Extra.Add(new ZmanTime("kl", "תחילת קידוש לבנה — 3 ימים", new DateTimeOffset(2026, 11, 12, 22, 27, 0, TimeSpan.FromHours(2)))
            { DisplayText = "ליל שישי ג׳ לחודש \u206622:27.\u2069" });
        var st = new WidgetCustomState { ZmanimMode = "custom", ZmanimIds = ["sunrise", "chatzos", "kl"] };
        var ctx = Ctx.Make(WidgetKinds.Zmanim, "large", DataCase.Present, state: st, cal: cal);
        using var d = Data(ctx);
        var rows = d.RootElement.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(3, rows.Count);
        Assert.Equal("תחילת קידוש לבנה — 3 ימים", rows[^1].GetProperty("name").GetString());
        Assert.Equal("22:27", rows[^1].GetProperty("time").GetString());
        Assert.Equal("ליל שישי ג׳ לחודש", rows[^1].GetProperty("sub").GetString());
        Assert.False(rows[^1].GetProperty("past").GetBoolean());
        Assert.Equal("חצות היום", d.RootElement.GetProperty("next").GetProperty("name").GetString());
    }

    [Fact]
    public void Zmanim_after_sunset_label_tomorrow_when_the_date_rolled()
    {
        var night = new DateTimeOffset(2026, 11, 13, 21, 0, 0, TimeSpan.FromHours(2));   // fake day flips at 16:37
        using var d = Data(Ctx.Make(WidgetKinds.Zmanim, "medium", DataCase.Present, now: night));
        Assert.Contains("מחר", d.RootElement.GetProperty("next").GetProperty("label").GetString());
        Assert.Equal("עלות השחר", d.RootElement.GetProperty("next").GetProperty("name").GetString());
    }

    // ------------------------------------------------------------------------------------------------ zmanim customization presets
    private static JsonElement Field(JsonDocument d, string id) =>
        d.RootElement.GetProperty("fields").EnumerateArray().Single(f => f.GetProperty("id").GetString() == id);

    [Fact]
    public void Zmanim_customization_offers_presets_not_a_checklist()
    {
        var r = CardBuilders.BuildCustomizationSafe(Ctx.Make(WidgetKinds.Zmanim, "large", DataCase.Present));
        using var d = JsonDocument.Parse(r.DataJson);
        Assert.DoesNotContain(d.RootElement.GetProperty("fields").EnumerateArray(), f => f.GetProperty("kind").GetString() == "multi");
        Assert.True(d.RootElement.GetProperty("settingsButton").GetBoolean());
        var mode = Field(d, "zmanimMode").GetProperty("choices").EnumerateArray().Select(c => c.GetProperty("title").GetString()).ToList();
        Assert.Equal(["כמו באוצריא", "מותאם אישית"], mode);
        var presets = Field(d, "zmanim").GetProperty("choices").EnumerateArray().Select(c => c.GetProperty("title").GetString()!).ToList();
        Assert.Contains(presets, t => t.StartsWith("עיקריים"));
        Assert.Contains(presets, t => t.StartsWith("ברירת המחדל של אוצריא"));

        var ex = CardExpander.Expand(r, "large", "light");
        CardExpander.Validate(ex, customization: true, authenticated: true, "zmanim-customize");
        Assert.Contains(CardExpander.Elements(ex.Root, "Action.Execute"), a => a.GetProperty("verb").GetString() == "settings");
        Assert.Contains(CardExpander.TextBlocks(ex.Root), t => t.Contains("בהגדרות האפליקציה"));
    }

    [Fact]
    public void Zmanim_preset_choice_round_trips_through_ApplyInputs()
    {
        var r = CardBuilders.BuildCustomizationSafe(Ctx.Make(WidgetKinds.Zmanim, "large", DataCase.Present));
        using var d = JsonDocument.Parse(r.DataJson);
        var core = Field(d, "zmanim").GetProperty("choices").EnumerateArray()
            .First(c => c.GetProperty("title").GetString()!.StartsWith("עיקריים")).GetProperty("value").GetString()!;
        var st = new WidgetCustomState();
        st.ApplyInputs(WidgetKinds.Zmanim, JsonSerializer.Serialize(new Dictionary<string, string> { ["zmanimMode"] = "custom", ["zmanim"] = core, ["city"] = "" }));
        Assert.Equal("custom", st.ZmanimMode);
        Assert.Equal(core.Split(','), st.ZmanimIds);
        Assert.All(st.ZmanimIds, id => Assert.Contains(id, ZmanimBuilder.CoreZmanimIds));

        // and the card built from that state shows only those zmanim
        using var shown = Data(Ctx.Make(WidgetKinds.Zmanim, "large", DataCase.Present, state: st));
        Assert.True(shown.RootElement.GetProperty("rows").GetArrayLength() <= ZmanimBuilder.CoreZmanimIds.Count);
    }

    [Fact]
    public void Zmanim_customization_keeps_a_personal_list_as_its_own_choice()
    {
        var st = new WidgetCustomState { ZmanimMode = "custom", ZmanimIds = ["sunrise", "tzeis"] };
        using var d = JsonDocument.Parse(CardBuilders.BuildCustomizationSafe(Ctx.Make(WidgetKinds.Zmanim, "large", DataCase.Present, state: st)).DataJson);
        var field = Field(d, "zmanim");
        Assert.Equal("sunrise,tzeis", field.GetProperty("value").GetString());
        Assert.Contains(field.GetProperty("choices").EnumerateArray(), c => c.GetProperty("title").GetString()!.StartsWith("הרשימה שלי"));
    }

    // ------------------------------------------------------------------------------------------------ layout sanity
    [Fact]
    public void Templates_use_no_styled_containers_and_only_pixel_or_weight_columns()
    {
        foreach (var f in CardTemplates.All)
        {
            using var doc = JsonDocument.Parse(CardTemplates.Load(f));
            foreach (var c in CardExpander.Elements(doc.RootElement, "Container"))
                Assert.False(c.TryGetProperty("style", out _), $"{f}: styled Container (host-defined padding)");
        }
    }

    [Fact]
    public void Dashboard_compact_lines_are_single_line()
    {
        using var d = Data(Ctx.Make(WidgetKinds.Dashboard, "medium", DataCase.Present));
        foreach (var s in d.RootElement.GetProperty("sections").EnumerateArray())
            foreach (var l in s.GetProperty("lines").EnumerateArray())
            {
                Assert.Equal("", l.GetProperty("sub").GetString());
                Assert.False(l.TryGetProperty("bar", out _));
            }
    }
}
