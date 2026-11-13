namespace OtzariaWidget.App.SettingsUi.Infrastructure;

/// <summary>Semantic state shown as a coloured glyph (check / cross / warning / dash).</summary>
public enum StateKind { Neutral, Ok, Warning, Error }

/// <summary>A short message with a state; used as DataContext of the glyph+text message strip.</summary>
public sealed record StatusMessage(string Text, StateKind State);

public static class ErrorText
{
    /// <summary>Short Hebrew text for an exception coming out of a (possibly not yet implemented) service.</summary>
    public static string From(Exception ex) => ex switch
    {
        NotImplementedException => "לא זמין (השירות עדיין לא מומש)",
        AggregateException { InnerException: { } inner } => From(inner),
        _ => "לא זמין: " + ex.Message,
    };
}
