using System;
using System.Collections.Generic;
using System.Linq;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.ViewModel
{
    /// <summary>Overlay „Standardrundung“: die globale Aufrundung je Buchung für alle Projekte ohne eigene Rundung.</summary>
    internal sealed class GlobalRoundingOverlayViewModel : OverlayViewModelBase
    {
        private readonly TimeTracker _db;
        private readonly int _inheritingProjects;
        private readonly Action _onSaved;
        private int _selectedMinutes;
        private string _errorText;

        /// <param name="inheritingProjects">Anzahl der Projekte, die die Standardrundung erben (nur für den Hinweis).</param>
        public GlobalRoundingOverlayViewModel(TimeTracker db, int inheritingProjects, Action onSaved = null)
            : base("Standardrundung")
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _inheritingProjects = Math.Max(0, inheritingProjects);
            _onSaved = onSaved;
            _selectedMinutes = db.GetGlobalRoundingMinutes();
        }

        /// <summary>Auswahl: Aus, 5, 10, 15, 60 Minuten.</summary>
        public IReadOnlyList<RoundingChoice> Choices { get; } =
            RoundingOptions.Allowed.Select(m => new RoundingChoice(m)).ToList();

        public int SelectedMinutes
        {
            get => _selectedMinutes;
            set
            {
                if (Array.IndexOf(RoundingOptions.Allowed, value) < 0) return;
                SetProperty(ref _selectedMinutes, value);
            }
        }

        /// <summary>Hinweis, wie viele Projekte die Standardrundung erben.</summary>
        public string InheritText
        {
            get
            {
                switch (_inheritingProjects)
                {
                    case 0: return "Aktuell erbt kein Projekt die Standardrundung.";
                    case 1: return "Aktuell erbt 1 Projekt die Standardrundung.";
                    default: return "Aktuell erben " + _inheritingProjects + " Projekte die Standardrundung.";
                }
            }
        }

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

        protected override void OnSave()
        {
            try
            {
                _db.SetGlobalRoundingMinutes(_selectedMinutes);
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
