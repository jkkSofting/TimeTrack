using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Zeitmanagement.MVVM;

namespace Zeitmanagement.ViewModel
{
    /// <summary>Eine Notiz in der Liste links.</summary>
    internal sealed class NoteItemViewModel : BindableBase
    {
        private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

        public NoteItemViewModel(TimeTracker.NoteRow row)
        {
            Id = row.Id;
            Apply(row.Titel, row.Text, row.Projektname, row.UpdatedUtc);
        }

        public int Id { get; }

        public string Title { get; private set; }
        public string Text { get; private set; }
        /// <summary>Das zugeordnete Projekt oder <c>null</c>.</summary>
        public string ProjectName { get; private set; }
        public DateTime UpdatedUtc { get; private set; }

        /// <summary>Der Titel; ohne Titel die erste Zeile des Textes, damit die Liste nicht aus „Ohne Titel“ besteht.</summary>
        public string DisplayTitle
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Title)) return Title.Trim();
                var first = FirstLine(Text);
                return first.Length > 0 ? first : "Ohne Titel";
            }
        }

        /// <summary>Eine Zeile Text unter dem Titel, nur wenn der Titel nicht schon aus dem Text stammt.</summary>
        public string Preview
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Title)) return string.Empty;
                return FirstLine(Text);
            }
        }

        public bool HasPreview => Preview.Length > 0;
        public bool HasProject => !string.IsNullOrEmpty(ProjectName);

        public string UpdatedText
        {
            get
            {
                var local = UpdatedUtc.ToLocalTime();
                return local.Date == DateTime.Today
                    ? "heute " + local.ToString("HH:mm", German)
                    : local.ToString("dd.MM.yyyy", German);
            }
        }

        public void Apply(string title, string text, string projectName, DateTime updatedUtc)
        {
            Title = title ?? string.Empty;
            Text = text ?? string.Empty;
            ProjectName = string.IsNullOrWhiteSpace(projectName) ? null : projectName;
            UpdatedUtc = updatedUtc;

            RaisePropertyChanged(nameof(Title));
            RaisePropertyChanged(nameof(Text));
            RaisePropertyChanged(nameof(ProjectName));
            RaisePropertyChanged(nameof(DisplayTitle));
            RaisePropertyChanged(nameof(Preview));
            RaisePropertyChanged(nameof(HasPreview));
            RaisePropertyChanged(nameof(HasProject));
            RaisePropertyChanged(nameof(UpdatedText));
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var line = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0) ?? string.Empty;
            return line.Length > 120 ? line.Substring(0, 120) : line;
        }
    }

    /// <summary>
    /// Die Seite „Notizen“: Projektnotizen mit Titel, Text und optionalem Projekt. Links die Liste (durchsuchbar,
    /// nach Projekt filterbar), rechts der Editor. Änderungen werden kurz nach dem Tippen von selbst gespeichert.
    /// </summary>
    internal sealed class NotesViewModel : BaseViewModel
    {
        public const string AllProjectsLabel = "Alle Projekte";
        public const string WithoutProjectLabel = "Ohne Projekt";
        public const string NoProjectLabel = "Kein Projekt";

        private static readonly TimeSpan AutoSaveDelay = TimeSpan.FromMilliseconds(600);

        private static TimeTracker Db => MainViewModel.DbInstance;

        private readonly List<NoteItemViewModel> _all = new List<NoteItemViewModel>();
        private readonly DispatcherTimer _saveTimer;

        private bool _loadingEditor;
        private bool _dirty;

        public NotesViewModel()
        {
            _saveTimer = new DispatcherTimer { Interval = AutoSaveDelay };
            _saveTimer.Tick += (s, e) =>
            {
                _saveTimer.Stop();
                Flush();
            };

            NewNoteCommand = new DelegateCommand(_ => NewNote());
            DeleteNoteCommand = new DelegateCommand(_ => DeleteNote(), _ => SelectedNote != null);
            ClearSearchCommand = new DelegateCommand(_ => SearchText = string.Empty);
            RefreshCommand = new DelegateCommand(_ => Refresh());

            FilterOptions.Add(AllProjectsLabel);
            _selectedFilter = AllProjectsLabel;
            ProjectOptions.Add(NoProjectLabel);
            _editProject = NoProjectLabel;
        }

        /// <summary>Soll den Cursor in den Titel des Editors setzen (nach „Neue Notiz“).</summary>
        public event EventHandler FocusEditorRequested;

        public ObservableCollection<NoteItemViewModel> Notes { get; } = new ObservableCollection<NoteItemViewModel>();

        /// <summary>Einträge des Filters über der Liste: alle, ohne Projekt, dann die Projekte.</summary>
        public ObservableCollection<string> FilterOptions { get; } = new ObservableCollection<string>();

        /// <summary>Einträge der Projektwahl im Editor: „Kein Projekt“, dann die Projekte.</summary>
        public ObservableCollection<string> ProjectOptions { get; } = new ObservableCollection<string>();

        public DelegateCommand NewNoteCommand { get; }
        public DelegateCommand DeleteNoteCommand { get; }
        public DelegateCommand ClearSearchCommand { get; }
        public DelegateCommand RefreshCommand { get; }

        public string HeaderSubtitle
        {
            get
            {
                if (_all.Count == 0) return "Noch keine Notizen";
                var shown = Notes.Count;
                return shown == _all.Count
                    ? $"{_all.Count} {(_all.Count == 1 ? "Notiz" : "Notizen")}"
                    : $"{shown} von {_all.Count} Notizen";
            }
        }

        public bool HasNotes => Notes.Count > 0;
        public bool HasNoNotes => Notes.Count == 0;

        /// <summary>Hinweis für die leere Liste: andere Worte, wenn nichts passt statt nichts da ist.</summary>
        public string EmptyListText => _all.Count == 0
            ? "Noch keine Notizen. Lege mit „Neue Notiz“ die erste an."
            : "Keine Notiz passt zur Suche.";

        // ---------- Liste: Suche und Filter ----------

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value ?? string.Empty))
                    ApplyFilter();
            }
        }

        private string _selectedFilter;
        public string SelectedFilter
        {
            get => _selectedFilter;
            set
            {
                // Beim Neuaufbau der Auswahlliste meldet das ComboBox kurz null.
                if (value == null) return;
                if (SetProperty(ref _selectedFilter, value))
                    ApplyFilter();
            }
        }

        private void ApplyFilter()
        {
            // Erst abwählen: Das speichert ausstehende Änderungen und entfernt eine nie beschriebene Notiz, bevor die
            // sichtbare Liste berechnet wird.
            var selectedId = SelectedNote?.Id;
            SelectedNote = null;

            var query = SearchText.Trim();

            IEnumerable<NoteItemViewModel> items = _all;
            if (SelectedFilter == WithoutProjectLabel)
                items = items.Where(n => !n.HasProject);
            else if (SelectedFilter != AllProjectsLabel)
                items = items.Where(n => string.Equals(n.ProjectName, SelectedFilter, StringComparison.Ordinal));

            if (query.Length > 0)
            {
                items = items.Where(n =>
                    Contains(n.Title, query) || Contains(n.Text, query) || Contains(n.ProjectName, query));
            }

            var visible = items.ToList();

            Notes.Clear();
            foreach (var note in visible) Notes.Add(note);

            // Auswahl behalten, wenn die Notiz weiter sichtbar ist; sonst fällt sie aus dem Editor.
            if (selectedId.HasValue)
                SelectedNote = Notes.FirstOrDefault(n => n.Id == selectedId.Value);

            RaisePropertyChanged(nameof(HeaderSubtitle));
            RaisePropertyChanged(nameof(HasNotes));
            RaisePropertyChanged(nameof(HasNoNotes));
            RaisePropertyChanged(nameof(EmptyListText));
        }

        private static bool Contains(string haystack, string needle)
        {
            return !string.IsNullOrEmpty(haystack) && haystack.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        // ---------- Laden ----------

        public override void Refresh()
        {
            Flush();

            try
            {
                // Die Notizen werden neu gelesen (neue Objekte); die zuvor gewählte bleibt über ihre Id gewählt.
                var selectedId = SelectedNote?.Id;
                SelectedNote = null;

                SyncProjectOptions();

                _all.Clear();
                _all.AddRange(Db.GetNotes().Select(r => new NoteItemViewModel(r)));

                ApplyFilter();
                if (selectedId.HasValue)
                    SelectedNote = Notes.FirstOrDefault(n => n.Id == selectedId.Value);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Die Notizen konnten nicht geladen werden", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SyncProjectOptions()
        {
            var names = Db.GetProjects().Select(p => p.Projektname).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();

            var filter = SelectedFilter;
            FilterOptions.Clear();
            FilterOptions.Add(AllProjectsLabel);
            FilterOptions.Add(WithoutProjectLabel);
            foreach (var n in names) FilterOptions.Add(n);
            _selectedFilter = FilterOptions.Contains(filter) ? filter : AllProjectsLabel;
            RaisePropertyChanged(nameof(SelectedFilter));

            var project = EditProject;
            _loadingEditor = true;
            ProjectOptions.Clear();
            ProjectOptions.Add(NoProjectLabel);
            foreach (var n in names) ProjectOptions.Add(n);
            _editProject = ProjectOptions.Contains(project) ? project : NoProjectLabel;
            RaisePropertyChanged(nameof(EditProject));
            _loadingEditor = false;
        }

        // ---------- Auswahl und Editor ----------

        private NoteItemViewModel _selectedNote;
        public NoteItemViewModel SelectedNote
        {
            get => _selectedNote;
            set
            {
                if (ReferenceEquals(_selectedNote, value)) return;

                // Was in der bisherigen Notiz noch ungespeichert ist, gehört zu ihr, nicht zur nächsten.
                Flush();
                DiscardIfEmpty(_selectedNote);

                _selectedNote = value;
                LoadEditor();
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(HasSelection));
                RaisePropertyChanged(nameof(HasNoSelection));
                DeleteNoteCommand.RaiseCanExecuteChanged();
            }
        }

        public bool HasSelection => SelectedNote != null;
        public bool HasNoSelection => SelectedNote == null;

        private string _editTitle = string.Empty;
        public string EditTitle
        {
            get => _editTitle;
            set { if (SetProperty(ref _editTitle, value ?? string.Empty)) MarkDirty(); }
        }

        private string _editText = string.Empty;
        public string EditText
        {
            get => _editText;
            set { if (SetProperty(ref _editText, value ?? string.Empty)) MarkDirty(); }
        }

        private string _editProject;
        public string EditProject
        {
            get => _editProject;
            set
            {
                // Beim Neuaufbau der Auswahlliste meldet das ComboBox kurz null.
                if (value == null) return;
                if (SetProperty(ref _editProject, value)) MarkDirty();
            }
        }

        private string _saveStatus = string.Empty;
        /// <summary>„Gespeichert um 14:03:12“ oder leer.</summary>
        public string SaveStatus
        {
            get => _saveStatus;
            private set => SetProperty(ref _saveStatus, value);
        }

        private void LoadEditor()
        {
            _loadingEditor = true;
            try
            {
                var note = _selectedNote;
                EditTitle = note?.Title ?? string.Empty;
                EditText = note?.Text ?? string.Empty;
                EditProject = note != null && note.HasProject && ProjectOptions.Contains(note.ProjectName)
                    ? note.ProjectName
                    : NoProjectLabel;
                SaveStatus = string.Empty;
            }
            finally
            {
                _loadingEditor = false;
                _dirty = false;
                _saveTimer.Stop();
            }
        }

        private void MarkDirty()
        {
            if (_loadingEditor || _selectedNote == null) return;

            _dirty = true;
            SaveStatus = "Änderungen werden gespeichert …";
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        /// <summary>Speichert ausstehende Änderungen sofort (beim Wechseln der Notiz, der Seite und beim Beenden).</summary>
        public void Flush()
        {
            _saveTimer.Stop();
            if (!_dirty || _selectedNote == null) return;

            var note = _selectedNote;
            var project = EditProject == NoProjectLabel ? null : EditProject;
            try
            {
                Db.UpdateNote(note.Id, EditTitle, EditText, project);
                note.Apply(EditTitle, EditText, project, DateTime.UtcNow);
                _dirty = false;
                SaveStatus = "Gespeichert um " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.GetCultureInfo("de-DE"));
            }
            catch (Exception ex)
            {
                // Die Änderung bleibt im Editor stehen und wird beim nächsten Versuch erneut gespeichert.
                SaveStatus = "Speichern fehlgeschlagen: " + ex.Message;
            }
        }

        // ---------- Neu und Löschen ----------

        private void NewNote()
        {
            Flush();

            try
            {
                // Ist ein Projekt als Filter gewählt, gehört die neue Notiz dazu.
                var project = SelectedFilter != AllProjectsLabel && SelectedFilter != WithoutProjectLabel ? SelectedFilter : null;
                var id = Db.AddNote(string.Empty, string.Empty, project);
                var row = Db.GetNotes().First(n => n.Id == id);
                var item = new NoteItemViewModel(row);

                // Eine neue Notiz muss auch bei Suche und Filter sichtbar sein.
                if (SearchText.Length > 0) { _searchText = string.Empty; RaisePropertyChanged(nameof(SearchText)); }

                _all.Insert(0, item);
                ApplyFilter();
                SelectedNote = item;
                FocusEditorRequested?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Die Notiz konnte nicht angelegt werden", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void DeleteNote()
        {
            var note = SelectedNote;
            if (note == null) return;

            var answer = MessageBox.Show(
                $"Notiz „{note.DisplayTitle}“ endgültig löschen?",
                "Notiz löschen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;

            try
            {
                _dirty = false;
                _saveTimer.Stop();
                Db.DeleteNote(note.Id);

                var index = Notes.IndexOf(note);
                _all.Remove(note);
                ApplyFilter();
                SelectedNote = Notes.Count == 0 ? null : Notes[Math.Min(index, Notes.Count - 1)];
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Die Notiz konnte nicht gelöscht werden", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>Eine angelegte, aber nie beschriebene Notiz wird beim Verlassen wieder entfernt.</summary>
        private void DiscardIfEmpty(NoteItemViewModel note)
        {
            if (note == null) return;
            if (!string.IsNullOrWhiteSpace(note.Title) || !string.IsNullOrWhiteSpace(note.Text)) return;

            try
            {
                Db.DeleteNote(note.Id);
                _all.Remove(note);
                Notes.Remove(note);
                RaisePropertyChanged(nameof(HeaderSubtitle));
                RaisePropertyChanged(nameof(HasNotes));
                RaisePropertyChanged(nameof(HasNoNotes));
                RaisePropertyChanged(nameof(EmptyListText));
            }
            catch (Exception)
            {
                // Eine leere Notiz mehr in der Liste ist harmlos; der nächste Start zeigt sie wieder.
            }
        }
    }
}
