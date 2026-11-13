using System.Diagnostics;
using OtzariaWidget.Core.Contracts;
using OtzariaWidget.Core.Launch;

namespace OtzariaWidget.Core.Tests.Launch;

/// <summary>QA regression tests: launcher safety (no argument/command injection, no plugin URIs) and link fidelity.</summary>
public class OtzariaLauncherQaTests
{
    private readonly OtzariaLauncher _l = new();

    private static ReadingEntry E(int? id, string type = "TextBook", string source = "o", int index = 0) => new()
    {
        Book = new BookRef { Id = id, Title = "ספר", Type = type, Source = source },
        Index = index,
    };

    // ---- IsSafeOtzariaUri -------------------------------------------------------------------------------
    [Theory]
    [InlineData("otzaria://open/book/1?index=2")]
    [InlineData("OTZARIA://OPEN/book/1")]
    [InlineData("otzaria://open/pdf/131?index=150")]
    [InlineData("otzaria://open/search?q=%D7%A9%D7%9C%D7%95%D7%9D&mode=exact")]
    [InlineData("otzaria://open/history")]
    [InlineData("otzaria://open/shamor_zachor")]
    [InlineData("otzaria://open/book/5?source=db:my-lib_1.2&index=7")]
    [InlineData("otzaria://open/book/1&calc")]               // `&` is plain query syntax; no shell is involved
    public void Safe_uris_are_accepted(string uri) => Assert.True(OtzariaLauncher.IsSafeOtzariaUri(uri));

    [Theory]
    [InlineData("")]
    [InlineData("otzaria:")]
    [InlineData("otzaria://")]
    [InlineData("otzaria://plugin/install?url=x")]
    [InlineData("otzaria://PLUGIN/install-local?path=C:%5Cx")]
    [InlineData("otzaria://library/reindex")]
    [InlineData("otzaria://window/new")]
    [InlineData("otzaria://info/errors")]
    [InlineData("otzaria://open/plugin/some-id")]
    [InlineData("otzaria://open/%70lugin/some-id")]          // percent-encoded segment
    [InlineData("otzaria://open//plugin/some-id")]           // empty segments are ignored by Otzaria
    [InlineData("otzaria://open/book/../plugin/x")]
    [InlineData("otzaria://open@plugin/install")]            // userinfo trick
    [InlineData("otzaria:///plugin/install")]
    [InlineData("otzaria:/plugin/install")]
    [InlineData("zayit://book/5")]
    [InlineData("https://otzaria://open/book/1")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("otzaria://open/book/1\" --evil")]           // would break out of "%1" in a registered command
    [InlineData("otzaria://open/book/1 --evil")]
    [InlineData("otzaria://open/book/1\t--evil")]
    [InlineData("otzaria://open/book/1\r\n")]
    public void Unsafe_uris_are_rejected(string uri) => Assert.False(OtzariaLauncher.IsSafeOtzariaUri(uri));

    [Theory]
    [InlineData("otzaria://open/book/1|calc")]
    [InlineData("otzaria://open/book/1^&calc")]
    [InlineData("otzaria://open/book/1<x")]
    [InlineData("otzaria://open/book/1>x")]
    [InlineData("otzaria://open/book/1`x")]
    [InlineData("otzaria://open/book/1\\x")]
    [InlineData("otzaria://open/book/1\u007f")]
    public void Shell_metacharacters_are_rejected(string uri) => Assert.False(OtzariaLauncher.IsSafeOtzariaUri(uri));

    [Fact]
    public void Overlong_uri_is_rejected()
    {
        Assert.False(OtzariaLauncher.IsSafeOtzariaUri("otzaria://open/search?q=" + new string('a', OtzariaLauncher.MaxUriLength)));
        Assert.True(OtzariaLauncher.IsSafeOtzariaUri("otzaria://open/search?q=" + new string('a', 1000)));
    }

    [Fact]
    public void Launch_rejects_unsafe_uris_on_both_paths_and_never_starts_a_process()
    {
        var started = new List<ProcessStartInfo>();
        var l = new OtzariaLauncher(psi => { started.Add(psi); return true; }, () => true, _ => true);
        var paths = new OtzariaPaths { OtzariaExe = @"C:\Otz\otzaria.exe" };
        foreach (var uri in new[] { "otzaria://open/book/1\" --x", "otzaria://open/plugin/a", "otzaria://plugin/install?x=1", "calc.exe", "otzaria://open/%70lugin/x" })
        {
            Assert.False(l.Launch(uri, paths));
            Assert.False(l.Launch(uri, new OtzariaPaths()));          // ShellExecute fallback path
        }
        Assert.Empty(started);
    }

