using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
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
            MainViewModel.DbInstance.AddTimeEntry(DateTime.Today, selectedItem.Start, selectedItem.End, selectedItem.SelectedProject, "Quick Select");
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
