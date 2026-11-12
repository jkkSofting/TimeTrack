using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Zeitmanagement.Helpers;
using Zeitmanagement.MVVM;

namespace Zeitmanagement.ViewModel
{
    /// <summary>
    /// Backs the floating Quick Switch window: which projects or To-Do tickets can be started, and the
    /// bookings currently running or paused. Also runs the lock/sleep auto-stop and the idle reminder.
    /// </summary>
    internal class QuickSelectViewModel:BindableBase
    {
        /// <summary>
        /// All projects that may be picked in Quick Switch, i.e. every project except those the
        /// user disabled in the project overview, in alphabetical order. Serves as the item
        /// source for the floating window's "start a project" combo box. Kept current by
        /// <see cref="ReloadProjects"/>.
        /// </summary>
        public ObservableCollection<string> SelectableProjects { get; } = new ObservableCollection<string>();

        /// <summary>
        /// The To-Do tickets that may be picked in ticket mode: those in columns the user switched on for
        /// Quick Switch (in the To-Do overview), on a project's board, sorted by project and title. Kept
        /// current by <see cref="ReloadTickets"/>.
        /// </summary>
        public ObservableCollection<QuickSwitchTicketOption> Tickets { get; } = new ObservableCollection<QuickSwitchTicketOption>();

        /// <summary>
        /// The projects currently running or paused, in parallel, up to <see cref="MaxActiveSlots"/>
        /// at a time, in the order they were started. A slot lives exactly as long as its booking
        /// is open: it is added on start and dropped on stop. Serves as the item source for the
        /// floating window's slot list.
        /// </summary>
        public ObservableCollection<QuickSelectItemViewModel> ActiveSlots { get; } = new ObservableCollection<QuickSelectItemViewModel>();

        /// <summary>
        /// How many projects a user may track in parallel at once.
        /// </summary>
        public const int MaxActiveSlots = 4;

        TimeTracker db;

        private readonly DispatcherTimer _statusTimer;

        public DelegateCommand StartNewSlotCommand { get; }

        /// <summary>
        /// Raised when the user should be notified via a tray balloon: an auto-stop happened,
        /// or no booking has been running for a while. (title, message)
        /// </summary>
        public event Action<string, string> NotificationRequested;

        public QuickSelectViewModel()
        {
            db = MainViewModel.DbInstance;

            StartNewSlotCommand = new DelegateCommand(StartNewSlotExecute, StartNewSlotCanExecute);

            _isTicketMode = Properties.Settings.Default.QuickSwitchTicketMode;
            Tickets.CollectionChanged += (s, e) => RaisePropertyChanged(nameof(NoTicketsAvailable));

            ReloadProjects();
            UpdateActiveState();

            // Keeps the "running since" elapsed display in the floating window live.
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statusTimer.Tick += (s, e) => UpdateElapsed();
            _statusTimer.Start();

            InitializeSessionAutomation();
        }

        #region Floating window state

        private bool _isAnyActive;
        public bool IsAnyActive
        {
            get => _isAnyActive;
            private set
            {
                if (SetProperty(ref _isAnyActive, value))
                    RaisePropertyChanged(nameof(NoActiveSlots));
            }
        }

        /// <summary>
        /// Inverse of <see cref="IsAnyActive"/>, for the floating window's "nothing running"
        /// hint (plain bool properties keep the XAML free of extra converters).
        /// </summary>
        public bool NoActiveSlots => !IsAnyActive;

        private bool _canStartMoreSlots = true;
        /// <summary>
        /// Whether fewer than <see cref="MaxActiveSlots"/> projects are currently running, so
        /// the floating window's "start a project" controls should stay enabled.
        /// </summary>
        public bool CanStartMoreSlots
        {
            get => _canStartMoreSlots;
            private set
            {
                if (SetProperty(ref _canStartMoreSlots, value))
                    RaisePropertyChanged(nameof(MaxSlotsReached));
            }
        }

