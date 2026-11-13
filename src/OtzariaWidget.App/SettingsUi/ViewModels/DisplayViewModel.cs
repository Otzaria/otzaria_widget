using OtzariaWidget.App.SettingsUi.Infrastructure;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.SettingsUi.ViewModels;

/// <summary>Page 4 - "תצוגה והתנהגות". (Theme/accent options were removed on purpose: the Widgets Board renders Adaptive Cards with its
/// own theme and cannot take custom colours. <c>WidgetAppSettings.Theme</c>/<c>AccentColor</c> stay in the contract untouched.)</summary>
public sealed class DisplayViewModel : ObservableObject
{
    private readonly SettingsSession _session;

    public DisplayViewModel(SettingsSession session)
    {
        _session = session;
    }

    public string Title => "תצוגה והתנהגות";

    // ---- link mode
    public bool LinkOpenUrl
    {
        get => _session.Settings.LinkMode == LinkOpenMode.OpenUrl;
        set { if (value) SetLinkMode(LinkOpenMode.OpenUrl); }
    }

    public bool LinkProviderLaunch
    {
        get => _session.Settings.LinkMode == LinkOpenMode.ProviderLaunch;
        set { if (value) SetLinkMode(LinkOpenMode.ProviderLaunch); }
    }

    private void SetLinkMode(LinkOpenMode mode)
    {
        if (_session.Settings.LinkMode == mode) return;
        _session.Settings.LinkMode = mode;
        Raise(nameof(LinkOpenUrl));
        Raise(nameof(LinkProviderLaunch));
        _session.MarkChanged();
    }

    // ---- privacy
    public bool HideSearches
    {
        get => _session.Settings.HideSearches;
        set
        {
            if (_session.Settings.HideSearches == value) return;
            _session.Settings.HideSearches = value;
            Raise();
            _session.MarkChanged();
        }
    }
}
