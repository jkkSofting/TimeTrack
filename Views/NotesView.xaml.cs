using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Zeitmanagement.ViewModel;

namespace Zeitmanagement.Views
{
    /// <summary>Die Notizen-Seite. Hier liegt nur der Fokus nach „Neue Notiz“, alles andere steckt im ViewModel.</summary>
    public partial class NotesView : UserControl
    {
        private NotesViewModel _subscribedTo;

        public NotesView()
        {
            InitializeComponent();
            Loaded += (s, e) => Subscribe();
            Unloaded += (s, e) => Unsubscribe();
        }

        // Die Seite wird bei jedem Besuch neu aufgebaut, das ViewModel bleibt: Abonnement nur, solange die Ansicht lebt.
        private void Subscribe()
        {
            Unsubscribe();
            _subscribedTo = DataContext as NotesViewModel;
            if (_subscribedTo != null)
                _subscribedTo.FocusEditorRequested += OnFocusEditorRequested;
        }

        private void Unsubscribe()
        {
            if (_subscribedTo != null)
                _subscribedTo.FocusEditorRequested -= OnFocusEditorRequested;
            _subscribedTo = null;
        }

        private void OnFocusEditorRequested(object sender, EventArgs e)
        {
            // Der Editor wird mit der Auswahl erst sichtbar; danach erst lässt sich der Titel fokussieren.
            Dispatcher.BeginInvoke(new Action(() => TitleBox.Focus()), DispatcherPriority.Input);
        }
    }
}
