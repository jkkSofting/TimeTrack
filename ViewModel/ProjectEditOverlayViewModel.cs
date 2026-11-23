using System;
using System.Collections.Generic;
using System.Linq;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.ViewModel
{
    /// <summary>
    /// Overlay zum Anlegen und Bearbeiten eines Projekts (Name, Kunde, Kostenträger, extern, Rundung).
    /// Die Rundung ist eine Dreifach-Auswahl: Standardrundung erben, eigener Wert oder für dieses Projekt aus.
    /// </summary>
    internal sealed class ProjectEditOverlayViewModel : OverlayViewModelBase
    {
        /// <summary>Wie die Rundung des Projekts bestimmt wird.</summary>
        public enum RoundingMode
        {
            /// <summary>Das Projekt erbt die globale Standardrundung.</summary>
            Standard,

            /// <summary>Das Projekt hat einen eigenen Wert (5, 10, 15 oder 60 Minuten).</summary>
            Own,

            /// <summary>Für dieses Projekt wird nicht gerundet.</summary>
            Off
        }

        private readonly TimeTracker _db;
        private readonly Action _onSaved;
        private readonly string _originalName; // null = neues Projekt

        private string _projektname;
        private string _kunde;
        private string _kostentraeger;
        private bool _istExtern;
        private RoundingMode _mode;
        private int _ownMinutes = 15;
        private string _errorText;

        /// <summary>Wählbare eigene Werte (ohne „Aus“, das ist eine eigene Option).</summary>
        public static IReadOnlyList<RoundingChoice> OwnChoices { get; } =
            RoundingOptions.Allowed.Where(m => m > 0).Select(m => new RoundingChoice(m)).ToList();

        private ProjectEditOverlayViewModel(TimeTracker db, string originalName, Action onSaved)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _originalName = originalName;
            _onSaved = onSaved;

            GlobalMinutes = db.GetGlobalRoundingMinutes();
            Title = originalName == null ? "Neues Projekt" : "Projekt bearbeiten";
            SaveText = originalName == null ? "Hinzufügen" : "Speichern";
        }

        /// <summary>Overlay zum Anlegen eines neuen Projekts (Rundung: Standard).</summary>
        public static ProjectEditOverlayViewModel ForNew(TimeTracker db, Action onSaved = null)
        {
            return new ProjectEditOverlayViewModel(db, null, onSaved);
        }

        /// <summary>Overlay zum Bearbeiten; der Anfangszustand stammt aus der Projektzeile.</summary>
        public static ProjectEditOverlayViewModel ForEdit(TimeTracker db, string projektname, string kunde, string kostentraeger,
            bool istExtern, bool rundungGlobal, int rundungEigenMinuten, Action onSaved = null)
        {
            var vm = new ProjectEditOverlayViewModel(db, projektname, onSaved);
            vm._projektname = projektname;
            vm._kunde = kunde;
            vm._kostentraeger = kostentraeger;
            vm._istExtern = istExtern;

            int own = RoundingOptions.Normalize(rundungEigenMinuten);
            if (rundungGlobal)
            {
                vm._mode = RoundingMode.Standard;
            }
            else if (own > 0)
            {
                vm._mode = RoundingMode.Own;
                vm._ownMinutes = own;
            }
            else
            {
                vm._mode = RoundingMode.Off;
            }
            return vm;
        }

        /// <summary>True beim Bearbeiten eines vorhandenen Projekts.</summary>
        public bool IsEdit => _originalName != null;

        /// <summary>Aktuelle globale Standardrundung in Minuten (0 = Aus).</summary>
        public int GlobalMinutes { get; }

        /// <summary>Text der Standard-Option inklusive aktuellem Wert, z. B. „Standard verwenden (aktuell: 15 Minuten)“.</summary>
        public string StandardOptionText => "Standard verwenden (aktuell: " + RoundingOptions.Label(GlobalMinutes) + ")";

        public IReadOnlyList<RoundingChoice> RoundingChoices => OwnChoices;

        public string Projektname
        {
            get => _projektname;
            set { if (SetProperty(ref _projektname, value)) FieldChanged(); }
        }

        public string Kunde
        {
            get => _kunde;
            set { if (SetProperty(ref _kunde, value)) FieldChanged(); }
        }

        public string Kostentraeger
        {
            get => _kostentraeger;
            set { if (SetProperty(ref _kostentraeger, value)) FieldChanged(); }
        }

        public bool IstExtern
        {
            get => _istExtern;
            set => SetProperty(ref _istExtern, value);
        }

        public RoundingMode Mode
        {
            get => _mode;
            set
            {
                if (_mode == value) return;
                _mode = value;
                RaisePropertyChanged(nameof(Mode));
                RaisePropertyChanged(nameof(IsRoundingStandard));
                RaisePropertyChanged(nameof(IsRoundingOwn));
                RaisePropertyChanged(nameof(IsRoundingOff));
            }
        }

        // Die RadioButtons binden an je ein bool; nur das Setzen auf true ändert den Modus, das Abwählen
        // folgt daraus (die Bindings werden über PropertyChanged aktualisiert).
        public bool IsRoundingStandard
        {
            get => _mode == RoundingMode.Standard;
            set { if (value) Mode = RoundingMode.Standard; }
        }

        public bool IsRoundingOwn
        {
            get => _mode == RoundingMode.Own;
            set { if (value) Mode = RoundingMode.Own; }
        }

        public bool IsRoundingOff
        {
            get => _mode == RoundingMode.Off;
            set { if (value) Mode = RoundingMode.Off; }
        }

        /// <summary>Eigener Rundungswert (5, 10, 15 oder 60); Vorgabe beim Wechsel auf „Eigene Rundung“ ist 15.</summary>
        public int OwnMinutes
        {
            get => _ownMinutes;
            set
            {
                if (OwnChoices.All(c => c.Minutes != value)) return; // z. B. null/0 aus der ComboBox ignorieren
                SetProperty(ref _ownMinutes, value);
            }
        }

        /// <summary>Fehlermeldung des letzten Speicherversuchs (leer: kein Fehler).</summary>
        public string ErrorText
        {
            get => _errorText;
            private set
            {
                if (SetProperty(ref _errorText, value))
                    RaisePropertyChanged(nameof(HasError));
            }
        }

        public bool HasError => !string.IsNullOrEmpty(_errorText);

        private void FieldChanged()
        {
            ErrorText = null;
            RaiseCanSaveChanged();
        }

        protected override bool CanSave()
        {
            return !string.IsNullOrWhiteSpace(_projektname)
                && !string.IsNullOrWhiteSpace(_kunde)
                && !string.IsNullOrWhiteSpace(_kostentraeger);
        }

        protected override void OnSave()
        {
            if (!CanSave()) return;

            var name = _projektname.Trim();
            var kunde = _kunde.Trim();
            var kt = _kostentraeger.Trim();

            // Für die DB immer beide Rundungsparameter ausdrücklich angeben.
            bool useGlobal = _mode == RoundingMode.Standard;
            int minutes = _mode == RoundingMode.Own ? _ownMinutes : 0;

            try
            {
                if (_originalName == null)
                {
                    // AddOrEnsureProject ist ein UPSERT und würde ein vorhandenes Projekt still überschreiben.
                    if (_db.GetProjects().Any(p => string.Equals(p.Projektname, name, StringComparison.Ordinal)))
                        throw new InvalidOperationException("Projektname '" + name + "' ist bereits vergeben.");

                    _db.AddOrEnsureProject(name, kunde, kt, _istExtern, rundungMinuten: minutes, rundungGlobal: useGlobal);
                }
                else
                {
                    _db.UpdateProject(_originalName, name, kunde, kt, _istExtern, rundungMinuten: minutes, rundungGlobal: useGlobal);
                }
            }
            catch (Exception ex)
            {
                ErrorText = ex.Message;
                return;
            }

            Close();
            _onSaved?.Invoke();
        }
    }
}
