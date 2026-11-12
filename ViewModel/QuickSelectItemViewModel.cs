using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Zeitmanagement.MVVM;

namespace Zeitmanagement.ViewModel
{
    /// <summary>Builds the keys that tell Quick Switch slots apart: one per project, one per ticket.</summary>
    public static class SlotKey
    {
        public static string ForProject(string project) => "p:" + project;
        public static string ForTicket(int ticketId) => "t:" + ticketId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A To-Do ticket as offered in Quick Switch's ticket mode. Two options are equal when everything shown
    /// is equal, so a renamed ticket is replaced in the list while untouched ones keep their place.
    /// </summary>
    public sealed class QuickSwitchTicketOption : IEquatable<QuickSwitchTicketOption>
    {
        public QuickSwitchTicketOption(int id, string title, string project)
        {
            Id = id;
            Title = title;
            Project = project;
        }

        public int Id { get; }
        public string Title { get; }

        /// <summary>The project of the ticket's board; the time is booked on it.</summary>
        public string Project { get; }

        /// <summary>What the drop-down shows. Sorted by project, so the project comes first.</summary>
        public string Label => Project + " · " + Title;

        public bool Equals(QuickSwitchTicketOption other) =>
            other != null && Id == other.Id && Title == other.Title && Project == other.Project;

        public override bool Equals(object obj) => Equals(obj as QuickSwitchTicketOption);

        public override int GetHashCode() => Id;

        public override string ToString() => Label;
    }

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

        /// <summary>A slot that books on a whole project (the booking text stays "Quick Select").</summary>
        public QuickSelectItemViewModel(string project, Action<string> begin, Action<string> stop, Action<string> pause)
            : this(SlotKey.ForProject(project), project, project, "Quick Select", null, begin, stop, pause)
        {
        }

        /// <summary>
        /// A slot that books on the project of a To-Do ticket, with the ticket's title as booking text.
        /// </summary>
        public QuickSelectItemViewModel(QuickSwitchTicketOption ticket, Action<string> begin, Action<string> stop, Action<string> pause)
            : this(SlotKey.ForTicket(ticket.Id), ticket.Project, ticket.Title, ticket.Title, ticket.Project, begin, stop, pause)
        {
        }

        private QuickSelectItemViewModel(string key, string project, string title, string bookingText, string subtitle,
            Action<string> begin, Action<string> stop, Action<string> pause)
        {
            Key = key;
            SelectedProject = project;
            Title = title;
            BookingText = bookingText;
            _subtitle = subtitle;
            _begin = begin;
            _stop = stop;
            _pause = pause;

            StartTimerCommand = new DelegateCommand(StartTimerCommandExecute, StartTimerCommandCanExecute);
            EndTimerCommand = new DelegateCommand(EndTimerCommandExecute);
            PauseTimerCommand = new DelegateCommand(PauseTimerCommandExecute, PauseTimerCommandCanExecute);
        }

        private string _start;
        private string _end;

        /// <summary>Identifies the slot: the project, or the ticket, it was started from (see <see cref="SlotKey"/>).</summary>
        public string Key { get; }

        /// <summary>The project this slot books its time on.</summary>
        public string SelectedProject { get; }

        /// <summary>What the floating window shows as the slot's name: the project, or the ticket's title.</summary>
        public string Title { get; }

        /// <summary>The booking text written to the time entry.</summary>
        public string BookingText { get; }

        // The project name shown next to the time for ticket slots; null for project slots.
        private readonly string _subtitle;

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
        public string StatusText
        {
            get
            {
                var time = IsPaused ? $"pausiert seit {End}" : $"seit {Start}";
                return _subtitle == null ? time : $"{_subtitle} · {time}";
            }
        }

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
            _begin.Invoke(Key);
        }

        public bool StartTimerCommandCanExecute(object obj)
        {
            // Only offered to resume a paused slot - a running one has the pause/stop buttons.
            return IsPaused;
        }

        public void EndTimerCommandExecute(object obj)
        {
            _stop.Invoke(Key);
        }

        public void PauseTimerCommandExecute(object obj)
        {
            _pause.Invoke(Key);
        }

        public bool PauseTimerCommandCanExecute(object obj)
        {
            return !IsPaused;
        }
    }
}
