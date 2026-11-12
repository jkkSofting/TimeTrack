using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Zeitmanagement.MVVM;

namespace Zeitmanagement.ViewModel
{
    /// <summary>Ein Ticket auf dem Board: nur Titel und Beschreibung.</summary>
    internal sealed class TodoTicketViewModel : BindableBase
    {
        public TodoTicketViewModel(TimeTracker.TodoTicketRow row)
        {
            Id = row.Id;
            Update(row);
        }

        public int Id { get; }

        private int _columnId;
        public int ColumnId
        {
            get => _columnId;
            private set => SetProperty(ref _columnId, value);
        }

        private string _title;
        public string Title
        {
            get => _title;
            private set => SetProperty(ref _title, value);
        }

        private string _description;
        public string Description
        {
            get => _description;
            private set
            {
                if (SetProperty(ref _description, value))
                    RaisePropertyChanged(nameof(HasDescription));
            }
        }

        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

        // Einfügemarke beim Ziehen: -1 = über diesem Ticket, 1 = darunter, 0 = keine.
        private int _dropMarker;
        public int DropMarker
        {
            get => _dropMarker;
            set
            {
                if (!SetProperty(ref _dropMarker, value)) return;
                RaisePropertyChanged(nameof(IsDropAbove));
                RaisePropertyChanged(nameof(IsDropBelow));
            }
        }

        public bool IsDropAbove => _dropMarker < 0;
        public bool IsDropBelow => _dropMarker > 0;

        public void Update(TimeTracker.TodoTicketRow row)
        {
            ColumnId = row.ColumnId;
            Title = row.Titel;
            Description = row.Beschreibung;
        }
    }

    /// <summary>Eine Spalte des Boards mit ihren Tickets.</summary>
    internal sealed class TodoColumnViewModel : BindableBase
    {
        public TodoColumnViewModel(TimeTracker.TodoColumnRow row, DelegateCommand addTicketCommand, DelegateCommand editCommand, DelegateCommand deleteCommand)
        {
            Id = row.Id;
            Name = row.Name;
            AddTicketCommand = addTicketCommand;
            EditCommand = editCommand;
            DeleteCommand = deleteCommand;
            Tickets.CollectionChanged += (s, e) => RaisePropertyChanged(nameof(TicketCountText));
        }

        public int Id { get; }

        private string _name;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public ObservableCollection<TodoTicketViewModel> Tickets { get; } = new ObservableCollection<TodoTicketViewModel>();

        public string TicketCountText => Tickets.Count.ToString();

        // Spalte, über der gerade ein Ticket gezogen wird.
        private bool _isDropTarget;
        public bool IsDropTarget
        {
            get => _isDropTarget;
            set => SetProperty(ref _isDropTarget, value);
        }

        /// <summary>Neues Ticket in dieser Spalte (Parameter ist die Spalte).</summary>
        public DelegateCommand AddTicketCommand { get; }

        /// <summary>Spalte umbenennen, verschieben oder löschen (Parameter ist die Spalte).</summary>
        public DelegateCommand EditCommand { get; }

        /// <summary>Spalte nach Rückfrage löschen (Parameter ist die Spalte).</summary>
        public DelegateCommand DeleteCommand { get; }

        // Auswahllisten (ComboBox) zeigen ein Element über ToString; ohne das stünde dort der Typname.
        public override string ToString() => Name;
    }

    /// <summary>
    /// Die Seite „To-Do“: je Projekt ein Board (dazu ein allgemeines „Ohne Projekt“) mit frei benennbaren Spalten
    /// (anfangs To Do, In Arbeit, Erledigt), in denen Tickets liegen. Ein Ticket hat nur Titel und Beschreibung und
    /// wird per Ziehen oder über die Spaltenwahl im Ticket verschoben.
    /// </summary>
    internal sealed class TodoViewModel : BaseViewModel
    {
        private static TimeTracker Db => MainViewModel.DbInstance;

        /// <summary>Eintrag der Projektwahl für das allgemeine Board.</summary>
        public const string GeneralBoardLabel = "Ohne Projekt";

