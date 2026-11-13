using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Input;
using OtzariaWidget.App.SettingsUi.Infrastructure;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.SettingsUi.ViewModels;

public sealed class ZmanItemViewModel : ObservableObject
{
    private bool _isChecked;
    private readonly Action<ZmanItemViewModel> _onToggled;

    public ZmanItemViewModel(ZmanDefinition def, bool isChecked, Action<ZmanItemViewModel> onToggled)
    {
        Id = def.Id;
        Title = def.Title;
        Description = def.Description;
        _isChecked = isChecked;
        _onToggled = onToggled;
    }

    public string Id { get; }
    public string Title { get; }
    public string? Description { get; }

    public bool IsChecked
    {
        get => _isChecked;
        set { if (Set(ref _isChecked, value)) _onToggled(this); }
    }

    /// <summary>Sets the value without notifying the owner (bulk operations).</summary>
    public void SetSilently(bool value)
    {
        _isChecked = value;
        Raise(nameof(IsChecked));
    }
}

public sealed record PreviewRow(string Title, string TimeText, bool IsPast, bool IsNext, bool Missing);

/// <summary>Page 3 - "זמנים ולוח שנה" including the live preview.</summary>
public sealed class TimesViewModel : ObservableObject
{
    private static readonly string?[] DayTransitionValues = [null, "sunset", "tzais", "rabbeinuTam", "midnight"];
    private static readonly string[] DayTransitionLabels =
        ["כמו באוצריא", "שקיעה", "צאת הכוכבים", "רבנו תם", "חצות"];

    private readonly SettingsSession _session;
    private readonly SettingsServices _svc;
    private readonly SnapshotHub _hub;
    private bool _syncing;
    private int _previewGen;

    // city
    private bool _followCity = true;
    private string _cityFilter = "";
    private CityInfo? _selectedCity;
    private string? _cityWarning;
    // zmanim
    private bool _followZmanim = true;
    // format / transition
    private int _timeFormatIndex;
    private int _dayTransitionIndex;
    private bool _showYerushalmi;
    // preview
    private string _previewCity = "";
    private string _previewDate = "";
    private string _previewGregorian = "";
    private string _previewExtra = "";
    private string? _previewError;
    private bool _previewBusy;

    private readonly List<CityInfo> _allCities = [];
    private ListCollectionView? _citiesView;
    private IReadOnlyList<string> _defaultZmanim = [];

    public TimesViewModel(SettingsSession session, SettingsServices svc, SnapshotHub hub)
    {
        _session = session;
        _svc = svc;
        _hub = hub;

        LoadCalendarData();
        LoadFromSettings();

        SelectAllZmanimCommand = new RelayCommand(() => SetAllZmanim(_ => true));
        ClearZmanimCommand = new RelayCommand(() => SetAllZmanim(_ => false));
        DefaultZmanimCommand = new RelayCommand(() =>
        {
            var d = new HashSet<string>(_defaultZmanim);
            SetAllZmanim(id => d.Contains(id));
        });
        RefreshPreviewCommand = new RelayCommand(() => _ = RefreshPreviewAsync());

        _hub.Updated += (_, _) => { Raise(nameof(OtzariaCityText)); Raise(nameof(TimeFormatHint)); Raise(nameof(DayTransitionHint)); _ = RefreshPreviewAsync(); };
        _ = RefreshPreviewAsync();
    }

    public string Title => "זמנים ולוח שנה";

    // ------------------------------------------------------------------ data load

    public string? CalendarError { get; private set; }

