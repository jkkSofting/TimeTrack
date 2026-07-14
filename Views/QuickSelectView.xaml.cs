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
            ShowFloatingWindow(DataContext, Window.GetWindow(this));
        }

        /// <summary>
        /// Opens the floating Quick Switch window, or brings the existing one to the front if
        /// it's already open. Shared between the manual button click and app-startup handling
        /// so at most one instance ever exists.
        /// </summary>
        public static void ShowFloatingWindow(object dataContext, Window owner)
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
                DataContext = dataContext
            };

            // Deliberately not setting Owner: WPF minimizes/hides owned windows whenever their
            // owner is minimized, which would take the floating window down with the main
            // window. Instead, position it manually so it still starts out centered on the
            // main window while remaining fully independent. If the owner is minimized (e.g.
            // StartupMode.MinimizedWithFloating), its Left/Top/ActualWidth/ActualHeight are not
            // meaningful, so fall back to centering on screen.
            if (owner != null && owner.WindowState != WindowState.Minimized)
            {
                _floatingWindow.Loaded += (s, args) =>
                {
                    _floatingWindow.Left = owner.Left + (owner.ActualWidth - _floatingWindow.ActualWidth) / 2;
                    _floatingWindow.Top = owner.Top + (owner.ActualHeight - _floatingWindow.ActualHeight) / 2;
                };
            }
            else
            {
                _floatingWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            _floatingWindow.Closed += (s, args) => _floatingWindow = null;
            _floatingWindow.Show();
        }
    }
}
