using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Zeitmanagement.MVVM;

namespace Zeitmanagement.ViewModel
{
    public class QuickSelectItemViewModel:BindableBase
    {
        private readonly Action<string> _begin;
        private readonly Action<string> _stop;
        private readonly Action<string> _pause;
        private readonly Action _saveProjectnames;
        private readonly Action<QuickSelectItemViewModel> _removeRequested;

        public QuickSelectItemViewModel(Action<string> begin, Action<string> stop, Action<string> pause, Action saveProjectnames, Action<QuickSelectItemViewModel> removeRequested)
        {
            _begin = begin;
            _stop = stop;
            _pause = pause;
            _saveProjectnames = saveProjectnames;
            _removeRequested = removeRequested;

            StartTimerCommand = new DelegateCommand(StartTimerCommandExecute, StartTimerCommandCanExecute);
            EndTimerCommand = new DelegateCommand(EndTimerCommandExecute, EndTimerCommandCanExecute);
            PauseTimerCommand = new DelegateCommand(PauseTimerCommandExecute, PauseTimerCommandCanExecute);
            RemoveCommand = new DelegateCommand(RemoveCommandExecute, RemoveCommandCanExecute);
        }


        public ObservableCollection<string> AvailableProjects { get; set; } = new ObservableCollection<string>();

        private string _start;
        private string _end;

        private string _selectedProject = "";

        public string SelectedProject
        {
            get => _selectedProject;
            set
            {
                // Configuring which project a slot refers to (done in the main window's Quick
                // Select list) must not, by itself, start a booking - it's just editing the
                // list of available slots. Only the floating window's "start a project" combo
                // box (QuickSelectViewModel.PendingNewProject) auto-starts on selection.
                SetProperty(ref _selectedProject, value);
                _saveProjectnames.Invoke();
            }
        }

        /// <summary>
        /// Sets the selected project without notifying the parent view model, so restoring
        /// saved slots on load doesn't trigger a save/reconcile cycle per row.
        /// </summary>
        internal void SetSelectedProjectSilently(string project)
        {
            SetProperty(ref _selectedProject, project ?? string.Empty, nameof(SelectedProject));
        }

        public string End
        {
            get => _end;
            set
            {
                if (SetProperty(ref _end, value))
                    RaisePropertyChanged(nameof(StatusText));
            }
        }
        public string Start
        {
            get => _start;
            set
            {
                if (SetProperty(ref _start, value))
                    RaisePropertyChanged(nameof(StatusText));
            }
        }

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }

        private bool _isPaused;
        /// <summary>
        /// Whether this slot is reserved (<see cref="IsActive"/>) but currently not accruing
        /// time - the booking for the segment up to the pause has already been persisted, and
        /// the slot is waiting to be resumed or stopped.
        /// </summary>
        public bool IsPaused
        {
            get => _isPaused;
            set
            {
                if (SetProperty(ref _isPaused, value))
                    RaisePropertyChanged(nameof(StatusText));
            }
        }

        /// <summary>
        /// The floating window's status line for this slot: when the current segment started,
        /// or when it was paused.
        /// </summary>
        public string StatusText => IsPaused ? $"pausiert seit {End}" : $"seit {Start}";

        /// <summary>
        /// The exact moment this slot's current segment started, used for the elapsed-time
        /// display so it stays correct across midnight instead of re-deriving a start time from
        /// today's date. Null while the slot isn't actively running (stopped or paused).
        /// </summary>
        public DateTime? StartedAt { get; set; }

        /// <summary>
        /// Running time accumulated over segments already closed out by a pause, within the
        /// current start-to-stop session, so <see cref="ElapsedText"/> keeps counting across a
        /// pause/resume cycle instead of restarting at zero.
        /// </summary>
        private TimeSpan _accumulatedElapsed = TimeSpan.Zero;

        /// <summary>
        /// Folds the segment since <see cref="StartedAt"/> into <see cref="_accumulatedElapsed"/>.
        /// Called by the owning view model whenever a running segment ends, whether by pausing
        /// or stopping.
        /// </summary>
        public void AccumulateElapsed(DateTime now)
        {
            if (StartedAt == null) return;

            var running = now - StartedAt.Value;
            if (running > TimeSpan.Zero) _accumulatedElapsed += running;
        }

        /// <summary>
        /// Clears the running total once the slot is fully stopped, so a later fresh start
        /// begins counting from zero again.
        /// </summary>
        public void ResetAccumulatedElapsed() => _accumulatedElapsed = TimeSpan.Zero;

        private string _elapsedText = "";
        /// <summary>
        /// Live "HH:mm:ss" elapsed time for the current session, refreshed periodically by the
        /// owning view model via <see cref="RefreshElapsed"/> so multiple slots can run in
        /// parallel, each with their own ticking display. Keeps counting the accumulated total
        /// while paused, without advancing further.
        /// </summary>
        public string ElapsedText
        {
            get => _elapsedText;
            private set => SetProperty(ref _elapsedText, value);
        }

        public void RefreshElapsed()
        {
            if (!IsActive)
            {
                ElapsedText = "";
                return;
            }

            var elapsed = _accumulatedElapsed;
            if (!IsPaused && StartedAt != null)
            {
                var running = DateTime.Now - StartedAt.Value;
                if (running > TimeSpan.Zero) elapsed += running;
            }

            ElapsedText = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        }

        public DelegateCommand StartTimerCommand { get; set; }
        public DelegateCommand EndTimerCommand { get; set; }
        public DelegateCommand PauseTimerCommand { get; set; }
        public DelegateCommand RemoveCommand { get; set; }


        public void StartTimerCommandExecute(object obj)
        {
            _begin.Invoke(SelectedProject);
        }

        public bool StartTimerCommandCanExecute(object obj)
        {
            // Enabled to start a fresh slot, or to resume one that's paused - disabled only
            // while it's actively running (that's what the pause/stop buttons are for).
            return !IsActive || IsPaused;
        }

        public void EndTimerCommandExecute(object obj)
        {
            _stop.Invoke(SelectedProject);
        }

        public bool EndTimerCommandCanExecute(object obj)
        {
            return IsActive;
        }

        public void PauseTimerCommandExecute(object obj)
        {
            _pause.Invoke(SelectedProject);
        }

        public bool PauseTimerCommandCanExecute(object obj)
        {
            return IsActive && !IsPaused;
        }

        public void RemoveCommandExecute(object obj)
        {
            _removeRequested?.Invoke(this);
        }

        public bool RemoveCommandCanExecute(object obj)
        {
            return !IsActive && !string.IsNullOrWhiteSpace(SelectedProject);
        }
    }
}
