using System.Windows;
using System.Windows.Controls;

namespace Zeitmanagement.Views
{
    /// <summary>
    /// Interaction logic for SettingsView.xaml
    /// </summary>
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Switching the penguin off needs a second thought: if the user doesn't confirm, the
        /// check box goes back to checked (which also pushes the value back to the view model).
        /// Click only fires for real user clicks, not when the settings are refreshed.
        /// </summary>
        private void PenguinCheckBox_Click(object sender, RoutedEventArgs e)
        {
            var checkBox = (CheckBox)sender;
            if (checkBox.IsChecked == true)
                return;

            var result = MessageBox.Show(
                "Wenn du den Pinguin deaktivierst, wäre er seeehr traurig.\n\nMöchtest du ihn wirklich deaktivieren?",
                "Pinguin deaktivieren",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

            if (result != MessageBoxResult.Yes)
                checkBox.IsChecked = true;
        }
    }
}