    private void LoadCalendarData()
    {
        try
        {
            _allCities.AddRange(_svc.Calendar.Cities);
            var view = new ListCollectionView(_allCities);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CityInfo.Country)));
            view.Filter = o => o is CityInfo c && MatchesFilter(c);
            _citiesView = view;
        }
        catch (Exception ex)
        {
            SettingsLog.Error("Loading cities failed", ex);
            CalendarError = ErrorText.From(ex);
        }

        try
        {
            _defaultZmanim = _svc.Calendar.DefaultEnabledZmanim.ToList();
            var initial = EffectiveZmanimIds(null);
            foreach (var def in _svc.Calendar.ZmanimRegistry)
                Zmanim.Add(new ZmanItemViewModel(def, initial.Contains(def.Id), OnZmanToggled));
        }
        catch (Exception ex)
        {
            SettingsLog.Error("Loading zmanim registry failed", ex);
            CalendarError ??= ErrorText.From(ex);
        }
        Raise(nameof(CalendarError));
        Raise(nameof(HasCalendarError));
    }

    public bool HasCalendarError => !string.IsNullOrEmpty(CalendarError);

    private bool MatchesFilter(CityInfo c)
    {
        var f = _cityFilter.Trim();
        if (f.Length == 0) return true;
        return c.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
            || c.Country.Contains(f, StringComparison.OrdinalIgnoreCase);
    }

    private void LoadFromSettings()
    {
        var s = _session.Settings;
        _syncing = true;
        try
        {
            _followCity = s.CityOverride is null;
            _selectedCity = s.CityOverride is null ? null : _allCities.FirstOrDefault(c => c.Name == s.CityOverride);
            _cityWarning = s.CityOverride is not null && _selectedCity is null && _allCities.Count > 0
                ? $"העיר \"{s.CityOverride}\" אינה ברשימה."
                : null;

            _followZmanim = s.ZmanimOverride is null;
            if (s.ZmanimOverride is not null)
            {
                var set = new HashSet<string>(s.ZmanimOverride);
                foreach (var z in Zmanim) z.SetSilently(set.Contains(z.Id));
            }

            _timeFormatIndex = s.TwelveHourClockOverride switch { null => 0, false => 1, true => 2 };
            var di = Array.IndexOf(DayTransitionValues, s.DayTransitionOverride);
            _dayTransitionIndex = di < 0 ? 0 : di;
            _showYerushalmi = s.ShowYerushalmiDaf;
        }
        finally { _syncing = false; }
    }

    private void Commit()
    {
        if (_syncing) return;
        _session.MarkChanged();
        _ = RefreshPreviewAsync();
    }

    // ------------------------------------------------------------------ city

    public ICollectionView? CitiesView => _citiesView;
    public int CityCount => _allCities.Count;

    public bool FollowCity
    {
        get => _followCity;
        set
        {
            if (!Set(ref _followCity, value)) return;
            Raise(nameof(CustomCity));
            if (value)
            {
                _session.Settings.CityOverride = null;
                _selectedCity = null;
                _cityWarning = null;
            }
            else
            {
                var start = _selectedCity
                    ?? _allCities.FirstOrDefault(c => c.Name == (_hub.Current?.Preferences.SelectedCity))
                    ?? _allCities.FirstOrDefault();
                _selectedCity = start;
                _session.Settings.CityOverride = start?.Name;
            }
            Raise(nameof(SelectedCity));
            Raise(nameof(CityWarning));
            Commit();
        }
    }

    public bool CustomCity
    {
        get => !_followCity;
        set { if (value) FollowCity = false; }
    }

    public string OtzariaCityText
    {
        get
        {
            var c = _hub.Current?.Preferences.SelectedCity;
            return string.IsNullOrEmpty(c)
                ? (_hub.Error is null ? "העיר שנבחרה באוצריא: לא ידוע" : "העיר שנבחרה באוצריא: לא זמין")
                : "העיר שנבחרה באוצריא כעת: " + c;
        }
    }

    public string CityFilter
    {
        get => _cityFilter;
        set
        {
            if (!Set(ref _cityFilter, value ?? "")) return;
            _citiesView?.Refresh();
            Raise(nameof(NoCityMatches));
        }
    }

    public bool NoCityMatches => _citiesView is not null && _citiesView.IsEmpty;

    public CityInfo? SelectedCity
    {
        get => _selectedCity;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedCity)) return; // list clears selection when filtering; ignore
            _selectedCity = value;
            _cityWarning = null;
            _session.Settings.CityOverride = value.Name;
            Raise();
            Raise(nameof(CityWarning));
            Commit();
        }
    }

    public string? CityWarning => _cityWarning;

    // ------------------------------------------------------------------ zmanim

    public ObservableCollection<ZmanItemViewModel> Zmanim { get; } = [];

    private ListCollectionView? _zmanimView;
    private string _zmanFilter = "";

    /// <summary>Filtered view over <see cref="Zmanim"/> (the registry has ~100 entries).</summary>
    public ICollectionView ZmanimView
    {
        get
        {
            if (_zmanimView is null)
            {
                _zmanimView = new ListCollectionView(Zmanim)
                {
                    Filter = o => o is ZmanItemViewModel z &&
                        (_zmanFilter.Length == 0 || z.Title.Contains(_zmanFilter, StringComparison.OrdinalIgnoreCase)),
                };
            }
            return _zmanimView;
        }
    }

    public string ZmanFilter
    {
        get => _zmanFilter;
        set
        {
            value = (value ?? "").Trim();
            if (!Set(ref _zmanFilter, value)) return;
            _zmanimView?.Refresh();
        }
    }
    public ICommand SelectAllZmanimCommand { get; }
    public ICommand ClearZmanimCommand { get; }
    public ICommand DefaultZmanimCommand { get; }

    public bool FollowZmanim
    {
        get => _followZmanim;
        set
        {
            if (!Set(ref _followZmanim, value)) return;
            Raise(nameof(CustomZmanim));
            if (value)
            {
                _session.Settings.ZmanimOverride = null;
            }
            else
            {
                // start from what Otzaria currently shows
                var eff = EffectiveZmanimIds(null);
                _syncing = true;
                foreach (var z in Zmanim) z.SetSilently(eff.Contains(z.Id));
                _syncing = false;
                _session.Settings.ZmanimOverride = Zmanim.Where(z => z.IsChecked).Select(z => z.Id).ToList();
            }
            Raise(nameof(ZmanimSummary));
            Commit();
        }
    }

    public bool CustomZmanim
    {
        get => !_followZmanim;
        set { if (value) FollowZmanim = false; }
    }

    public string ZmanimSummary
    {
        get
        {
            if (_followZmanim) return "הזמנים המוצגים הם אלה שנבחרו באוצריא.";
            int n = Zmanim.Count(z => z.IsChecked);
            return n == 0 ? "לא נבחרו זמנים — הווידג'ט לא יציג זמנים." : $"נבחרו {n} מתוך {Zmanim.Count} זמנים.";
        }
    }

    private void OnZmanToggled(ZmanItemViewModel _)
    {
        if (_syncing) return;
        _session.Settings.ZmanimOverride = Zmanim.Where(z => z.IsChecked).Select(z => z.Id).ToList();
        Raise(nameof(ZmanimSummary));
        Commit();
    }

    private void SetAllZmanim(Func<string, bool> pick)
    {
        _syncing = true;
        foreach (var z in Zmanim) z.SetSilently(pick(z.Id));
        _syncing = false;
        _session.Settings.ZmanimOverride = Zmanim.Where(z => z.IsChecked).Select(z => z.Id).ToList();
        Raise(nameof(ZmanimSummary));
        Commit();
    }

    /// <summary>Ids that are in effect: the explicit list, else Otzaria's enabled zmanim, else the defaults.</summary>
    private HashSet<string> EffectiveZmanimIds(List<string>? overrideList)
    {
        if (overrideList is not null) return new HashSet<string>(overrideList);
        var fromOtzaria = _hub.Current?.Preferences.EnabledZmanim;
        return new HashSet<string>(fromOtzaria is { Count: > 0 } ? fromOtzaria : _defaultZmanim);
    }

    // ------------------------------------------------------------------ time format & day transition

    public string[] TimeFormatLabels { get; } = ["כמו באוצריא", "24 שעות (13:45)", "12 שעות (1:45 PM)"];

    public int TimeFormatIndex
    {
        get => _timeFormatIndex;
        set
        {
            if (value < 0 || !Set(ref _timeFormatIndex, value)) return;
            _session.Settings.TwelveHourClockOverride = value switch { 1 => false, 2 => true, _ => null };
            Raise(nameof(TimeFormatHint));
            Commit();
        }
    }

    public string? TimeFormatHint
    {
        get
        {
            if (_timeFormatIndex != 0) return null;
            var p = _hub.Current?.Preferences;
            return p is null ? "הערך באוצריא: לא זמין" : "הערך באוצריא כעת: " + (p.TwelveHourClock ? "12 שעות" : "24 שעות");
        }
    }

    public string[] DayTransitionOptions => DayTransitionLabels;

    public int DayTransitionIndex
    {
        get => _dayTransitionIndex;
        set
        {
            if (value < 0 || !Set(ref _dayTransitionIndex, value)) return;
            _session.Settings.DayTransitionOverride = DayTransitionValues[value];
            Raise(nameof(DayTransitionHint));
            Commit();
        }
    }

    public string? DayTransitionHint
    {
        get
        {
            if (_dayTransitionIndex != 0) return null;
            var p = _hub.Current?.Preferences;
            if (p is null) return "הערך באוצריא: לא זמין";
            var i = Array.IndexOf(DayTransitionValues, p.DayTransition);
            return "הערך באוצריא כעת: " + (i > 0 ? DayTransitionLabels[i] : p.DayTransition);
        }
    }

    public bool ShowYerushalmiDaf
    {
        get => _showYerushalmi;
        set
        {
            if (!Set(ref _showYerushalmi, value)) return;
            _session.Settings.ShowYerushalmiDaf = value;
            Commit();
        }
    }

    // ------------------------------------------------------------------ preview

    public ObservableCollection<PreviewRow> Preview { get; } = [];
    public ICommand RefreshPreviewCommand { get; }

    public string PreviewCity { get => _previewCity; private set => Set(ref _previewCity, value); }
    public string PreviewDate { get => _previewDate; private set => Set(ref _previewDate, value); }
    public string PreviewGregorian { get => _previewGregorian; private set => Set(ref _previewGregorian, value); }
    public string PreviewExtra { get => _previewExtra; private set { if (Set(ref _previewExtra, value)) Raise(nameof(HasPreviewExtra)); } }
    public bool HasPreviewExtra => !string.IsNullOrEmpty(_previewExtra);
    public string? PreviewError { get => _previewError; private set { if (Set(ref _previewError, value)) Raise(nameof(HasPreviewError)); } }
    public bool HasPreviewError => !string.IsNullOrEmpty(_previewError);
    public bool PreviewBusy { get => _previewBusy; private set => Set(ref _previewBusy, value); }

    private sealed record PreviewResult(string City, string Date, string Gregorian, string Extra, List<PreviewRow> Rows);

    public async Task RefreshPreviewAsync()
    {
        int gen = ++_previewGen;
        PreviewBusy = true;

        var s = _session.Settings;
        var prefs = _hub.Current?.Preferences;
        string cityName = s.CityOverride ?? prefs?.SelectedCity ?? "ירושלים";
        bool twelve = s.TwelveHourClockOverride ?? prefs?.TwelveHourClock ?? false;
        string transition = s.DayTransitionOverride ?? prefs?.DayTransition ?? "sunset";
        var ids = s.ZmanimOverride?.ToList()
            ?? (prefs?.EnabledZmanim is { Count: > 0 } e ? e.ToList() : _defaultZmanim.ToList());
        bool yer = s.ShowYerushalmiDaf;
        var calendar = _svc.Calendar;

        PreviewResult? result = null;
        string? error = null;
        try
        {
            result = await Task.Run(() => BuildPreview(calendar, cityName, ids, twelve, transition, yer)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            error = ErrorText.From(ex);
            if (ex is not NotImplementedException) SettingsLog.Error("Preview failed", ex);
        }

        if (gen != _previewGen) return;
        PreviewBusy = false;
        Preview.Clear();
        if (result is null)
        {
            PreviewError = error ?? "לא זמין";
            PreviewCity = PreviewDate = PreviewGregorian = PreviewExtra = "";
            return;
        }
        PreviewError = null;
        PreviewCity = result.City;
        PreviewDate = result.Date;
        PreviewGregorian = result.Gregorian;
        PreviewExtra = result.Extra;
        foreach (var r in result.Rows) Preview.Add(r);
    }

    private static PreviewResult BuildPreview(ICalendarService cal, string cityName, List<string> ids,
        bool twelve, string transition, bool showYerushalmi)
    {
        var city = cal.FindCity(cityName) ?? cal.Cities.FirstOrDefault()
            ?? throw new InvalidOperationException("לא נמצאו ערים");

        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(city.TimeZoneId); }
        catch { tz = TimeZoneInfo.Local; }

        var now = DateTimeOffset.Now;
        var date = cal.GetEffectiveDate(now, city, transition);
        var info = cal.GetDayInfo(date, city);
        var times = cal.GetZmanim(date, city, ids);

        var extraParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(info.Parasha)) extraParts.Add("פרשת " + info.Parasha!.Replace("פרשת ", ""));
        extraParts.AddRange(info.Holidays.Where(h => !string.IsNullOrWhiteSpace(h)));
        if (!string.IsNullOrWhiteSpace(info.DafYomiBavli)) extraParts.Add("דף יומי: " + info.DafYomiBavli);
        if (showYerushalmi && !string.IsNullOrWhiteSpace(info.DafYomiYerushalmi)) extraParts.Add("ירושלמי: " + info.DafYomiYerushalmi);
        if (!string.IsNullOrWhiteSpace(info.Omer)) extraParts.Add(info.Omer!);

        // the "next" zman is only meaningful when the preview shows today
        bool isToday = date == DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, tz).DateTime);
        var rows = new List<PreviewRow>();
        // the next upcoming zman is the earliest future one (registry order is not chronological)
        var nextId = isToday
            ? times.Where(t => t.Time is { } tt && tt > now).OrderBy(t => t.Time).Select(t => t.Id).FirstOrDefault()
            : null;
        foreach (var z in times)
        {
            if (z.Time is null)
            {
                rows.Add(new PreviewRow(z.Title, "—", false, false, true));
                continue;
            }
            var local = TimeZoneInfo.ConvertTime(z.Time.Value, tz);
            string text = FormatTime(local, twelve);
            bool past = isToday && z.Time.Value <= now;
            bool next = nextId is not null && z.Id == nextId;
            rows.Add(new PreviewRow(z.Title, text, past, next, false));
        }

        return new PreviewResult(
            city.Name,
            $"{info.HebrewDayOfWeek} · {info.HebrewDate}".Trim(' ', '·'),
            info.GregorianDate,
            string.Join(" · ", extraParts),
            rows);
    }

    public static string FormatTime(DateTimeOffset t, bool twelve)
        => twelve
            // Otzaria (zmanTimeForDisplay): "h:mm", LRM, NBSP and the English AM/PM marker (no Hebrew marker).
            ? t.ToString("h:mm", CultureInfo.InvariantCulture) + "‎ " + (t.Hour < 12 ? "AM" : "PM")
            : t.ToString("HH:mm", CultureInfo.InvariantCulture);
}