        public TodoViewModel()
        {
            _addTicketCommand = new DelegateCommand(c => OpenNewTicket((TodoColumnViewModel)c));
            _editColumnCommand = new DelegateCommand(c => OpenColumn((TodoColumnViewModel)c));
            _deleteColumnCommand = new DelegateCommand(c => DeleteColumn((TodoColumnViewModel)c));
            ProjectOptions.Add(GeneralBoardLabel);
            _selectedProject = GeneralBoardLabel;

            AddColumnCommand = new DelegateCommand(_ => OpenNewColumn());
            RefreshCommand = new DelegateCommand(_ => Refresh());

            SaveTicketCommand = new DelegateCommand(_ => SaveTicket(), _ => IsTicketEditorOpen && !string.IsNullOrWhiteSpace(TicketTitle) && SelectedTicketColumn != null);
            CancelTicketCommand = new DelegateCommand(_ => IsTicketEditorOpen = false);
            DeleteTicketCommand = new DelegateCommand(_ => DeleteTicket(), _ => IsEditingTicket);

            SaveColumnCommand = new DelegateCommand(_ => SaveColumn(), _ => IsColumnEditorOpen && !string.IsNullOrWhiteSpace(ColumnName));
            CancelColumnCommand = new DelegateCommand(_ => IsColumnEditorOpen = false);
            DeleteColumnCommand = new DelegateCommand(_ => DeleteColumn(Columns.FirstOrDefault(c => c.Id == _editingColumnId)), _ => IsEditingColumn);
            MoveColumnLeftCommand = new DelegateCommand(_ => MoveColumn(-1), _ => CanMoveColumn(-1));
            MoveColumnRightCommand = new DelegateCommand(_ => MoveColumn(1), _ => CanMoveColumn(1));

            Columns.CollectionChanged += (s, e) =>
            {
                RaisePropertyChanged(nameof(HasColumns));
                RaisePropertyChanged(nameof(HeaderSubtitle));
            };
        }

        private readonly DelegateCommand _addTicketCommand;
        private readonly DelegateCommand _editColumnCommand;
        private readonly DelegateCommand _deleteColumnCommand;

        public ObservableCollection<TodoColumnViewModel> Columns { get; } = new ObservableCollection<TodoColumnViewModel>();

        public bool HasColumns => Columns.Count > 0;

        public string HeaderSubtitle
        {
            get
            {
                var tickets = Columns.Sum(c => c.Tickets.Count);
                return $"{tickets} {(tickets == 1 ? "Ticket" : "Tickets")} in {Columns.Count} {(Columns.Count == 1 ? "Spalte" : "Spalten")}";
            }
        }

        public DelegateCommand AddColumnCommand { get; }
        public DelegateCommand RefreshCommand { get; }

        // ---------- Board (Projekt) wählen ----------

        /// <summary>Einträge der Projektwahl: „Ohne Projekt“, dann die Projekte. Jedes hat sein eigenes Board.</summary>
        public ObservableCollection<string> ProjectOptions { get; } = new ObservableCollection<string>();

        private string _selectedProject;
        public string SelectedProject
        {
            get => _selectedProject;
            set
            {
                // Beim Neuaufbau der Auswahlliste meldet das ComboBox kurz null.
                if (value == null) return;
                if (!SetProperty(ref _selectedProject, value)) return;

                IsTicketEditorOpen = false;
                IsColumnEditorOpen = false;
                LoadBoard(); // die Projektliste bleibt unberührt, solange die Auswahl noch läuft
            }
        }

        /// <summary>Das Projekt des angezeigten Boards, <c>null</c> für das allgemeine Board.</summary>
        private string CurrentProject => SelectedProject == GeneralBoardLabel ? null : SelectedProject;

        private void SyncProjectOptions()
        {
            var wanted = new List<string> { GeneralBoardLabel };
            wanted.AddRange(Db.GetProjects().Select(p => p.Projektname).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase));

            // Die Liste wird angeglichen, nicht neu aufgebaut: Das ComboBox behält so seine Auswahl.
            ListSync.Sync(ProjectOptions, wanted);

