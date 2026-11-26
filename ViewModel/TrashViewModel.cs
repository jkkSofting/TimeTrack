using System;
using System.Collections.ObjectModel;
using System.Globalization;
using Zeitmanagement.Helpers;
using Zeitmanagement.MVVM;

namespace Zeitmanagement.ViewModel
{
    /// <summary>Papierkorb: gelöschte Projekte und einzeln gelöschte Buchungen wiederherstellen oder endgültig löschen.</summary>
    internal sealed class TrashViewModel : BaseViewModel
    {
        internal const string DeletedSuffix = " (gelöscht)";

        private readonly TimeTracker _db;
        private readonly IConfirmService _confirm;
        private readonly Action _trashChanged;

        private bool _showProjects = true;
        private bool _showEntries;
        private string _notice;

        /// <param name="db">Die Datenbank (explizit übergeben, damit testbar).</param>
        /// <param name="confirm">Rückfrage vor endgültigem Löschen.</param>
        /// <param name="trashChanged">Wird aufgerufen, wenn sich die Quick-Switch-Projektliste ändert (Wiederherstellen).</param>
        public TrashViewModel(TimeTracker db, IConfirmService confirm, Action trashChanged = null)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _confirm = confirm ?? throw new ArgumentNullException(nameof(confirm));
            _trashChanged = trashChanged;

            RestoreProjectCommand = new DelegateCommand(p => RestoreProject(p as TrashedProjectItem), p => p is TrashedProjectItem);
            PurgeProjectCommand = new DelegateCommand(p => PurgeProject(p as TrashedProjectItem), p => p is TrashedProjectItem);
            RestoreEntryCommand = new DelegateCommand(e => RestoreEntry(e as TrashedEntryItem), e => e is TrashedEntryItem);
            PurgeEntryCommand = new DelegateCommand(e => PurgeEntry(e as TrashedEntryItem), e => e is TrashedEntryItem);
            EmptyTrashCommand = new DelegateCommand(_ => EmptyTrash(), _ => HasItems);
            DismissNoticeCommand = new DelegateCommand(_ => Notice = null);
        }

        public ObservableCollection<TrashedProjectItem> Projects { get; } = new ObservableCollection<TrashedProjectItem>();
        public ObservableCollection<TrashedEntryItem> Entries { get; } = new ObservableCollection<TrashedEntryItem>();

        public DelegateCommand RestoreProjectCommand { get; }
        public DelegateCommand PurgeProjectCommand { get; }
        public DelegateCommand RestoreEntryCommand { get; }
        public DelegateCommand PurgeEntryCommand { get; }
        public DelegateCommand EmptyTrashCommand { get; }
        public DelegateCommand DismissNoticeCommand { get; }

        /// <summary>Tab „Projekte“ aktiv. Gegenstück zu <see cref="ShowEntries"/>.</summary>
        public bool ShowProjects
        {
            get => _showProjects;
            set
            {
                if (value == _showProjects) return;
                _showProjects = value;
                _showEntries = !value;
                RaisePropertyChanged(nameof(ShowProjects));
                RaisePropertyChanged(nameof(ShowEntries));
            }
        }

        /// <summary>Tab „Buchungen“ aktiv. Gegenstück zu <see cref="ShowProjects"/>.</summary>
        public bool ShowEntries
        {
            get => _showEntries;
            set
            {
                if (value == _showEntries) return;
                _showEntries = value;
                _showProjects = !value;
                RaisePropertyChanged(nameof(ShowProjects));
                RaisePropertyChanged(nameof(ShowEntries));
            }
        }

        public int ProjectCount => Projects.Count;
        public int EntryCount => Entries.Count;
        public bool IsEmpty => Projects.Count == 0 && Entries.Count == 0;
        public bool HasItems => !IsEmpty;
        public bool IsProjectsEmpty => Projects.Count == 0;
        public bool IsEntriesEmpty => Entries.Count == 0;
        public string ProjectsTabText => $"Projekte ({ProjectCount})";
        public string EntriesTabText => $"Buchungen ({EntryCount})";

        /// <summary>Hinweis oben (Erfolg, Umbenennung, Fehler). Null = kein Hinweis.</summary>
        public string Notice
        {
            get => _notice;
            set
            {
                if (SetProperty(ref _notice, value))
                    RaisePropertyChanged(nameof(HasNotice));
            }
        }

        public bool HasNotice => !string.IsNullOrEmpty(_notice);

        /// <summary>Seitenbesuch: lädt neu und entfernt einen alten Hinweis.</summary>
        public override void Refresh()
        {
            Notice = null;
            Load();
        }

        private void Load()
        {
            try
            {
                Projects.Clear();
                foreach (var p in _db.GetTrashedProjects())
                    Projects.Add(new TrashedProjectItem(p));

                Entries.Clear();
                foreach (var e in _db.GetTrashedTimeEntries())
                    Entries.Add(new TrashedEntryItem(e));
            }
            catch (Exception ex)
            {
                Notice = ex.Message;
            }

            RaisePropertyChanged(nameof(ProjectCount));
            RaisePropertyChanged(nameof(EntryCount));
            RaisePropertyChanged(nameof(IsEmpty));
            RaisePropertyChanged(nameof(HasItems));
            RaisePropertyChanged(nameof(IsProjectsEmpty));
            RaisePropertyChanged(nameof(IsEntriesEmpty));
            RaisePropertyChanged(nameof(ProjectsTabText));
            RaisePropertyChanged(nameof(EntriesTabText));
            EmptyTrashCommand.RaiseCanExecuteChanged();
        }

