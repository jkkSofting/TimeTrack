using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Views
{
    /// <summary>
    /// A small, always-on-top window that lets the user start, pause and stop their project
    /// bookings via a combo box without opening the main window.
    /// </summary>
    public partial class FloatingQuickSelectWindow : Window
    {
        public static readonly DependencyProperty IsPresentingInTeamsProperty = DependencyProperty.Register(
            nameof(IsPresentingInTeams), typeof(bool), typeof(FloatingQuickSelectWindow), new PropertyMetadata(false));

        // Shared so at most one floating window exists.
        private static FloatingQuickSelectWindow _instance;

        private readonly DispatcherTimer _presentationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };

        /// <summary>
        /// Opens the floating Quick Switch window, or brings the existing one to the front if
        /// it's already open. Shared between the sidebar entry, the tray menu and app-startup
        /// handling so at most one instance ever exists.
        /// </summary>
        public static void ShowOrActivate(object dataContext, Window owner)
        {
            if (_instance != null)
            {
                // Already open - just bring it back to the front.
                if (_instance.WindowState == WindowState.Minimized)
                    _instance.WindowState = WindowState.Normal;

                _instance.Activate();
                return;
            }

            _instance = new FloatingQuickSelectWindow
            {
                // Share the Quick Select view model so the window shows the running bookings.
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
                _instance.Loaded += (s, args) =>
                {
                    _instance.Left = owner.Left + (owner.ActualWidth - _instance.ActualWidth) / 2;
                    _instance.Top = owner.Top + (owner.ActualHeight - _instance.ActualHeight) / 2;
                };
            }
            else
            {
                _instance.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            _instance.Closed += (s, args) => _instance = null;
            _instance.Show();
        }

        /// <summary>True while Teams is sharing the screen; the window then shows a red frame.</summary>
        public bool IsPresentingInTeams
        {
            get => (bool)GetValue(IsPresentingInTeamsProperty);
            private set => SetValue(IsPresentingInTeamsProperty, value);
        }

        public FloatingQuickSelectWindow()
        {
            InitializeComponent();
            Topmost = Properties.Settings.Default.FloatingWindowAlwaysOnTop;

            _presentationTimer.Tick += (s, e) => UpdatePresentationState();
            Loaded += (s, e) =>
            {
                UpdatePresentationState();
                _presentationTimer.Start();
            };
            Closed += (s, e) => _presentationTimer.Stop();
        }

        private void UpdatePresentationState()
        {
            try
            {
                IsPresentingInTeams = PresentationDetector.IsPresentingInTeams();
            }
            catch (Exception ex)
            {
                // Purely decorative indicator - keep the last known state rather than letting a
                // registry hiccup take down the time tracker.
                Debug.WriteLine($"Teams presentation check failed: {ex}");
            }
        }
    }
}
