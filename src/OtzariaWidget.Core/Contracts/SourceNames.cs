// SHARED CONTRACTS — owned by the project lead.
namespace OtzariaWidget.Core.Contracts;

/// <summary>Canonical <see cref="SourceStatus.Name"/> values produced by IOtzariaDataService. Match on these exactly.</summary>
public static class SourceNames
{
    public const string UserState = "user_state.db";
    public const string Preferences = "app_preferences.hive";
    public const string Library = "seforim.db";
    public const string Progress = "progress";
    public const string OtzariaExe = "otzaria.exe";
}