        private void RestoreProject(TrashedProjectItem item)
        {
            if (item == null) return;

            try
            {
                string actual = _db.RestoreProject(item.Projektname);
                if (actual == null)
                {
                    Notice = "Projekt nicht mehr im Papierkorb.";
                }
                else
                {
                    Notice = RestoreNotice(item.Projektname, actual);
                    _trashChanged?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Notice = ex.Message;
            }
            Load();
        }

        private void RestoreEntry(TrashedEntryItem item)
        {
            if (item == null) return;

            try
            {
                string actual = _db.RestoreTimeEntry(item.Id);
                if (actual == null)
                {
                    Notice = "Buchung nicht mehr im Papierkorb.";
                }
                else if (item.ProjektGeloescht)
                {
                    Notice = EntryRestoreNotice(item.Projektname, actual);
                    _trashChanged?.Invoke();
                }
                else
                {
                    Notice = "Buchung wiederhergestellt.";
                }
            }
            catch (Exception ex)
            {
                Notice = ex.Message;
            }
            Load();
        }

        private void PurgeProject(TrashedProjectItem item)
        {
            if (item == null) return;
            if (!_confirm.Confirm(ConfirmTexts.PurgeProjectText(item.Projektname, item.AnzahlBuchungen), ConfirmTexts.PurgeTitle))
                return;

            try
            {
                _db.PurgeProject(item.Projektname);
            }
            catch (Exception ex)
            {
                Notice = ex.Message;
            }
            Load();
        }

        private void PurgeEntry(TrashedEntryItem item)
        {
            if (item == null) return;
            if (!_confirm.Confirm(ConfirmTexts.PurgeEntryText(item.Startzeit, item.Endzeit, item.Projektname), ConfirmTexts.PurgeTitle))
                return;

            try
            {
                _db.PurgeTimeEntry(item.Id);
            }
            catch (Exception ex)
            {
                Notice = ex.Message;
            }
            Load();
        }

        private void EmptyTrash()
        {
            if (!_confirm.Confirm(ConfirmTexts.EmptyTrashText(Projects.Count, Entries.Count), ConfirmTexts.EmptyTrashTitle))
                return;

            try
            {
                int n = _db.EmptyTrash();
                Notice = $"Papierkorb geleert ({n} Einträge gelöscht).";
            }
            catch (Exception ex)
            {
                Notice = ex.Message;
            }
            Load();
        }

        /// <summary>Hinweistext nach dem Wiederherstellen eines Projekts (rein, testbar).</summary>
        internal static string RestoreNotice(string original, string actual)
        {
            if (actual == original)
                return $"Projekt „{original}“ wurde wiederhergestellt.";
            if (IsSameAfterSuffixRemoval(original, actual))
                return $"Projekt „{original}“ wurde als „{actual}“ wiederhergestellt.";
            return $"Projekt „{original}“ wurde als „{actual}“ wiederhergestellt, weil der Name bereits vergeben war.";
        }

        /// <summary>Hinweistext nach dem Wiederherstellen einer Buchung, deren Projekt mit zurückkommt (rein, testbar).</summary>
        internal static string EntryRestoreNotice(string trashedProjectName, string actual)
        {
            if (actual == trashedProjectName || IsSameAfterSuffixRemoval(trashedProjectName, actual))
                return $"Buchung wiederhergestellt; das Projekt „{actual}“ wurde mit wiederhergestellt.";
            return $"Buchung wiederhergestellt; das Projekt wurde als „{actual}“ wiederhergestellt, weil der Name bereits vergeben war.";
        }

        private static bool IsSameAfterSuffixRemoval(string original, string actual)
        {
            return original.EndsWith(DeletedSuffix, StringComparison.Ordinal)
                && original.Substring(0, original.Length - DeletedSuffix.Length) == actual;
        }
    }

    /// <summary>Anzeigezeile für ein Projekt im Papierkorb.</summary>
    internal sealed class TrashedProjectItem
    {
        public TrashedProjectItem(TimeTracker.TrashedProjectRow row)
        {
            Row = row ?? throw new ArgumentNullException(nameof(row));
        }

        public TimeTracker.TrashedProjectRow Row { get; }
        public string Projektname => Row.Projektname;
        public string Kunde => Row.Kunde;
        public string Kostentraeger => Row.Kostentraeger;
        public int AnzahlBuchungen => Row.AnzahlBuchungen;
        public string GeloeschtAmText => Row.GeloeschtAm.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>Anzeigezeile für eine einzeln gelöschte Buchung.</summary>
    internal sealed class TrashedEntryItem
    {
        public TrashedEntryItem(TimeTracker.TrashedTimeEntryRow row)
        {
            Row = row ?? throw new ArgumentNullException(nameof(row));
        }

        public TimeTracker.TrashedTimeEntryRow Row { get; }
        public int Id => Row.Id;
        public string DatumText => Row.Datum.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        public string Startzeit => Row.Startzeit;
        public string Endzeit => Row.Endzeit;
        public string ZeitText => $"{Row.Startzeit}–{Row.Endzeit}";
        public string Projektname => Row.Projektname;
        public string Beschreibung => Row.Beschreibung ?? string.Empty;
        public string GeloeschtAmText => Row.GeloeschtAm.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
        public bool ProjektGeloescht => Row.ProjektGeloescht;
        public string ProjektHinweis => ProjektGeloescht ? "Projekt ebenfalls im Papierkorb – wird mit wiederhergestellt" : null;
    }
}
