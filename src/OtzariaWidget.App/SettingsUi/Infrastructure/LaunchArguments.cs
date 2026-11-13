namespace OtzariaWidget.App.SettingsUi.Infrastructure;

/// <summary>Command-line forms the settings window understands.</summary>
public static class LaunchArguments
{
    public const string Protocol = "otzaria-widget:";

    /// <summary>
    /// Protocol activation passes <c>otzaria-widget://settings[/page]</c> as an argument (Windows appends a trailing slash:
    /// <c>otzaria-widget://settings/</c>). page = status | data | times | display | about (0-4). Anything else just opens the window.
    /// The URI is only ever used to choose a page; nothing in it is executed.
    /// </summary>
    public static int? PageFromProtocolUri(IEnumerable<string> args)
    {
        var uri = args.FirstOrDefault(a => a.StartsWith(Protocol, StringComparison.OrdinalIgnoreCase));
        if (uri is null) return null;
        var rest = uri[Protocol.Length..].TrimStart('/');
        var parts = rest.Split(['/', '?', '#'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].Equals("settings", StringComparison.OrdinalIgnoreCase)) return null;
        return parts[1].ToLowerInvariant() switch { "status" => 0, "data" => 1, "times" => 2, "display" => 3, "about" => 4, _ => null };
    }
}