            // Wurde das gewählte Projekt gelöscht, zeigt das Board wieder „Ohne Projekt“.
            if (!ProjectOptions.Contains(SelectedProject))
            {
                _selectedProject = GeneralBoardLabel;
                RaisePropertyChanged(nameof(SelectedProject));
            }
        }

        // ---------- Laden ----------

        public override void Refresh()
        {
            try
            {
                SyncProjectOptions();
            }
            catch (Exception ex)
            {
                ShowError("Die Projekte konnten nicht geladen werden", ex);
            }

            LoadBoard();
        }

        private void LoadBoard()
        {
            try
            {
                // Das Board richtet sich beim ersten Öffnen mit den Standardspalten ein.
                Db.EnsureTodoBoard(CurrentProject);
                var columnRows = Db.GetTodoColumns(CurrentProject);
                var ticketRows = Db.GetTodoTickets(CurrentProject).ToLookup(t => t.ColumnId);

                SyncColumns(columnRows);
                foreach (var column in Columns)
                    SyncTickets(column, ticketRows[column.Id].ToList());

                RaisePropertyChanged(nameof(HeaderSubtitle));
            }
            catch (Exception ex)
            {
                ShowError("Das Board konnte nicht geladen werden", ex);
            }
        }

        // Abgleich statt Neuaufbau: Die Ansicht behält so ihre Bildlaufposition und die Auswahl der Spaltenwahl.
        private void SyncColumns(IList<TimeTracker.TodoColumnRow> rows)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var at = -1;
                for (var j = i; j < Columns.Count; j++)
                {
                    if (Columns[j].Id == row.Id) { at = j; break; }
                }

                if (at < 0)
                {
                    Columns.Insert(i, new TodoColumnViewModel(row, _addTicketCommand, _editColumnCommand, _deleteColumnCommand));
                    continue;
                }

