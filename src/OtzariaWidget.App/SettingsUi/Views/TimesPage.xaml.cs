using System.Windows;
using System.Windows.Controls;

namespace OtzariaWidget.App.SettingsUi.Views;

public partial class TimesPage : UserControl
{
    public TimesPage() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // bring the currently selected city into view when the page opens
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (CityList.SelectedItem is not null) CityList.ScrollIntoView(CityList.SelectedItem);
            }
            catch { /* virtualisation edge cases are cosmetic only */ }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
