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
    internal class QuickSelectViewModel:BaseViewModel
    {
        public ObservableCollection<QuickSelectItemViewModel> QuickSelectItems { get; set; } = new ObservableCollection<QuickSelectItemViewModel>();

        /// <summary>
        /// Distinct list of the projects that are currently configured in the main window's
        /// Quick Select slots. Serves as the item source for the floating window's "start a
        /// project" combo box, so only projects the user has already set up there can be picked
        /// as one of the (up to <see cref="MaxActiveSlots"/>) parallel-running slots.
        /// </summary>
        public ObservableCollection<string> ConfiguredProjects { get; } = new ObservableCollection<string>();

        /// <summary>
        /// The projects currently running, in parallel, up to <see cref="MaxActiveSlots"/> at a
        /// time. Serves as the item source for the floating window's slot list.
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

            LoadProjectnames();
            UpdateActiveState();

            // Keeps the "running since" elapsed display in the floating window live.
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statusTimer.Tick += (s, e) => UpdateElapsed();
            _statusTimer.Start();

            InitializeSessionAutomation();
        }

        public override void Refresh()
        {

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
                SetProperty(ref _pendingNewProject, value);
                StartNewSlotCommand.RaiseCanExecuteChanged();
            }
        }

        private bool StartNewSlotCanExecute(object obj)
        {
            if (string.IsNullOrWhiteSpace(PendingNewProject)) return false;
            if (ActiveSlots.Count >= MaxActiveSlots) return false;

            var existing = QuickSelectItems.FirstOrDefault(q => q.SelectedProject == PendingNewProject);
            return existing == null || !existing.IsActive;
        }

        private void StartNewSlotExecute(object obj)
        {
            var project = PendingNewProject;
            if (string.IsNullOrWhiteSpace(project)) return;

            // Reuse an existing slot for this project if the user already configured one in the
            // main window's Quick Select list; otherwise create one so it's persisted the same
            // way a manually-added row would be.
            var slot = QuickSelectItems.FirstOrDefault(q => q.SelectedProject == project);
            if (slot == null)
            {
                slot = CreateQuickSelectItem();
                slot.SetSelectedProjectSilently(project);
                QuickSelectItems.Insert(Math.Max(0, QuickSelectItems.Count - 1), slot);
                SaveProjectnames();
            }

            SetBookingInformation(project);
            PendingNewProject = null;
        }

        /// <summary>
        /// Rebuilds <see cref="ConfiguredProjects"/> from the slots in place, so the floating
        /// window's combo box keeps its current selection while stale entries are pruned and
        /// new ones added.
        /// </summary>
        private void UpdateConfiguredProjects()
        {
            var desired = QuickSelectItems
                .Select(q => q.SelectedProject)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct()
                .ToList();

            for (int i = ConfiguredProjects.Count - 1; i >= 0; i--)
            {
                if (!desired.Contains(ConfiguredProjects[i]))
                    ConfiguredProjects.RemoveAt(i);
            }

            foreach (var p in desired)
            {
                if (!ConfiguredProjects.Contains(p))
                    ConfiguredProjects.Add(p);
            }
        }

        /// <summary>
        /// Recomputes the "currently active" display state from the slots: which ones are
        /// running in parallel (up to <see cref="MaxActiveSlots"/>) and whether another one can
        /// still be started.
        /// </summary>
        private void UpdateActiveState()
        {
            var active = QuickSelectItems.Where(q => q.IsActive).ToList();

            for (int i = ActiveSlots.Count - 1; i >= 0; i--)
            {
                if (!active.Contains(ActiveSlots[i]))
                    ActiveSlots.RemoveAt(i);
            }

            foreach (var item in active)
            {
                if (!ActiveSlots.Contains(item))
                    ActiveSlots.Add(item);
            }

            IsAnyActive = active.Count > 0;
            CanStartMoreSlots = active.Count < MaxActiveSlots;

            StartNewSlotCommand.RaiseCanExecuteChanged();
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
            // Snapshot first: SetBookingInformation mutates QuickSelectItems' IsActive flags as
            // it goes, so iterating the live query would skip entries.
            var activeProjects = QuickSelectItems.Where(q => q.IsActive).Select(q => q.SelectedProject).ToList();
            if (activeProjects.Count == 0)
                return;

            foreach (var project in activeProjects)
            {
                // Reuses the same booking-end/persistence logic as the manual per-slot stop
                // button, so there's a single place that ends a booking.
                SetBookingInformation(project);
            }

            var names = string.Join(", ", activeProjects.Select(p => $"\"{p}\""));
            var verb = activeProjects.Count == 1 ? "wurde" : "wurden";
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

        /// <summary>
        /// Starts or stops the booking for <paramref name="selectedProject"/>'s slot. Unlike a
        /// single-active model, this never touches other slots: several projects can run in
        /// parallel, up to <see cref="MaxActiveSlots"/> at once.
        /// </summary>
        private void SetBookingInformation(string selectedProject)
        {
            var now = DateTime.Now;

            var calledProject = QuickSelectItems.FirstOrDefault(q => q.SelectedProject != null && q.SelectedProject.Equals(selectedProject));

            if (calledProject == null)
            {
                return;
            }

            if (calledProject.IsActive)
            {
                calledProject.End = now.ToString("HH:mm");
                calledProject.IsActive = false;
                calledProject.StartedAt = null;

                UpdateDatabase(calledProject);
            }
            else
            {
                var activeCount = QuickSelectItems.Count(q => q.IsActive);
                if (activeCount >= MaxActiveSlots)
                {
                    MessageBox.Show(
                        $"Es können maximal {MaxActiveSlots} Projekte gleichzeitig laufen. Bitte zuerst ein anderes Projekt stoppen.",
                        "Maximal erreicht",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                calledProject.Start = now.ToString("HH:mm");
                calledProject.IsActive = true;
                calledProject.StartedAt = now;
            }

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
                MainViewModel.DbInstance.AddTimeEntry(DateTime.Today, selectedItem.Start, selectedItem.End, selectedItem.SelectedProject, "Quick Select");
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

        /// <summary>
        /// Builds a new slot with the current project list, wired to the shared booking/save/
        /// remove callbacks.
        /// </summary>
        private QuickSelectItemViewModel CreateQuickSelectItem()
        {
            var item = new QuickSelectItemViewModel(SetBookingInformation, SaveProjectnames, RemoveQuickSelectItem);

            foreach (var project in db.GetProjects())
            {
                item.AvailableProjects.Add(project.Projektname);
            }

            return item;
        }

        /// <summary>
        /// Enforces the "one trailing empty row" invariant: any empty row that isn't last is
        /// dropped, and a fresh empty row is appended whenever the last row has been filled in.
        /// </summary>
        private void EnsureTrailingEmptyRow()
        {
            for (int i = QuickSelectItems.Count - 2; i >= 0; i--)
            {
                if (string.IsNullOrWhiteSpace(QuickSelectItems[i].SelectedProject))
                    QuickSelectItems.RemoveAt(i);
            }

            if (QuickSelectItems.Count == 0 || !string.IsNullOrWhiteSpace(QuickSelectItems[QuickSelectItems.Count - 1].SelectedProject))
            {
                QuickSelectItems.Add(CreateQuickSelectItem());
            }
        }

        /// <summary>
        /// Handles the row's delete button: clearing the project lets <see cref="SaveProjectnames"/>
        /// reconcile the row away via <see cref="EnsureTrailingEmptyRow"/>.
        /// </summary>
        private void RemoveQuickSelectItem(QuickSelectItemViewModel item)
        {
            if (item.IsActive) return;

            item.SelectedProject = string.Empty;
        }

        private void SaveProjectnames()
        {
            EnsureTrailingEmptyRow();

            // Only the filled-in slots are persisted; the trailing empty row is re-created on load.
            var selectedProjects = QuickSelectItems
                .Select(item => item.SelectedProject)
                .Where(p => !string.IsNullOrWhiteSpace(p));

            Properties.Settings.Default.QuickSelectProjects = string.Join(";", selectedProjects);
            Properties.Settings.Default.Save();

            // Keep the floating window's project list - and, in case the renamed slot was one of
            // the currently-running ones, its active-slots display - in sync with the slots.
            UpdateConfiguredProjects();
            UpdateActiveState();
        }

        public void LoadProjectnames()
        {
            QuickSelectItems.Clear();

            string serialized = Properties.Settings.Default.QuickSelectProjects;
            var savedProjects = string.IsNullOrEmpty(serialized)
                ? Enumerable.Empty<string>()
                : serialized.Split(';').Where(p => !string.IsNullOrWhiteSpace(p));

            foreach (var project in savedProjects)
            {
                var item = CreateQuickSelectItem();
                item.SetSelectedProjectSilently(project);
                QuickSelectItems.Add(item);
            }

            EnsureTrailingEmptyRow();
            UpdateConfiguredProjects();
            UpdateActiveState();
        }
    }
}