        /// <summary>
        /// Inverse of <see cref="CanStartMoreSlots"/>, for the floating window's "limit reached"
        /// hint (plain bool properties keep the XAML free of extra converters).
        /// </summary>
        public bool MaxSlotsReached => !CanStartMoreSlots;

        private bool _isTicketMode;
        /// <summary>
        /// Whether the "start" combo box offers To-Do tickets instead of projects. Remembered between
        /// sessions. Switching only changes what is offered; nothing is started by it.
        /// </summary>
        public bool IsTicketMode
        {
            get => _isTicketMode;
            set
            {
                if (!SetProperty(ref _isTicketMode, value)) return;

                RaisePropertyChanged(nameof(IsProjectMode));
                RaisePropertyChanged(nameof(NoTicketsAvailable));
                RefreshStartState();

                Properties.Settings.Default.QuickSwitchTicketMode = value;
                Properties.Settings.Default.Save();
            }
        }

        /// <summary>Inverse of <see cref="IsTicketMode"/>, so each radio button of the mode switch binds to a plain bool.</summary>
        public bool IsProjectMode
        {
            get => !_isTicketMode;
            set
            {
                if (value) IsTicketMode = false;
            }
        }

        /// <summary>
        /// Ticket mode is on but no ticket is offered, so the window can say where to switch tickets on.
        /// </summary>
        public bool NoTicketsAvailable => IsTicketMode && Tickets.Count == 0;

        private string _pendingNewProject;
        /// <summary>
        /// The project currently picked in the floating window's "start a project" combo box,
        /// waiting to be started via <see cref="StartNewSlotCommand"/>.
        /// </summary>
        public string PendingNewProject
        {
            get => _pendingNewProject;
            set
            {
                bool changed = SetProperty(ref _pendingNewProject, value);
                RefreshStartState();

                // Picking a project in the floating window's combo box should start it right
                // away - no separate click on "Start" required. The selection is left in place
                // afterwards (see StartNewSlotExecute) so that once the project is stopped again,
                // it's still selected and a single click on "Start" restarts it - no need to
                // reopen the combo box and pick it again.
                if (changed && IsProjectMode && StartNewSlotCanExecute(null))
                {
                    StartNewSlotExecute(null);
                }
            }
        }

        private QuickSwitchTicketOption _pendingNewTicket;
        /// <summary>
        /// The ticket currently picked in ticket mode, waiting to be started via
        /// <see cref="StartNewSlotCommand"/>. Like <see cref="PendingNewProject"/>, picking it starts it.
        /// </summary>
        public QuickSwitchTicketOption PendingNewTicket
        {
            get => _pendingNewTicket;
            set
            {
                bool changed = SetProperty(ref _pendingNewTicket, value);
                RefreshStartState();

                if (changed && IsTicketMode && StartNewSlotCanExecute(null))
                {
                    StartNewSlotExecute(null);
                }
            }
        }

        /// <summary>
        /// The project the current pick (project or ticket, depending on the mode) would book on, or
        /// null while nothing is picked.
        /// </summary>
        private string PendingBookingProject
        {
            get
            {
                if (IsTicketMode) return PendingNewTicket?.Project;
                return string.IsNullOrWhiteSpace(PendingNewProject) ? null : PendingNewProject;
            }
        }

        /// <summary>
        /// Whether a running or paused slot already books on <paramref name="project"/>. A second slot
        /// on the same project would book overlapping times on it, so it is not allowed.
        /// </summary>
        private bool ProjectInUse(string project)
        {
            return ActiveSlots.Any(q => string.Equals(q.SelectedProject, project, StringComparison.Ordinal));
        }

        /// <summary>
        /// Why the current pick cannot be started although a slot is free, for the floating window; null
        /// when it can be started or nothing is picked.
        /// </summary>
        public string StartBlockedText
        {
            get
            {
                var project = PendingBookingProject;
                if (project == null || ActiveSlots.Count >= MaxActiveSlots || !ProjectInUse(project)) return null;

                return $"Für „{project}“ läuft bereits eine Buchung. Beende sie zuerst.";
            }
        }

        public bool HasStartBlockedText => StartBlockedText != null;

