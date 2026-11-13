using System.Collections.ObjectModel;
using OtzariaWidget.App.SettingsUi.Infrastructure;

namespace OtzariaWidget.App.SettingsUi.ViewModels;

public sealed record NavItem(string Title, string Glyph, ObservableObject Page);

/// <summary>Shell view model: navigation + the shared settings session.</summary>
public sealed class MainViewModel : ObservableObject
{
    private NavItem _selected;

    public MainViewModel(SettingsServices services)
    {
        Services = services;
        Session = new SettingsSession(services.Store);
        Hub = new SnapshotHub(services.Data);

        Status = new StatusViewModel(Session, services, Hub);
        DataLocations = new DataLocationsViewModel(Session, services);
        Times = new TimesViewModel(Session, services, Hub);
        Display = new DisplayViewModel(Session);
        About = new AboutViewModel(services);

        Pages =
        [
            new NavItem("מצב", "", Status),
            new NavItem("מיקום הנתונים", "", DataLocations),
            new NavItem("זמנים ולוח שנה", "", Times),
            new NavItem("תצוגה והתנהגות", "", Display),
            new NavItem("אודות", "", About),
        ];
        _selected = Pages[0];

        // Otzaria's files or our own settings changed: re-read the snapshot so Status/Times stay truthful.
        Session.Saved += (_, _) => _ = Hub.RefreshAsync(true);
        _ = Hub.RefreshAsync(false);
    }

    public SettingsServices Services { get; }
    public SettingsSession Session { get; }
    public SnapshotHub Hub { get; }

    public StatusViewModel Status { get; }
    public DataLocationsViewModel DataLocations { get; }
    public TimesViewModel Times { get; }
    public DisplayViewModel Display { get; }
    public AboutViewModel About { get; }

    public bool HasBanner => !string.IsNullOrEmpty(Session.LoadError);

    public ObservableCollection<NavItem> Pages { get; }

    public NavItem SelectedPage
    {
        get => _selected;
        set { if (value is not null) Set(ref _selected, value); }
    }
}