                if (at != i) Columns.Move(at, i);
                Columns[i].Name = row.Name;
            }

            while (Columns.Count > rows.Count)
                Columns.RemoveAt(Columns.Count - 1);
        }

        private static void SyncTickets(TodoColumnViewModel column, IList<TimeTracker.TodoTicketRow> rows)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var at = -1;
                for (var j = i; j < column.Tickets.Count; j++)
                {
                    if (column.Tickets[j].Id == row.Id) { at = j; break; }
                }

                if (at < 0)
                {
                    column.Tickets.Insert(i, new TodoTicketViewModel(row));
                    continue;
                }

                if (at != i) column.Tickets.Move(at, i);
                column.Tickets[i].Update(row);
            }

            while (column.Tickets.Count > rows.Count)
                column.Tickets.RemoveAt(column.Tickets.Count - 1);
        }

        // ---------- Ticket bearbeiten ----------

        private int? _editingTicketId;

        private bool _isTicketEditorOpen;
        public bool IsTicketEditorOpen
        {
            get => _isTicketEditorOpen;
            set
            {
                if (!SetProperty(ref _isTicketEditorOpen, value)) return;
                if (value) IsColumnEditorOpen = false;
                SaveTicketCommand.RaiseCanExecuteChanged();
            }
        }

        public bool IsEditingTicket => _editingTicketId.HasValue;

        private string _ticketEditorTitle;
        public string TicketEditorTitle
        {
            get => _ticketEditorTitle;
            private set => SetProperty(ref _ticketEditorTitle, value);
        }

        private string _ticketTitle;
        public string TicketTitle
        {
            get => _ticketTitle;
            set
            {
                if (SetProperty(ref _ticketTitle, value))
                    SaveTicketCommand.RaiseCanExecuteChanged();
            }
        }

        private string _ticketDescription;
        public string TicketDescription
        {
            get => _ticketDescription;
            set => SetProperty(ref _ticketDescription, value);
        }

        private TodoColumnViewModel _selectedTicketColumn;
        /// <summary>Die Spalte, in der das Ticket liegt bzw. angelegt wird; eine andere Wahl verschiebt es beim Speichern.</summary>
        public TodoColumnViewModel SelectedTicketColumn
        {
            get => _selectedTicketColumn;
            set
            {
                if (SetProperty(ref _selectedTicketColumn, value))
                    SaveTicketCommand.RaiseCanExecuteChanged();
            }
        }

        public DelegateCommand SaveTicketCommand { get; }
        public DelegateCommand CancelTicketCommand { get; }
        public DelegateCommand DeleteTicketCommand { get; }

        public void OpenNewTicket(TodoColumnViewModel column)
        {
            _editingTicketId = null;
            RaisePropertyChanged(nameof(IsEditingTicket));
            TicketEditorTitle = "Neues Ticket";
            TicketTitle = string.Empty;
            TicketDescription = string.Empty;
            SelectedTicketColumn = column;
            DeleteTicketCommand.RaiseCanExecuteChanged();
            IsTicketEditorOpen = true;
        }

        public void OpenTicket(TodoTicketViewModel ticket)
        {
            _editingTicketId = ticket.Id;
            RaisePropertyChanged(nameof(IsEditingTicket));
            TicketEditorTitle = "Ticket bearbeiten";
            TicketTitle = ticket.Title;
            TicketDescription = ticket.Description;
            SelectedTicketColumn = Columns.FirstOrDefault(c => c.Id == ticket.ColumnId);
            DeleteTicketCommand.RaiseCanExecuteChanged();
            IsTicketEditorOpen = true;
        }

        private void SaveTicket()
        {
            try
            {
                var column = SelectedTicketColumn;
                if (_editingTicketId.HasValue)
                {
                    var id = _editingTicketId.Value;
                    var before = Columns.SelectMany(c => c.Tickets).FirstOrDefault(t => t.Id == id);

                    Db.UpdateTodoTicket(id, TicketTitle, TicketDescription);
                    // Eine andere Spalte heißt: ans Ende dieser Spalte legen.
                    if (before != null && before.ColumnId != column.Id)
                        Db.MoveTodoTicket(id, column.Id, int.MaxValue);
                }
                else
                {
                    Db.AddTodoTicket(column.Id, TicketTitle, TicketDescription);
                }

                IsTicketEditorOpen = false;
                LoadBoard();
            }
            catch (Exception ex)
            {
                ShowError("Das Ticket konnte nicht gespeichert werden", ex);
            }
        }

        private void DeleteTicket()
        {
            if (!_editingTicketId.HasValue) return;

            var answer = MessageBox.Show(
                $"Ticket „{TicketTitle}“ endgültig löschen?",
                "Ticket löschen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;

            try
            {
                Db.DeleteTodoTicket(_editingTicketId.Value);
                IsTicketEditorOpen = false;
                LoadBoard();
            }
            catch (Exception ex)
            {
                ShowError("Das Ticket konnte nicht gelöscht werden", ex);
            }
        }

        /// <summary>
        /// Verschiebt ein Ticket per Ziehen. <paramref name="insertBeforeIndex"/> ist die Stelle in der Zielspalte,
        /// wie sie vor dem Verschieben aussieht (Anzahl der Tickets = ans Ende).
        /// </summary>
        public void MoveTicket(TodoTicketViewModel ticket, TodoColumnViewModel target, int insertBeforeIndex)
        {
            var source = Columns.FirstOrDefault(c => c.Id == ticket.ColumnId);
            if (source == null || target == null) return;

            var index = insertBeforeIndex;
            if (source == target)
            {
                // Das Ticket zählt in der Zielspalte nicht mit: liegt es vor der Einfügestelle, rückt sie eins nach vorn.
                var from = source.Tickets.IndexOf(ticket);
                if (from < index) index--;
                if (from == index) return;
            }

            try
            {
                Db.MoveTodoTicket(ticket.Id, target.Id, index);
            }
            catch (Exception ex)
            {
                ShowError("Das Ticket konnte nicht verschoben werden", ex);
            }

            LoadBoard();
        }

        // ---------- Spalte bearbeiten ----------

        private int? _editingColumnId;

        private bool _isColumnEditorOpen;
        public bool IsColumnEditorOpen
        {
            get => _isColumnEditorOpen;
            set
            {
                if (!SetProperty(ref _isColumnEditorOpen, value)) return;
                if (value) IsTicketEditorOpen = false;
                SaveColumnCommand.RaiseCanExecuteChanged();
            }
        }

        public bool IsEditingColumn => _editingColumnId.HasValue;

        private string _columnEditorTitle;
        public string ColumnEditorTitle
        {
            get => _columnEditorTitle;
            private set => SetProperty(ref _columnEditorTitle, value);
        }

        private string _columnName;
        public string ColumnName
        {
            get => _columnName;
            set
            {
                if (SetProperty(ref _columnName, value))
                    SaveColumnCommand.RaiseCanExecuteChanged();
            }
        }

        public DelegateCommand SaveColumnCommand { get; }
        public DelegateCommand CancelColumnCommand { get; }
        public DelegateCommand DeleteColumnCommand { get; }
        public DelegateCommand MoveColumnLeftCommand { get; }
        public DelegateCommand MoveColumnRightCommand { get; }

        private void OpenNewColumn()
        {
            _editingColumnId = null;
            RaisePropertyChanged(nameof(IsEditingColumn));
            ColumnEditorTitle = "Neue Spalte";
            ColumnName = string.Empty;
            RaiseColumnCommands();
            IsColumnEditorOpen = true;
        }

        private void OpenColumn(TodoColumnViewModel column)
        {
            _editingColumnId = column.Id;
            RaisePropertyChanged(nameof(IsEditingColumn));
            ColumnEditorTitle = "Spalte bearbeiten";
            ColumnName = column.Name;
            RaiseColumnCommands();
            IsColumnEditorOpen = true;
        }

        private void RaiseColumnCommands()
        {
            DeleteColumnCommand.RaiseCanExecuteChanged();
            MoveColumnLeftCommand.RaiseCanExecuteChanged();
            MoveColumnRightCommand.RaiseCanExecuteChanged();
        }

        private void SaveColumn()
        {
            try
            {
                if (_editingColumnId.HasValue)
                    Db.RenameTodoColumn(_editingColumnId.Value, ColumnName);
                else
                    Db.AddTodoColumn(CurrentProject, ColumnName);

                IsColumnEditorOpen = false;
                LoadBoard();
            }
            catch (Exception ex)
            {
                ShowError("Die Spalte konnte nicht gespeichert werden", ex);
            }
        }

        private void DeleteColumn(TodoColumnViewModel column)
        {
            if (column == null) return;

            var count = column.Tickets.Count;
            var question = count == 0
                ? $"Spalte „{column.Name}“ löschen?"
                : $"Spalte „{column.Name}“ löschen?\n\nSie enthält {count} {(count == 1 ? "Ticket" : "Tickets")}, " +
                  (count == 1 ? "das" : "die") + " dabei endgültig gelöscht " + (count == 1 ? "wird" : "werden") + ".";

            var answer = MessageBox.Show(question, "Spalte löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;

            try
            {
                Db.DeleteTodoColumn(column.Id, withTickets: true);

                if (_editingColumnId == column.Id) IsColumnEditorOpen = false;
                LoadBoard();
            }
            catch (Exception ex)
            {
                ShowError("Die Spalte konnte nicht gelöscht werden", ex);
            }
        }

        private int EditingColumnIndex()
        {
            if (!_editingColumnId.HasValue) return -1;
            for (var i = 0; i < Columns.Count; i++)
            {
                if (Columns[i].Id == _editingColumnId.Value) return i;
            }
            return -1;
        }

        private bool CanMoveColumn(int direction)
        {
            var index = EditingColumnIndex();
            return index >= 0 && index + direction >= 0 && index + direction < Columns.Count;
        }

        private void MoveColumn(int direction)
        {
            var index = EditingColumnIndex();
            if (index < 0) return;

            try
            {
                Db.MoveTodoColumn(_editingColumnId.Value, index + direction);
                LoadBoard();
                RaiseColumnCommands();
            }
            catch (Exception ex)
            {
                ShowError("Die Spalte konnte nicht verschoben werden", ex);
            }
        }

        private static void ShowError(string title, Exception ex)
        {
            MessageBox.Show(ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