        /// <summary>Re-evaluates everything that depends on the current pick, the mode or the running slots.</summary>
        private void RefreshStartState()
        {
            StartNewSlotCommand.RaiseCanExecuteChanged();
            RaisePropertyChanged(nameof(StartBlockedText));
            RaisePropertyChanged(nameof(HasStartBlockedText));
        }

        private bool StartNewSlotCanExecute(object obj)
        {
            var project = PendingBookingProject;
            if (project == null) return false;
            if (ActiveSlots.Count >= MaxActiveSlots) return false;

            return !ProjectInUse(project);
        }

        private void StartNewSlotExecute(object obj)
        {
            if (!StartNewSlotCanExecute(null)) return;

            QuickSelectItemViewModel slot;
            if (IsTicketMode)
                slot = new QuickSelectItemViewModel(PendingNewTicket, ResumeBooking, StopBooking, PauseBooking);
            else
                slot = new QuickSelectItemViewModel(PendingNewProject, ResumeBooking, StopBooking, PauseBooking);

            var now = DateTime.Now;
            slot.Start = now.ToString("HH:mm");
            slot.StartedAt = now;
            ActiveSlots.Add(slot);

            UpdateActiveState();

            // The pick is deliberately left set to the just-started project or ticket: once it's
            // stopped again, the combo box still shows it selected and "Start" is enabled again,
            // so restarting it is a single click instead of reopening the combo box.
        }

        /// <summary>
        /// Re-reads the project list from the database, so projects that were added, renamed,
        /// deleted or (de)activated for Quick Switch since the last call show up in the floating
        /// window's combo box without a restart. A running slot keeps its project until it is
        /// stopped.
        /// </summary>
        public void ReloadProjects()
        {
            var selectable = db.GetProjects()
                .Where(p => p.QuickSelectAktiv)
                .Select(p => p.Projektname)
                .OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            SyncSortedInPlace(SelectableProjects, selectable);

            // Renamed or deleted projects change which tickets can be booked and on what.
            ReloadTickets();
        }

        /// <summary>
        /// Re-reads the tickets Quick Switch offers, so tickets that were added, moved to or from a
        /// switched-on column, renamed or deleted in the To-Do overview show up without a restart. A
        /// running slot keeps its ticket title until it is stopped.
        /// </summary>
        public void ReloadTickets()
        {
            var pendingId = _pendingNewTicket?.Id;

            var desired = db.GetQuickSwitchTickets()
                .Select(t => new QuickSwitchTicketOption(t.Id, t.Titel, t.Projektname))
                .ToList();

            SyncSortedInPlace(Tickets, desired);

            // A renamed ticket is a new list entry, so the combo box dropped the pick: select the
            // same ticket again, without starting it (this is no user action).
            if (pendingId.HasValue && (_pendingNewTicket == null || !Tickets.Contains(_pendingNewTicket)))
            {
                _pendingNewTicket = Tickets.FirstOrDefault(t => t.Id == pendingId.Value);
                RaisePropertyChanged(nameof(PendingNewTicket));
                RefreshStartState();
            }
        }

        /// <summary>
        /// Makes <paramref name="target"/> equal to the (already sorted) <paramref name="desired"/>
        /// list by removing, adding and moving single entries instead of replacing the
        /// collection, so a bound combo box keeps its current selection.
        /// </summary>
        private static void SyncSortedInPlace<T>(ObservableCollection<T> target, IList<T> desired)
        {
            var same = EqualityComparer<T>.Default;

            for (int i = target.Count - 1; i >= 0; i--)
            {
                if (!desired.Contains(target[i]))
                    target.RemoveAt(i);
            }

            for (int i = 0; i < desired.Count; i++)
            {
                if (i < target.Count && same.Equals(target[i], desired[i]))
                    continue;

                int existing = target.IndexOf(desired[i]);
                if (existing > i)
                    target.Move(existing, i);
                else
                    target.Insert(i, desired[i]);
            }
        }

