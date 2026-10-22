using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Views
{
    /// <summary>
    /// A small, always-on-top companion window that mirrors the Quick Select state and lets
    /// the user switch the active project via a combo box without opening the main window.
    /// </summary>
    public partial class FloatingQuickSelectWindow : Window
    {
        public static readonly DependencyProperty IsPresentingInTeamsProperty = DependencyProperty.Register(
            nameof(IsPresentingInTeams), typeof(bool), typeof(FloatingQuickSelectWindow), new PropertyMetadata(false));

        private readonly DispatcherTimer _presentationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };

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
