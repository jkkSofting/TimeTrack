using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Zeitmanagement.ViewModel;

namespace Zeitmanagement.Views
{
    /// <summary>
    /// Das To-Do-Board. Hier liegt nur, was an der Ansicht hängt: Ziehen und Ablegen der Tickets (Maus) und der
    /// Fokus beim Öffnen der Editoren. Was beim Ablegen passiert, entscheidet <see cref="TodoViewModel"/>.
    /// </summary>
    public partial class TodoView : UserControl
    {
        private const string TicketFormat = "Zeitmanagement.TodoTicket";

        // Ticket unter der gedrückten linken Maustaste; wird zum Ziehen oder – bei einem Klick ohne Ziehen – zum Öffnen.
        private TodoTicketViewModel _pressed;
        private Point _pressPoint;

        // Was gerade als Ziel markiert ist, damit die Markierung nur dort steht.
        private TodoTicketViewModel _markedTicket;
        private TodoColumnViewModel _markedColumn;

        public TodoView()
        {
            InitializeComponent();
        }

        private TodoViewModel ViewModel => DataContext as TodoViewModel;

        // ---------- Ziehen starten / Klicken ----------

        private void Ticket_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _pressed = (sender as FrameworkElement)?.DataContext as TodoTicketViewModel;
            _pressPoint = e.GetPosition(this);
        }

        private void Ticket_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_pressed == null) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _pressed = null;
                return;
            }

            var now = e.GetPosition(this);
            if (Math.Abs(now.X - _pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(now.Y - _pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            var ticket = _pressed;
            _pressed = null; // wer zieht, hat nicht geklickt

            try
            {
                DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(TicketFormat, ticket), DragDropEffects.Move);
            }
            finally
            {
                ClearDropMarks();
            }
        }

        private void Ticket_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var ticket = (sender as FrameworkElement)?.DataContext as TodoTicketViewModel;
            var pressed = _pressed;
            _pressed = null;

            if (ticket != null && ReferenceEquals(ticket, pressed))
                ViewModel?.OpenTicket(ticket);
        }

        // ---------- Ablegen auf einem Ticket: davor oder dahinter ----------

        private void Ticket_DragOver(object sender, DragEventArgs e)
        {
            var target = (sender as FrameworkElement)?.DataContext as TodoTicketViewModel;
            if (target == null || !e.Data.GetDataPresent(TicketFormat)) return;

            var below = IsLowerHalf(sender, e);
            MarkTicket(target, below ? 1 : -1);
            // Nicht als behandelt markieren: Die Spalte darunter hebt sich dadurch ebenfalls hervor.
        }

        private void Ticket_Drop(object sender, DragEventArgs e)
        {
            var target = (sender as FrameworkElement)?.DataContext as TodoTicketViewModel;
            var dragged = e.Data.GetData(TicketFormat) as TodoTicketViewModel;
            var vm = ViewModel;
            if (target == null || dragged == null || vm == null) return;

            e.Handled = true; // sonst legt die Spalte das Ticket zusätzlich ans Ende

            var column = vm.Columns.FirstOrDefault(c => c.Id == target.ColumnId);
            if (column == null) return;

            var index = column.Tickets.IndexOf(target) + (IsLowerHalf(sender, e) ? 1 : 0);
            ClearDropMarks();
            QueueMove(dragged, column, index);
        }

        private static bool IsLowerHalf(object sender, DragEventArgs e)
        {
            var element = (FrameworkElement)sender;
            return e.GetPosition(element).Y > element.ActualHeight / 2;
        }

        // ---------- Ablegen auf einer Spalte: ans Ende ----------

        private void Column_DragOver(object sender, DragEventArgs e)
        {
            var column = (sender as FrameworkElement)?.DataContext as TodoColumnViewModel;
            if (column == null || !e.Data.GetDataPresent(TicketFormat))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            MarkColumn(column);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void Column_DragLeave(object sender, DragEventArgs e)
        {
            // DragLeave kommt auch beim Wechsel zwischen Tickets innerhalb der Spalte; nur beim echten Verlassen aufräumen.
            var element = (FrameworkElement)sender;
            var p = e.GetPosition(element);
            if (p.X >= 0 && p.Y >= 0 && p.X <= element.ActualWidth && p.Y <= element.ActualHeight) return;

            ClearDropMarks();
        }

        private void Column_Drop(object sender, DragEventArgs e)
        {
            var column = (sender as FrameworkElement)?.DataContext as TodoColumnViewModel;
            var dragged = e.Data.GetData(TicketFormat) as TodoTicketViewModel;
            if (column == null || dragged == null) return;

            e.Handled = true;
            ClearDropMarks();
            QueueMove(dragged, column, column.Tickets.Count);
        }

        // Das Verschieben baut die Ansicht um; das erst nach dem Ende der Ziehschleife.
        private void QueueMove(TodoTicketViewModel ticket, TodoColumnViewModel column, int index)
        {
            Dispatcher.BeginInvoke(new Action(() => ViewModel?.MoveTicket(ticket, column, index)), DispatcherPriority.Background);
        }

        // ---------- Markierung des Ziels ----------

        private void MarkTicket(TodoTicketViewModel ticket, int marker)
        {
            if (!ReferenceEquals(_markedTicket, ticket) && _markedTicket != null)
                _markedTicket.DropMarker = 0;

            _markedTicket = ticket;
            ticket.DropMarker = marker;
        }

        private void MarkColumn(TodoColumnViewModel column)
        {
            if (!ReferenceEquals(_markedColumn, column) && _markedColumn != null)
            {
                _markedColumn.IsDropTarget = false;
                // Die Einfügemarke eines Tickets der verlassenen Spalte gilt nicht mehr.
                if (_markedTicket != null && _markedTicket.ColumnId != column.Id)
                {
                    _markedTicket.DropMarker = 0;
                    _markedTicket = null;
                }
            }

            _markedColumn = column;
            column.IsDropTarget = true;
        }

        private void ClearDropMarks()
        {
            if (_markedTicket != null) _markedTicket.DropMarker = 0;
            if (_markedColumn != null) _markedColumn.IsDropTarget = false;
            _markedTicket = null;
            _markedColumn = null;
        }

        // ---------- Fokus beim Öffnen der Editoren ----------

        private void TicketEditor_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            FocusWhenShown(e, TicketTitleBox);
        }

        private void ColumnEditor_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            FocusWhenShown(e, ColumnNameBox);
        }

        private void FocusWhenShown(DependencyPropertyChangedEventArgs e, TextBox box)
        {
            if (!(bool)e.NewValue) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                box.Focus();
                box.SelectAll();
            }), DispatcherPriority.Input);
        }
    }
}