        /// <summary>
        /// Recomputes the "currently active" display state from the slots: whether any is open
        /// and whether another one (up to <see cref="MaxActiveSlots"/> in parallel) can still be
        /// started.
        /// </summary>
        private void UpdateActiveState()
        {
            IsAnyActive = ActiveSlots.Count > 0;
            CanStartMoreSlots = ActiveSlots.Count < MaxActiveSlots;

            RefreshStartState();
            UpdateElapsed();
        }

        private void UpdateElapsed()
        {
            foreach (var item in ActiveSlots)
            {
                item.RefreshElapsed();
            }
        }

        #endregion

        #region Session-based automation (auto-stop on lock/sleep, idle reminder)

        private SessionActivityMonitor _sessionMonitor;
        private DispatcherTimer _lockAutoStopTimer;
        private DispatcherTimer _nudgeCheckTimer;
        private bool _isLocked;
        private bool _isSuspended;
        private DateTime _lastActiveOrNudge = DateTime.Now;

        private void InitializeSessionAutomation()
        {
            _sessionMonitor = new SessionActivityMonitor();
            _sessionMonitor.Locked += OnSessionLocked;
            _sessionMonitor.Unlocked += OnSessionUnlocked;
            _sessionMonitor.Suspending += OnSessionSuspending;
            _sessionMonitor.Resumed += OnSessionResumed;

            // One-shot: (re)started on lock, stopped on unlock/suspend, fires the delayed
            // auto-stop for the lock-screen case.
            _lockAutoStopTimer = new DispatcherTimer();
            _lockAutoStopTimer.Tick += (s, e) =>
            {
                _lockAutoStopTimer.Stop();
                AutoStopActiveBooking("Sperrbildschirm");
            };

            _nudgeCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _nudgeCheckTimer.Tick += (s, e) => CheckNudge();
            _nudgeCheckTimer.Start();
        }

        private void OnSessionLocked()
        {
            _isLocked = true;
            _lockAutoStopTimer.Stop();

            if (Properties.Settings.Default.AutoStopOnLockEnabled && IsAnyActive)
            {
                var minutes = Math.Max(1, Properties.Settings.Default.AutoStopOnLockMinutes);
                _lockAutoStopTimer.Interval = TimeSpan.FromMinutes(minutes);
                _lockAutoStopTimer.Start();
            }
        }

        private void OnSessionUnlocked()
        {
            _isLocked = false;
            _lockAutoStopTimer.Stop();
            _lastActiveOrNudge = DateTime.Now;
        }

        private void OnSessionSuspending()
        {
            _lockAutoStopTimer.Stop();

            if (Properties.Settings.Default.AutoStopOnLockEnabled && IsAnyActive)
            {
                AutoStopActiveBooking("Ruhemodus");
            }

            _isSuspended = true;
        }

        private void OnSessionResumed()
        {
            _isSuspended = false;
            _lastActiveOrNudge = DateTime.Now;
        }

        private void AutoStopActiveBooking(string reason)
        {
            // Snapshot first: StopBooking removes the slots from ActiveSlots as it goes, so
            // iterating the live collection would skip entries.
            var active = ActiveSlots.Select(q => new { q.Key, q.Title }).ToList();
            if (active.Count == 0)
                return;

            foreach (var slot in active)
            {
                // Reuses the same booking-end/persistence logic as the manual per-slot stop
                // button, so there's a single place that ends a booking. Always a full stop -
                // even a paused slot is cleared out rather than left reserved - since the
                // session is going away (lock/sleep), not just a short interruption.
                StopBooking(slot.Key);
            }

            var names = string.Join(", ", active.Select(a => $"\"{a.Title}\""));
            var verb = active.Count == 1 ? "wurde" : "wurden";
            NotificationRequested?.Invoke("Buchung beendet", $"{names} {verb} automatisch beendet ({reason}).");
        }

