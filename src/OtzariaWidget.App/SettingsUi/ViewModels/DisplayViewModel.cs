using OtzariaWidget.App.SettingsUi.Infrastructure;
using OtzariaWidget.App.SettingsUi.Localization;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.SettingsUi.ViewModels;

/// <summary>Page 4 - "תצוגה והתנהגות". (Theme/accent options were removed on purpose: the Widgets Board renders Adaptive Cards with its
/// own theme and cannot take custom colours. <c>WidgetAppSettings.Theme</c>/<c>AccentColor</c> stay in the contract untouched.)</summary>
public sealed class DisplayViewModel : ObservableObject
{
    private static readonly string[] LanguageCodes = [UiLanguage.SystemCode, UiLanguage.Hebrew.Code, UiLanguage.English.Code];

    private readonly SettingsSession _session;
    private readonly Action? _languageChanged;

    /// <param name="languageChanged">Called after the language choice was stored; the shell rebuilds the window in the new language.</param>
    public DisplayViewModel(SettingsSession session, Action? languageChanged = null)
    {
        _session = session;
        _languageChanged = languageChanged;
        // language names are always shown in the language itself
        LanguageOptions = [Loc.T("אוטומטי (לפי שפת Windows)"), UiLanguage.Hebrew.Label, UiLanguage.English.Label];
    }

    public string Title => Loc.T("תצוגה והתנהגות");

    // ---- settings-window language
    public IReadOnlyList<string> LanguageOptions { get; }

    public int LanguageIndex
    {
        get
        {
            var chosen = UiLanguage.FromCode(_session.Settings.UiLanguage);
            return chosen is null ? 0 : Array.IndexOf(LanguageCodes, chosen.Code);
        }
        set
        {
            if (value < 0 || value >= LanguageCodes.Length || value == LanguageIndex) return;
            _session.Settings.UiLanguage = LanguageCodes[value];
            Raise();
            _session.MarkChanged();
            _languageChanged?.Invoke();
        }
    }

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
