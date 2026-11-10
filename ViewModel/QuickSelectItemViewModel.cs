using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Zeitmanagement.MVVM;

namespace Zeitmanagement.ViewModel
{
    /// <summary>
    /// One project booking in the floating Quick Switch window. A slot exists exactly as long as
    /// its booking is running or paused: <see cref="QuickSelectViewModel"/> creates it when the
    /// project is started and drops it once it is stopped.
    /// </summary>
    public class QuickSelectItemViewModel:BindableBase
    {
        private readonly Action<string> _begin;
        private readonly Action<string> _stop;
        private readonly Action<string> _pause;

        public QuickSelectItemViewModel(string project, Action<string> begin, Action<string> stop, Action<string> pause)
        {
            SelectedProject = project;
            _begin = begin;
            _stop = stop;
            _pause = pause;

            StartTimerCommand = new DelegateCommand(StartTimerCommandExecute, StartTimerCommandCanExecute);
            EndTimerCommand = new DelegateCommand(EndTimerCommandExecute);
            PauseTimerCommand = new DelegateCommand(PauseTimerCommandExecute, PauseTimerCommandCanExecute);
        }

        private string _start;
        private string _end;

        /// <summary>The project this slot books its time on.</summary>
        public string SelectedProject { get; }

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

        private bool _isPaused;
        /// <summary>
        /// Whether this slot is reserved but currently not accruing time - the booking for the
        /// segment up to the pause has already been persisted, and the slot is waiting to be
        /// resumed or stopped.
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
        /// today's date. Null while the slot is paused.
        /// </summary>
        public DateTime? StartedAt { get; set; }

        /// <summary>
        /// Running time accumulated over segments already closed out by a pause, so
        /// <see cref="ElapsedText"/> keeps counting across a pause/resume cycle instead of
        /// restarting at zero.
        /// </summary>
        private TimeSpan _accumulatedElapsed = TimeSpan.Zero;

        /// <summary>
        /// Folds the segment since <see cref="StartedAt"/> into <see cref="_accumulatedElapsed"/>.
        /// Called by the owning view model when a running segment ends by pausing.
        /// </summary>
        public void AccumulateElapsed(DateTime now)
        {
            if (StartedAt == null) return;

            var running = now - StartedAt.Value;
            if (running > TimeSpan.Zero) _accumulatedElapsed += running;
        }

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


        public void StartTimerCommandExecute(object obj)
        {
            _begin.Invoke(SelectedProject);
        }

        public bool StartTimerCommandCanExecute(object obj)
        {
            // Only offered to resume a paused slot - a running one has the pause/stop buttons.
            return IsPaused;
        }

        public void EndTimerCommandExecute(object obj)
        {
            _stop.Invoke(SelectedProject);
        }

        public void PauseTimerCommandExecute(object obj)
        {
            _pause.Invoke(SelectedProject);
        }

        public bool PauseTimerCommandCanExecute(object obj)
        {
            return !IsPaused;
        }
    }
}
