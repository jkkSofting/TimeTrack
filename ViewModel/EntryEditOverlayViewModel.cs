using System;
using System.Collections.Generic;
using Zeitmanagement.MVVM;

namespace Zeitmanagement.ViewModel
{
    /// <summary>
    /// Bearbeiten-Overlay für eine vorhandene Buchung. Die eigentliche Logik (Überschneidungs-Rückfrage,
    /// Datenbank, Neuladen) liegt im <see cref="EntriesViewModel"/>; dieses VM hält nur die Formularwerte
    /// und ruft die übergebenen Callbacks auf. Ein Callback liefert true bei Erfolg: dann schließt sich das Overlay.
    /// </summary>
    internal sealed class EntryEditOverlayViewModel : OverlayViewModelBase
    {
        private readonly Func<EntryEditOverlayViewModel, bool> _save;
        private readonly Func<EntryEditOverlayViewModel, bool> _delete;

        private string _projektname, _start, _end, _beschreibung;

        public EntryEditOverlayViewModel(
            int id,
            string projektname,
            string start,
            string end,
            string beschreibung,
            IEnumerable<string> projectNames,
            Func<EntryEditOverlayViewModel, bool> save,
            Func<EntryEditOverlayViewModel, bool> delete)
            : base("Buchung bearbeiten")
        {
            Id = id;
            _projektname = projektname;
            _start = start;
            _end = end;
            _beschreibung = beschreibung;
            ProjectNames = projectNames;
            _save = save ?? throw new ArgumentNullException(nameof(save));
            _delete = delete ?? throw new ArgumentNullException(nameof(delete));

            DangerText = "In den Papierkorb";
            DangerCommand = new DelegateCommand(_ => OnDelete());
        }

        /// <summary>Id der bearbeiteten Buchung.</summary>
        public int Id { get; }

        /// <summary>Projektliste für das Auswahlfeld.</summary>
        public IEnumerable<string> ProjectNames { get; }

        public string Projektname
        {
            get => _projektname;
            set => SetProperty(ref _projektname, value);
        }

        public string Start
        {
            get => _start;
            set
            {
                if (SetProperty(ref _start, value))
                    RaiseCanSaveChanged();
            }
        }

        public string End
        {
            get => _end;
            set
            {
                if (SetProperty(ref _end, value))
                    RaiseCanSaveChanged();
            }
        }

        public string Beschreibung
        {
            get => _beschreibung;
            set => SetProperty(ref _beschreibung, value);
        }

        protected override bool CanSave()
        {
            return EntriesViewModel.IsHHmm(Start)
                   && EntriesViewModel.IsHHmm(End)
                   && EntriesViewModel.IsStartBeforeEnd(Start, End);
        }

        protected override void OnSave()
        {
            if (_save(this))
                Close();
        }

        private void OnDelete()
        {
            if (_delete(this))
                Close();
        }
    }
}
