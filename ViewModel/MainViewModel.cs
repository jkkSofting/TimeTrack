using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Threading;
using Zeitmanagement.MVVM;

namespace Zeitmanagement.ViewModel
{
    internal class MainViewModel:BindableBase
    {
        private readonly BaseViewModel _dashboardView;
        private readonly BaseViewModel _projectView;
        private readonly BaseViewModel _entriesView;
        private readonly BaseViewModel _quickSelectView;

        private object _rightSide;

        private string _windowTitle;

        private static readonly Lazy<TimeTracker> instance =
            new Lazy<TimeTracker>(() => new TimeTracker());
        public static TimeTracker DbInstance => instance.Value;

        public static ObservableCollection<string> Projects = new ObservableCollection<string>();

        public object RightSide
        {
            get => _rightSide;
            set => SetProperty(ref _rightSide, value);
        }

        private string _clock;
        public string Clock
        {
            get => _clock;
            set => SetProperty(ref _clock, value);
        }

        private readonly DispatcherTimer _timer;

        public MainViewModel()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version.ToString();

            WindowTitle = "TimeTrack v" + version;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += (s, e) => Clock = DateTime.Now.ToLongTimeString();
            _timer.Start();


            _dashboardView = new DashboardViewModel();
            _projectView = new ProjectsViewModel();
            _entriesView = new EntriesViewModel();
            _quickSelectView = new QuickSelectViewModel();

            SelectViewCommandExecute("dashboard");

            SelectViewCommand = new DelegateCommand(SelectViewCommandExecute);

            GetDbProjects();
        }

        private static void GetDbProjects()
        {
            var projects = DbInstance.GetProjects();
            Projects.Clear();
            foreach (var project in projects)
            {
                Projects.Add(project.Projektname);
            }
        }

        public DelegateCommand SelectViewCommand{ get; set; }
        public string WindowTitle
        {
            get => _windowTitle;
            set => SetProperty(ref _windowTitle, value);
        }

        private void SelectViewCommandExecute(object obj)
        {
            var mode = obj.ToString().ToLower();

            switch (mode)
            {
                case "dashboard":
                    RightSide = _dashboardView;
                    break;
                case "hours":
                    RightSide = _entriesView;
                    break;
                case "projects":
                    RightSide = _projectView;
                    break;
                case "quickselect":
                    RightSide = _quickSelectView;
                    break;  
                default:
                    break;
            }

            ((BaseViewModel)RightSide).Refresh();
        }

    }
}
