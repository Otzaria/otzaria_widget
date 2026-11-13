using System.Text.Json;
using OtzariaWidget.App.Provider.Cards;

namespace OtzariaWidget.App.Tests;

public class CustomStateTests
{
    [Fact]
    public void Default_roundtrip_is_stable()
    {
        var s = new WidgetCustomState();
        var json = s.Serialize();
        var back = WidgetCustomState.Parse(json);
        Assert.Equal(json, back.Serialize());
        Assert.Equal(WidgetCustomState.CurrentSchema, back.Version);
        Assert.Equal(["date", "zman", "reading", "progress", "bookmarks"], back.Sections);
        Assert.True(back.ShowLocation);
        Assert.True(back.IncludePdfs);
        Assert.False(back.ShowSearches);
        Assert.Null(back.ShowYerushalmi);
    }

    [Fact]
    public void Full_roundtrip_preserves_every_field()
    {
        var s = new WidgetCustomState
        {
            Count = 5, ShowSearches = true, ShowLocation = false, IncludePdfs = false, Group = "g2",
            SzFilter = "completed", ShowAllColumns = true, City = "בני ברק", ZmanimMode = "custom",
            ZmanimIds = ["sunrise", "sunset"], ShowDaf = false, ShowYerushalmi = true, ShowOmer = false, ShowCandle = false,
            Sections = ["reading", "date"],
        };
        var b = WidgetCustomState.Parse(s.Serialize());
        Assert.Equal(5, b.Count);
        Assert.True(b.ShowSearches); Assert.False(b.ShowLocation); Assert.False(b.IncludePdfs);
        Assert.Equal("g2", b.Group);
        Assert.Equal("completed", b.SzFilter); Assert.True(b.ShowAllColumns);
        Assert.Equal("בני ברק", b.City);
        Assert.Equal("custom", b.ZmanimMode);
        Assert.Equal(["sunrise", "sunset"], b.ZmanimIds);
        Assert.False(b.ShowDaf); Assert.True(b.ShowYerushalmi); Assert.False(b.ShowOmer); Assert.False(b.ShowCandle);
        Assert.Equal(["reading", "date"], b.Sections);
        // Hebrew survives as readable UTF-8 JSON
        Assert.Contains("בני ברק", s.Serialize());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("\"string\"")]
    [InlineData("42")]
    [InlineData("{")]
    [InlineData("{\"count\":\"abc\",\"showSearches\":\"maybe\",\"szFilter\":7,\"zmanim\":{\"a\":1},\"sections\":\"x\"}")]
    [InlineData("{\"v\":99,\"count\":-5,\"unknownField\":{\"x\":[1]}}")]
    public void Garbage_yields_defaults_without_throwing(string? json)
    {
        var s = WidgetCustomState.Parse(json);
        Assert.InRange(s.Count, 0, 12);
        Assert.Contains(s.SzFilter, new[] { "inProgress", "completed", "all" });
        Assert.NotNull(s.Group);
        Assert.NotNull(s.Sections);
        Assert.False(s.ShowSearches);
        // and it can always be re-serialized
        using var _ = JsonDocument.Parse(s.Serialize());
    }

    [Fact]
    public void Out_of_range_and_unknown_values_are_sanitised()
    {
        var s = WidgetCustomState.Parse("{\"count\":999,\"szFilter\":\"weird\",\"zmanimMode\":\"x\",\"sections\":[\"date\",\"bogus\",\"date\",\"zman\"]}");
        Assert.Equal(12, s.Count);
        Assert.Equal("inProgress", s.SzFilter);
        Assert.Equal("follow", s.ZmanimMode);
        Assert.Equal(["date", "zman"], s.Sections);
    }

    [Fact]
    public void Explicitly_empty_sections_are_kept()
    {
        var s = WidgetCustomState.Parse("{\"sections\":[]}");
        Assert.Empty(s.Sections);
    }

    [Fact]
    public void ApplyInputs_parses_string_values_from_the_card()
    {
        var s = new WidgetCustomState();
        s.ApplyInputs(WidgetKinds.RecentBooks, "{\"count\":\"4\",\"showSearches\":\"true\",\"showLocation\":\"false\",\"includePdfs\":\"true\"}");
        Assert.Equal(4, s.Count); Assert.True(s.ShowSearches); Assert.False(s.ShowLocation);

        s.ApplyInputs(WidgetKinds.Zmanim, "{\"city\":\"ניו יורק\",\"zmanimMode\":\"custom\",\"zmanim\":\"sunrise,sunset, tzeis\"}");
        Assert.Equal("ניו יורק", s.City); Assert.Equal("custom", s.ZmanimMode);
        Assert.Equal(["sunrise", "sunset", "tzeis"], s.ZmanimIds);

        s.ApplyInputs(WidgetKinds.ShamorZachor, "{\"szFilter\":\"all\",\"count\":2,\"szAllColumns\":true}");
        Assert.Equal("all", s.SzFilter); Assert.Equal(2, s.Count); Assert.True(s.ShowAllColumns);

        s.ApplyInputs(WidgetKinds.Bookmarks, "{\"group\":\"none\"}");
        Assert.Equal("none", s.Group);

        s.ApplyInputs(WidgetKinds.HebrewCalendar, "{\"showYerushalmi\":\"true\",\"showOmer\":\"false\"}");
        Assert.True(s.ShowYerushalmi); Assert.False(s.ShowOmer);
    }

    [Fact]
    public void Dashboard_positions_define_order_and_visibility()
    {
        var s = new WidgetCustomState();
        s.ApplyInputs(WidgetKinds.Dashboard,
            "{\"pos_date\":\"0\",\"pos_zman\":\"3\",\"pos_reading\":\"1\",\"pos_progress\":\"2\",\"pos_bookmarks\":\"0\"}");
        Assert.Equal(["reading", "progress", "zman"], s.Sections);
    }

    [Fact]
    public void ApplyInputs_ignores_garbage_and_unknown_keys()
    {
        var s = new WidgetCustomState { Count = 3 };
        s.ApplyInputs(WidgetKinds.RecentBooks, "not json");
        s.ApplyInputs(WidgetKinds.RecentBooks, null);
        s.ApplyInputs(WidgetKinds.RecentBooks, "[1]");
        s.ApplyInputs(WidgetKinds.RecentBooks, "{\"nope\":1,\"count\":\"zz\"}");
        Assert.Equal(3, s.Count);
        s.ApplyInputs("UnknownKind", "{\"count\":9}");
        Assert.Equal(3, s.Count);
    }
}
