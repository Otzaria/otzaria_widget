using System.Diagnostics;
using OtzariaWidget.Core.Contracts;
using OtzariaWidget.Core.Launch;

namespace OtzariaWidget.Core.Tests.Launch;

public class OtzariaLauncherTests
{
    private readonly OtzariaLauncher _l = new();

    private static ReadingEntry E(int? id, string type = "TextBook", string source = "o", int index = 0) => new()
    {
        Book = new BookRef { Id = id, Title = "ספר", Type = type, Source = source },
        Index = index,
    };

    [Theory]
    [InlineData(1938, "TextBook", "o", 140, "otzaria://open/book/1938?index=140")]
    [InlineData(1938, "TextBook", "o", 0, "otzaria://open/book/1938?index=0")]
    [InlineData(1938, "TextBook", "o", -3, "otzaria://open/book/1938?index=0")]
    [InlineData(131, "PdfBook", "o", 150, "otzaria://open/pdf/131?index=150")]
    [InlineData(131, "PdfBook", "o", 0, "otzaria://open/pdf/131?index=1")]
    [InlineData(5, "TextBook", "u", 7, "otzaria://open/book/5?source=user&index=7")]
    [InlineData(5, "PdfBook", "u", 7, "otzaria://open/pdf/5?source=user&index=7")]
    [InlineData(5, "TextBook", "d:my-lib_1.2", 7, "otzaria://open/book/5?source=db:my-lib_1.2&index=7")]
    [InlineData(6, "DocxBook", "o", 2, "otzaria://open/book/6?index=2")]
    [InlineData(6, "EpubBook", "o", 2, "otzaria://open/book/6?index=2")]
    [InlineData(6, "DocumentBook", "o", 2, "otzaria://open/book/6?index=2")]
    public void BuildLink_mapping(int id, string type, string source, int index, string expected)
        => Assert.Equal(expected, _l.BuildLink(E(id, type, source, index)));

    [Fact]
    public void BuildLink_hebrew_slug_is_percent_encoded()
    {
        var link = _l.BuildLink(E(5, source: "d:ספרים"));
        Assert.Equal("otzaria://open/book/5?source=db:" + Uri.EscapeDataString("ספרים") + "&index=0", link);
    }

    [Theory]
    [InlineData(null, "TextBook", "o")]
    [InlineData(0, "TextBook", "o")]
    [InlineData(-1, "TextBook", "o")]
    [InlineData(3, "ExternalLibraryBook", "o")]
    [InlineData(3, "WeirdBook", "o")]
    [InlineData(3, "TextBook", "x:strange")]
    [InlineData(3, "TextBook", "d:bad slug!")]
    [InlineData(3, "TextBook", "d:")]
    public void BuildLink_not_linkable(int? id, string type, string source)
        => Assert.Null(_l.BuildLink(E(id, type, source)));

    [Fact]
    public void BuildSearchLink()
    {
        Assert.Equal("otzaria://open/search?q=" + Uri.EscapeDataString("שלום עולם") + "&mode=exact", _l.BuildSearchLink("שלום עולם", "exact"));
        Assert.Equal("otzaria://open/search?q=a%26b%3Dc", _l.BuildSearchLink("a&b=c", null));
        Assert.Equal("otzaria://open/search?q=x", _l.BuildSearchLink("x", "bogus"));
        Assert.EndsWith("&mode=smart", _l.BuildSearchLink("x", "Smart"));
        Assert.EndsWith("&mode=fuzzy", _l.BuildSearchLink("x", "fuzzy"));
    }

    [Fact]
    public void BuildBookOpenLink() => Assert.Equal("otzaria://open/book/12", OtzariaLauncher.BuildBookOpenLink(12));

    // ---- Launch ---------------------------------------------------------------------------------------
    private static OtzariaLauncher WithFakes(List<ProcessStartInfo> started, bool protocol, bool exeExists, bool starterResult = true, bool throws = false)
        => new(psi =>
            {
                if (throws) throw new InvalidOperationException("boom");
                started.Add(psi);
                return starterResult;
            },
            () => protocol,
            _ => exeExists);

    [Fact]
    public void Launch_with_exe_passes_uri_as_single_argument()
    {
        var started = new List<ProcessStartInfo>();
        var l = WithFakes(started, protocol: false, exeExists: true);
        var uri = "otzaria://open/book/1?index=2&mark";
        Assert.True(l.Launch(uri, new OtzariaPaths { OtzariaExe = @"C:\Otz\otzaria.exe" }));
        var psi = Assert.Single(started);
        Assert.Equal(@"C:\Otz\otzaria.exe", psi.FileName);
        Assert.False(psi.UseShellExecute);
        Assert.Equal(new[] { uri }, psi.ArgumentList.ToArray());
    }

    [Fact]
    public void Launch_null_uri_starts_exe_without_args()
    {
        var started = new List<ProcessStartInfo>();
        var l = WithFakes(started, protocol: false, exeExists: true);
        Assert.True(l.Launch(null, new OtzariaPaths { OtzariaExe = @"C:\Otz\otzaria.exe" }));
        Assert.Empty(started.Single().ArgumentList);
    }

    [Fact]
    public void Launch_falls_back_to_shell_protocol_when_exe_unknown()
    {
        var started = new List<ProcessStartInfo>();
        var l = WithFakes(started, protocol: true, exeExists: false);
        Assert.True(l.Launch("otzaria://open/history", new OtzariaPaths()));
        var psi = Assert.Single(started);
        Assert.True(psi.UseShellExecute);
        Assert.Equal("otzaria://open/history", psi.FileName);
    }

    [Fact]
    public void Launch_without_exe_and_protocol_returns_false()
    {
        var started = new List<ProcessStartInfo>();
        var l = WithFakes(started, protocol: false, exeExists: false);
        Assert.False(l.Launch("otzaria://open/history", new OtzariaPaths { OtzariaExe = @"C:\gone.exe" }));
        Assert.False(l.Launch(null, new OtzariaPaths()));
        Assert.Empty(started);
    }

    [Fact]
    public void Launch_null_uri_without_exe_does_not_use_protocol()
    {
        var started = new List<ProcessStartInfo>();
        var l = WithFakes(started, protocol: true, exeExists: false);
        Assert.False(l.Launch(null, new OtzariaPaths()));
        Assert.Empty(started);
    }

    [Fact]
    public void Launch_never_runs_plugin_install_or_foreign_uris()
    {
        var started = new List<ProcessStartInfo>();
        var l = WithFakes(started, protocol: true, exeExists: true);
        var paths = new OtzariaPaths { OtzariaExe = @"C:\Otz\otzaria.exe" };
        Assert.False(l.Launch("otzaria://plugin/install?url=http://evil", paths));
        Assert.False(l.Launch("otzaria://plugin/install-local?path=C:\\x", paths));
        Assert.False(l.Launch("https://example.com", paths));
        Assert.False(l.Launch("calc.exe", paths));
        Assert.Empty(started);
    }

    [Fact]
    public void Launch_failure_returns_false_never_throws()
    {
        var l = WithFakes([], protocol: true, exeExists: true, throws: true);
        Assert.False(l.Launch("otzaria://open/history", new OtzariaPaths { OtzariaExe = @"C:\Otz\otzaria.exe" }));
        var l2 = WithFakes([], protocol: true, exeExists: true, starterResult: false);
        Assert.False(l2.Launch("otzaria://open/history", new OtzariaPaths { OtzariaExe = @"C:\Otz\otzaria.exe" }));
    }
}