        private void CheckNudge()
        {
            if (!Properties.Settings.Default.ReminderNudgeEnabled || _isLocked || _isSuspended)
                return;

            if (IsAnyActive)
            {
                _lastActiveOrNudge = DateTime.Now;
                return;
            }

            var intervalMinutes = Math.Max(1, Properties.Settings.Default.ReminderNudgeIntervalMinutes);
            if (DateTime.Now - _lastActiveOrNudge < TimeSpan.FromMinutes(intervalMinutes))
                return;

            _lastActiveOrNudge = DateTime.Now;
            NotificationRequested?.Invoke("Kein Projekt ausgewählt", "Aktuell läuft keine Buchung.");
        }

        /// <summary>
        /// Stops all timers and unsubscribes from Windows session events. Called from
        /// <see cref="App"/>'s shutdown handling since this view model outlives any single view.
        /// </summary>
        public void Shutdown()
        {
            _statusTimer?.Stop();
            _lockAutoStopTimer?.Stop();
            _nudgeCheckTimer?.Stop();
            _sessionMonitor?.Dispose();
        }

        #endregion

        private QuickSelectItemViewModel FindSlot(string key)
        {
            return ActiveSlots.FirstOrDefault(q => q.Key == key);
        }

        /// <summary>
        /// Resumes the paused slot <paramref name="slotKey"/> with a new segment. Unlike a
        /// single-active model, this never touches other slots: several projects can run in
        /// parallel, up to <see cref="MaxActiveSlots"/> at once.
        /// </summary>
        private void ResumeBooking(string slotKey)
        {
            var calledProject = FindSlot(slotKey);
            if (calledProject == null || !calledProject.IsPaused) return;

            var now = DateTime.Now;
            calledProject.Start = now.ToString("HH:mm");
            calledProject.StartedAt = now;
            calledProject.IsPaused = false;

            UpdateActiveState();
        }

        /// <summary>
        /// Fully stops the slot <paramref name="slotKey"/>, freeing it up for another
        /// project. If it's currently running, closes out and persists the running segment
        /// first; if it's paused, that segment was already persisted when it was paused, so
        /// there's nothing left to book.
        /// </summary>
        private void StopBooking(string slotKey)
        {
            var calledProject = FindSlot(slotKey);
            if (calledProject == null) return;

            if (!calledProject.IsPaused)
            {
                calledProject.End = DateTime.Now.ToString("HH:mm");
                UpdateDatabase(calledProject);
            }

            ActiveSlots.Remove(calledProject);

            UpdateActiveState();
        }

        /// <summary>
        /// Pauses the slot <paramref name="slotKey"/>: persists the segment running up to
        /// now just like a stop would, but keeps the slot reserved so it can be resumed with a
        /// single click instead of going through the "start a project" combo box again.
        /// </summary>
        private void PauseBooking(string slotKey)
        {
            var calledProject = FindSlot(slotKey);
            if (calledProject == null || calledProject.IsPaused) return;

            var now = DateTime.Now;
            calledProject.End = now.ToString("HH:mm");
            UpdateDatabase(calledProject);
            calledProject.AccumulateElapsed(now);
            calledProject.StartedAt = null;
            calledProject.IsPaused = true;

            UpdateActiveState();
        }

        private void UpdateDatabase(QuickSelectItemViewModel selectedItem)
        {
            // A pause/switch within the same clock minute (Start == End, both formatted to
            // minute resolution) would otherwise be rejected by the DB layer as a zero-duration
            // booking. Round up to a minimum 1-minute entry instead of losing it.
            if (string.Equals(selectedItem.Start, selectedItem.End, StringComparison.Ordinal))
            {
                selectedItem.End = BumpByOneMinute(selectedItem.End);
            }

            try
            {
                MainViewModel.DbInstance.AddTimeEntry(DateTime.Today, selectedItem.Start, selectedItem.End, selectedItem.SelectedProject, selectedItem.BookingText);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Buchung konnte nicht gespeichert werden", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static string BumpByOneMinute(string hhmm)
        {
            if (!TimeSpan.TryParseExact(hhmm, @"hh\:mm", CultureInfo.InvariantCulture, out var t))
                return hhmm;

            if (t < new TimeSpan(23, 59, 0))
                t = t.Add(TimeSpan.FromMinutes(1));

            return t.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        }
    }
}
