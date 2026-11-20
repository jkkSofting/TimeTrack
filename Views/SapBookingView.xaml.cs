using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Zeitmanagement.ViewModel;

namespace Zeitmanagement.Views
{
    public partial class SapBookingView : UserControl
    {
        // Gesuchte Position, solange die Seite sie noch wiederherstellt (der Inhalt wird nach dem
        // Erzeugen der View per Refresh gefüllt, die Scrollhöhe wächst also erst nach und nach).
        private double? _pendingOffset;

        public SapBookingView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private SapBookingViewModel Model => DataContext as SapBookingViewModel;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var offset = Model?.ScrollOffset ?? 0;
            if (offset <= 0)
                return;

            _pendingOffset = offset;
            PageScroll.ScrollToVerticalOffset(offset);

            // Nach dem ersten Layout ist der Inhalt da; ab dann zählt wieder nur das Scrollen des Benutzers.
            Dispatcher.BeginInvoke(new Action(() => _pendingOffset = null), DispatcherPriority.ContextIdle);
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_pendingOffset is double target)
            {
                if (PageScroll.VerticalOffset < target && PageScroll.ScrollableHeight > PageScroll.VerticalOffset)
                    PageScroll.ScrollToVerticalOffset(target);
                if (PageScroll.VerticalOffset >= target || PageScroll.ScrollableHeight >= target)
                    _pendingOffset = null;
                return;
            }

            // Nur echtes Scrollen merken, nicht das Zurechtrücken durch geänderte Inhaltshöhe.
            if (e.VerticalChange != 0 && e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0 && Model != null)
                Model.ScrollOffset = e.VerticalOffset;
        }

        // Scrollt der Benutzer selbst, endet die Wiederherstellung.
        private void OnUserScroll(object sender, EventArgs e) => _pendingOffset = null;
    }
}
