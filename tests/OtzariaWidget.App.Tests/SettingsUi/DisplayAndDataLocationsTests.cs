using System.Reflection;
using OtzariaWidget.App.SettingsUi.Infrastructure;
using OtzariaWidget.App.SettingsUi.ViewModels;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.Tests.SettingsUi;

public class DisplayViewModelTests
{
    [Fact]
    public void Link_mode_and_hide_searches_edit_the_settings()
    {
        var store = new FakeSettingsStore();
        using var session = UiTestHelpers.WithoutSyncContext(() => new SettingsSession(store));
        var d = new DisplayViewModel(session);
        Assert.True(d.LinkOpenUrl);

        d.LinkProviderLaunch = true;
        Assert.Equal(LinkOpenMode.ProviderLaunch, session.Settings.LinkMode);
        Assert.False(d.LinkOpenUrl);
        d.LinkOpenUrl = true;
        Assert.Equal(LinkOpenMode.OpenUrl, session.Settings.LinkMode);

        d.HideSearches = true;
        Assert.True(session.Settings.HideSearches);
        session.Flush();
        Assert.True(store.Current.HideSearches);
    }

    [Fact]
    public void Theme_and_accent_options_are_gone_from_the_view_model()
    {
        // the Widgets Board owns the theme and cannot take custom colours; the options must not come back by accident
        var names = typeof(DisplayViewModel).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToList();
        Assert.DoesNotContain(names, n => n.Contains("Theme", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("Accent", StringComparison.OrdinalIgnoreCase) || n.Contains("Swatch") || n.Contains("Hex"));
    }

    [Fact]
    public void Display_page_xaml_has_no_theme_or_accent_section()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "OtzariaWidget.App", "SettingsUi", "Views", "DisplayPage.xaml"));
        Assert.DoesNotContain("ThemeFollow", xaml);
        Assert.DoesNotContain("Swatches", xaml);
        Assert.DoesNotContain("צבע הדגשה", xaml);
        Assert.Contains("HideSearches", xaml);
        Assert.Contains("LinkOpenUrl", xaml);
    }

    [Fact]
    public void No_settings_ui_file_uses_the_old_wording()
    {
        var root = Path.Combine(FindRepoRoot(), "src", "OtzariaWidget.App", "SettingsUi");
        foreach (var f in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".xaml") || f.EndsWith(".cs")))
            Assert.DoesNotContain("זהה לאוצריא", File.ReadAllText(f));
    }

    internal static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OtzariaWidget.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}

public class DataLocationsViewModelTests
{
    [Fact]
    public void Paths_are_trimmed_validated_browsed_and_reset()
    {
        var store = new FakeSettingsStore();
        var shell = new FakeUiShell();
        using var session = UiTestHelpers.WithoutSyncContext(() => new SettingsSession(store));
        var dl = UiTestHelpers.WithoutSyncContext(() => new DataLocationsViewModel(session, UiTestHelpers.Services(store, sh: shell)));
        var root = dl.Items[0];

        root.Text = "  \"C:\\no\\such\"  ";
        Assert.Equal(@"C:\no\such", session.Settings.DataRootOverride);
        Assert.Equal(StateKind.Error, root.State);

        root.Text = "";
        Assert.Null(session.Settings.DataRootOverride);
        Assert.Equal(StateKind.Neutral, root.State);

        shell.Picked = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        root.BrowseCommand.Execute(null);
        Assert.Equal(shell.Picked, session.Settings.DataRootOverride);
        Assert.True(root.State is StateKind.Ok or StateKind.Warning);

        root.ResetCommand.Execute(null);
        Assert.Null(session.Settings.DataRootOverride);

        var exe = dl.Items[3];
        exe.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");
        Assert.Equal(File.Exists(exe.Text) ? StateKind.Ok : StateKind.Error, exe.State);
        exe.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "win.ini");
        if (File.Exists(exe.Text)) Assert.Equal(StateKind.Warning, exe.State);

        Assert.True(UiTestHelpers.WaitFor(() => root.Resolved == @"C:\auto"));
    }
}
