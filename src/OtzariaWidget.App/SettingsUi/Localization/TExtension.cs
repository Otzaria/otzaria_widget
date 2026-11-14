using System.Windows.Markup;

namespace OtzariaWidget.App.SettingsUi.Localization;

/// <summary>
/// XAML: <c>Text="{l:T 'מצב'}"</c>. Evaluated when the view loads; a language change rebuilds the window.
/// Escape an apostrophe inside the quotes as <c>\'</c> (e.g. <c>'ווידג\'ט'</c>).
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }

    public TExtension(string text) => Text = text;

    [ConstructorArgument("text")]
    public string Text { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Text);
}
