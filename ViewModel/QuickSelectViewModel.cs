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
        /// Distinct list of the projects that are currently configured in the quick-select
        /// slots. Serves as the item source for the floating window's combo box so the user
        /// can switch quickly between the projects they use.
        /// </summary>
        public ObservableCollection<string> ConfiguredProjects { get; } = new ObservableCollection<string>();

        TimeTracker db;

        private readonly DispatcherTimer _statusTimer;

        public DelegateCommand StopActiveCommand { get; }

        /// <summary>
        /// Raised when the user should be notified via a tray balloon: an auto-stop happened,
        /// or no booking has been running for a while. (title, message)
        /// </summary>
        public event Action<string, string> NotificationRequested;

        public QuickSelectViewModel()
        {
            db = MainViewModel.DbInstance;

            StopActiveCommand = new DelegateCommand(StopActiveExecute, _ => IsAnyActive);

            QuickSelectItems.Clear();
            for (int i = 0; i < 10; i++)
            {
                var item = new QuickSelectItemViewModel(SetBookingInformation, SaveProjectnames);

                var availableProjects = db.GetProjects();

                foreach (var project in availableProjects)
                {
                    item.AvailableProjects.Add(project.Projektname);
                }

                QuickSelectItems.Add(item);

            }

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

        private string _activeProjectName;
        /// <summary>
        /// The project whose timer is currently running. Assigning a new value (e.g. from the
        /// floating window's combo box) switches the running booking to that project.
        /// </summary>
        public string ActiveProjectName
        {
            get => _activeProjectName;
            set
            {
                if (string.Equals(_activeProjectName, value)) return;

                if (!string.IsNullOrEmpty(value))
                {
                    // Switch the running booking to the chosen project.
                    SetBookingInformation(value);
                }
                else
                {
                    // Selection was merely cleared in the UI - resync, don't change bookings.
                    UpdateActiveState();
                }
            }
        }

        private bool _isAnyActive;
        public bool IsAnyActive
        {
            get => _isAnyActive;
            private set => SetProperty(ref _isAnyActive, value);
        }

        private string _activeStatusText = "Keine Buchung aktiv";
        public string ActiveStatusText
        {
            get => _activeStatusText;
            private set => SetProperty(ref _activeStatusText, value);
        }

        private string _activeSinceText = "";
        public string ActiveSinceText
        {
            get => _activeSinceText;
            private set => SetProperty(ref _activeSinceText, value);
        }

        private string _activeElapsedText = "";
        public string ActiveElapsedText
        {
            get => _activeElapsedText;
            private set => SetProperty(ref _activeElapsedText, value);
        }

        // The exact moment the current booking started, used for the elapsed-time display so it
        // stays correct across midnight instead of re-deriving a start time from today's date.
        private DateTime? _activeStartedAt;

        private void StopActiveExecute(object obj)
        {
            var active = QuickSelectItems.FirstOrDefault(q => q.IsActive);
            if (active == null)
            {
                return;
            }

            // Route through the same booking-switch logic the Start/Stop buttons and the
            // floating window's combo box use, so there's a single place that ends a booking.
            SetBookingInformation(active.SelectedProject);
        }

        /// <summary>
        /// Rebuilds <see cref="ConfiguredProjects"/> from the slots in place, so the combo box
        /// keeps its current selection while stale entries are pruned and new ones added.
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
        /// Recomputes the "currently active" display state from the slots.
        /// </summary>
        private void UpdateActiveState()
        {
            var active = QuickSelectItems.FirstOrDefault(q => q.IsActive);

            // Update the backing field directly (not via the property setter) to avoid
            // re-triggering a booking switch.
            SetProperty(ref _activeProjectName, active?.SelectedProject, nameof(ActiveProjectName));

            IsAnyActive = active != null;
            ActiveStatusText = active != null ? active.SelectedProject : "Keine Buchung aktiv";
            ActiveSinceText = active != null && !string.IsNullOrEmpty(active.Start) ? $"seit {active.Start}" : "";

            StopActiveCommand.RaiseCanExecuteChanged();
            UpdateElapsed();
        }

        private void UpdateElapsed()
        {
            if (_activeStartedAt == null)
            {
                ActiveElapsedText = "";
                return;
            }

            var elapsed = DateTime.Now - _activeStartedAt.Value;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;

            ActiveElapsedText = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
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
            var active = QuickSelectItems.FirstOrDefault(q => q.IsActive);
            if (active == null)
                return;

            var project = active.SelectedProject;

            // Reuses the same booking-end/persistence logic as the manual "Buchung stoppen"
            // button and the floating window's combo box.
            StopActiveExecute(null);

            NotificationRequested?.Invoke("Buchung beendet", $"\"{project}\" wurde automatisch beendet ({reason}).");
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

        private void SetBookingInformation(string selectedProject)
        {
            var now = DateTime.Now;

            var calledProject = QuickSelectItems.FirstOrDefault(q => q.SelectedProject != null && q.SelectedProject.Equals(selectedProject));

            if (calledProject == null)
            {
                return;
            }

            var lastActiveProject = QuickSelectItems.LastOrDefault(q => q.IsActive && !q.SelectedProject.Equals(selectedProject));

            if (lastActiveProject != null)
            {
                //Do stuff to end last active project
                lastActiveProject.End = now.ToString("HH:mm");
                lastActiveProject.IsActive = false;

                UpdateDatabase(lastActiveProject);

                calledProject.Start = now.ToString("HH:mm");
                calledProject.IsActive = true;
                _activeStartedAt = now;
            }
            else
            {
                if (calledProject.IsActive)
                {
                    calledProject.End = now.ToString("HH:mm");
                    calledProject.IsActive = false;
                    _activeStartedAt = null;

                    UpdateDatabase(calledProject);
                }
                else
                {
                    calledProject.Start = now.ToString("HH:mm");
                    calledProject.IsActive = true;
                    _activeStartedAt = now;
                }
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

        private void SaveProjectnames()
        {
            // Save the currently selected project names for each QuickSelectItemViewModel
            // We'll use Application Settings for persistence (Properties.Settings.Default)
            // Store as a semicolon-separated string

            var selectedProjects = QuickSelectItems
                .Select(item => item.SelectedProject ?? string.Empty)
                .ToArray();

            string serialized = string.Join(";", selectedProjects);

            // Save to settings (ensure you have a string property named QuickSelectProjects in your settings)
            Properties.Settings.Default.QuickSelectProjects = serialized;
            Properties.Settings.Default.Save();

            // Keep the floating window's quick-switch list - and, in case the renamed slot was
            // the one currently running, its active-booking display - in sync with the slots.
            UpdateConfiguredProjects();
            UpdateActiveState();
        }

        public void LoadProjectnames()
        {
            // Load the saved project names and assign them to the QuickSelectItems
            string serialized = Properties.Settings.Default.QuickSelectProjects;
            if (string.IsNullOrEmpty(serialized))
                return;

            var selectedProjects = serialized.Split(';');
            for (int i = 0; i < QuickSelectItems.Count && i < selectedProjects.Length; i++)
            {
                QuickSelectItems[i].SelectedProject = selectedProjects[i];
            }
        }
    }
}
