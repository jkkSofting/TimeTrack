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

        public QuickSelectItemViewModel(Action<string> updateDatabase, Action saveProjectnames)
        {
            _updateDatabase = updateDatabase;
            _saveProjectnames = saveProjectnames;

            StartTimerCommand = new DelegateCommand(StartTimerCommandExecute, StartTimerCommandCanExecute);
            EndTimerCommand = new DelegateCommand(EndTimerCommandExecute, EndTimerCommandCanExecute);
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

        public bool IsActive;

        public DelegateCommand StartTimerCommand { get; set; }
        public DelegateCommand EndTimerCommand { get; set; }


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
    }
}
