using System.IO;
using System.Diagnostics;
using System.Windows;

namespace OtzariaWidget.App.SettingsUi.Infrastructure;

/// <summary>Everything that touches the OS shell / dialogs, behind an interface so view models stay testable.</summary>
public interface IShell
{
    void OpenFolder(string path);
    /// <summary>Opens a URI via the shell. Throws if no handler is registered.</summary>
    void OpenUri(string uri);
    string? PickFolder(string? initial, string title);
    string? PickFile(string? initial, string title, string filter);
}

public sealed class WindowsShell : IShell
{
    public void OpenFolder(string path)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
    }

    public void OpenUri(string uri)
    {
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }

    public string? PickFolder(string? initial, string title)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = title, Multiselect = false };
        if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial)) dlg.InitialDirectory = initial;
        return dlg.ShowDialog(Application.Current?.MainWindow) == true ? dlg.FolderName : null;
    }

    public string? PickFile(string? initial, string title, string filter)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        try
        {
            var dir = string.IsNullOrWhiteSpace(initial) ? null : Path.GetDirectoryName(initial);
            if (dir is not null && Directory.Exists(dir)) dlg.InitialDirectory = dir;
        }
        catch { }
        return dlg.ShowDialog(Application.Current?.MainWindow) == true ? dlg.FileName : null;
    }
}
