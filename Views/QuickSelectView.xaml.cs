using System.Windows;
using System.Windows.Controls;

namespace Zeitmanagement.Views
{
    /// <summary>
    /// Interaction logic for QuickSelectView.xaml
    /// </summary>
    public partial class QuickSelectView : UserControl
    {
        // Shared across view re-instantiations so at most one floating window exists.
        private static FloatingQuickSelectWindow _floatingWindow;

        public QuickSelectView()
        {
            InitializeComponent();
        }

        private void OpenFloatingWindow_Click(object sender, RoutedEventArgs e)
        {
            if (_floatingWindow != null)
            {
                // Already open - just bring it back to the front.
                if (_floatingWindow.WindowState == WindowState.Minimized)
                    _floatingWindow.WindowState = WindowState.Normal;

                _floatingWindow.Activate();
                return;
            }

            _floatingWindow = new FloatingQuickSelectWindow
            {
                // Share the Quick Select view model so both views stay in sync.
                DataContext = DataContext,
                Owner = Window.GetWindow(this)
            };
            _floatingWindow.Closed += (s, args) => _floatingWindow = null;
            _floatingWindow.Show();
        }
    }
}
