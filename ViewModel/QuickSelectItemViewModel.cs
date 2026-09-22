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
        private readonly Action<string> _updateDatabase;
        private readonly Action _saveProjectnames;
        private readonly Action<QuickSelectItemViewModel> _removeRequested;

        public QuickSelectItemViewModel(Action<string> updateDatabase, Action saveProjectnames, Action<QuickSelectItemViewModel> removeRequested)
        {
            _updateDatabase = updateDatabase;
            _saveProjectnames = saveProjectnames;
            _removeRequested = removeRequested;

            StartTimerCommand = new DelegateCommand(StartTimerCommandExecute, StartTimerCommandCanExecute);
            EndTimerCommand = new DelegateCommand(EndTimerCommandExecute, EndTimerCommandCanExecute);
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
            set => SetProperty(ref _end, value);
        }
        public string Start
        {
            get => _start;
            set => SetProperty(ref _start, value);
        }

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }

        /// <summary>
        /// The exact moment this slot's booking started, used for the elapsed-time display so
        /// it stays correct across midnight instead of re-deriving a start time from today's
        /// date. Null while the slot isn't running.
        /// </summary>
        public DateTime? StartedAt { get; set; }

        private string _elapsedText = "";
        /// <summary>
        /// Live "HH:mm:ss" elapsed time since <see cref="StartedAt"/>, refreshed periodically
        /// by the owning view model via <see cref="RefreshElapsed"/> so multiple slots can run
        /// in parallel, each with their own ticking display.
        /// </summary>
        public string ElapsedText
        {
            get => _elapsedText;
            private set => SetProperty(ref _elapsedText, value);
        }

        public void RefreshElapsed()
        {
            if (StartedAt == null)
            {
                ElapsedText = "";
                return;
            }

            var elapsed = DateTime.Now - StartedAt.Value;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;

            ElapsedText = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        }

        public DelegateCommand StartTimerCommand { get; set; }
        public DelegateCommand EndTimerCommand { get; set; }
        public DelegateCommand RemoveCommand { get; set; }


        public void StartTimerCommandExecute(object obj)
        {
            //Start = DateTime.Now.ToString("HH:mm");
            //IsActive = true;
            _updateDatabase.Invoke(SelectedProject);
        }

        public bool StartTimerCommandCanExecute(object obj)
        {
            return !IsActive;
        }

        public void EndTimerCommandExecute(object obj)
        {
            //End = DateTime.Now.ToString("HH:mm");
            //IsActive = false;
            _updateDatabase.Invoke(SelectedProject);
        }

        public bool EndTimerCommandCanExecute(object obj)
        {
            return IsActive;
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
