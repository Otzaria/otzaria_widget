// Agent D - inputs/outputs of the pure card builders.
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.Provider.Cards;

/// <summary>Everything a card builder needs. The provider fills it; tests fill it with fakes.</summary>
public sealed class CardContext
{
    public required string Kind { get; init; }
    public CardSize Size { get; init; } = CardSize.Medium;
    public required WidgetAppSettings Settings { get; init; }
    public required ICalendarService Calendar { get; init; }
    public required DateTimeOffset Now { get; init; }
    /// <summary>Null when the data service failed (stub/exception): list widgets then show the "data missing" state.</summary>
    public OtzariaSnapshot? Snapshot { get; init; }
    public string? SnapshotError { get; init; }
    public WidgetCustomState State { get; init; } = new();
    /// <summary>Zero-based page of list widgets.</summary>
    public int Page { get; init; }
    /// <summary>One-shot message shown at the bottom of the card (e.g. "אוצריא לא נמצא").</summary>
    public string? Notice { get; init; }

    public OtzariaPreferences Prefs => Snapshot?.Preferences ?? new OtzariaPreferences();
    public bool LaunchViaProvider => Settings.LinkMode == LinkOpenMode.ProviderLaunch;
    public bool TwelveHour => Settings.TwelveHourClockOverride ?? Prefs.TwelveHourClock;
    public string DayTransition => Settings.DayTransitionOverride ?? Prefs.DayTransition;
}

/// <summary>A built card: which template to use, the data JSON, and when it next needs refreshing without any file change.</summary>
public sealed record CardResult(string TemplateFile, string DataJson, DateTimeOffset? NextRefresh = null, int? EffectivePage = null)
{
    /// <summary>"Data" payload kind for logging only (no personal content).</summary>
    public string Describe() => $"{TemplateFile} data={DataJson.Length}B";
}

/// <summary>One per widget kind. Pure: no WinRT, no file access beyond what the injected services do.</summary>
public interface IWidgetCardBuilder
{
    string Kind { get; }
    /// <summary>The normal card.</summary>
    CardResult Build(CardContext ctx);
    /// <summary>The customization card (Input.* fields) pre-filled with <see cref="CardContext.State"/>.</summary>
    CardResult BuildCustomization(CardContext ctx);
}