    [Fact]
    public void Launch_passes_hebrew_percent_encoded_uri_as_one_argument_without_shell()
    {
        var started = new List<ProcessStartInfo>();
        var l = new OtzariaLauncher(psi => { started.Add(psi); return true; }, () => false, _ => true);
        var uri = _l.BuildSearchLink("אור זרוע & \"x\"", "exact");
        Assert.True(l.Launch(uri, new OtzariaPaths { OtzariaExe = @"C:\Program Files\Otzaria\otzaria.exe" }));
        var psi = Assert.Single(started);
        Assert.False(psi.UseShellExecute);
        Assert.Equal(new[] { uri }, psi.ArgumentList.ToArray());
        Assert.True(string.IsNullOrEmpty(psi.Arguments));
        Assert.Equal(@"C:\Program Files\Otzaria", psi.WorkingDirectory);
    }

    // ---- slugs / source param -----------------------------------------------------------------------------
    [Theory]
    [InlineData("slug\n")]            // .NET `$` would accept this
    [InlineData("sl ug")]
    [InlineData("a/b")]
    [InlineData("a&b")]
    [InlineData("a:b")]
    [InlineData("")]
    [InlineData("..\\x")]
    public void Invalid_slugs_make_the_entry_not_linkable(string slug)
        => Assert.Null(_l.BuildLink(E(5, "TextBook", "d:" + slug, 1)));

    [Fact]
    public void Slug_longer_than_64_is_not_linkable_and_exactly_64_is()
    {
        Assert.Null(_l.BuildLink(E(5, "TextBook", "d:" + new string('a', 65), 1)));
        Assert.NotNull(_l.BuildLink(E(5, "TextBook", "d:" + new string('a', 64), 1)));
    }

    [Theory]
    [InlineData("x")]
    [InlineData("official")]
    [InlineData("db:slug")]
    [InlineData("U")]
    public void Unknown_source_keys_are_not_linkable(string source) => Assert.Null(_l.BuildLink(E(5, "TextBook", source, 1)));

    [Theory]
    [InlineData("ExternalLibraryBook")]
    [InlineData("AlienBook")]
    [InlineData("")]
    public void Non_linkable_book_types_have_no_link(string type) => Assert.Null(_l.BuildLink(E(5, type, "o", 1)));

    [Fact]
    public void Link_never_depends_on_target_kind_or_commentators()
    {
        var e = E(10, "TextBook", "o", 7) with { TargetKind = "commentators", Commentators = ["רש\"י"] };
        Assert.Equal("otzaria://open/book/10?index=7", _l.BuildLink(e));
    }

    [Theory]
    [InlineData(int.MaxValue, "otzaria://open/book/5?index=2147483647")]
    [InlineData(int.MinValue, "otzaria://open/book/5?index=0")]
    public void Extreme_indexes_do_not_overflow(int index, string expected) => Assert.Equal(expected, _l.BuildLink(E(5, "TextBook", "o", index)));

    [Fact]
    public void Null_entry_or_book_returns_null_without_throwing()
    {
        Assert.Null(_l.BuildLink(null!));
        Assert.Null(_l.BuildLink(new ReadingEntry { Book = null! }));
    }

    // ---- search links ---------------------------------------------------------------------------------------
    [Fact]
    public void Search_link_round_trips_hebrew_nikud_and_reserved_characters_as_utf8()
    {
        var q = "בְּרֵאשִׁית ברא & x=y?z#w+%41 \"q\"";
        var link = _l.BuildSearchLink(q, "fuzzy");
        Assert.Matches("^[A-Za-z0-9%._~:/?&=-]+$", link);                  // pure ASCII, nothing shell-significant
        var raw = link["otzaria://open/search?q=".Length..];
        var qv = raw[..raw.IndexOf('&')];
        Assert.Equal(q, Uri.UnescapeDataString(qv));
        Assert.EndsWith("&mode=fuzzy", link);
    }

    [Theory]
    [InlineData("smart", "&mode=smart")]
    [InlineData(" EXACT ", "&mode=exact")]
    [InlineData("regex", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Search_modes_are_normalised_and_whitelisted(string? mode, string suffix)
        => Assert.EndsWith("q=x" + suffix, _l.BuildSearchLink("x", mode));

    [Fact]
    public void Absurdly_long_query_degrades_to_the_search_screen_and_lone_surrogates_do_not_throw()
    {
        Assert.Equal("otzaria://open/search", _l.BuildSearchLink(new string('א', 5000), null));
        Assert.StartsWith("otzaria://open/search", _l.BuildSearchLink("\uD800 broken", null));
        Assert.Equal("otzaria://open/search?q=", _l.BuildSearchLink(null!, null));
    }

    [Fact]
    public void Plugin_install_urls_are_never_generated()
    {
        foreach (var q in new[] { "plugin/install", "otzaria://plugin/install", "../plugin" })
            Assert.StartsWith("otzaria://open/search?q=", _l.BuildSearchLink(q, null));
        Assert.True(OtzariaLauncher.IsSafeOtzariaUri(_l.BuildSearchLink("otzaria://plugin/install?x=1", null)));
    }
}
