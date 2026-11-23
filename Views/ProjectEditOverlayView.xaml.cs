using System.Windows;
using System.Windows.Controls;

namespace Zeitmanagement.Views
{
    /// <summary>Formularfelder des Overlays „Neues Projekt“ / „Projekt bearbeiten“.</summary>
    public partial class ProjectEditOverlayView : UserControl
    {
        public ProjectEditOverlayView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            NameBox.Focus();
            NameBox.CaretIndex = NameBox.Text?.Length ?? 0;
        }
    }
}
