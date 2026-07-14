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
        private readonly BaseViewModel _fragmentationView;
        private readonly BaseViewModel _settingsView;

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

        /// <summary>
        /// Exposes the shared Quick Select view model so the app-startup logic can open the
        /// floating window against the same instance the Quick Select view is bound to.
        /// </summary>
        public QuickSelectViewModel QuickSelectViewModel => (QuickSelectViewModel)_quickSelectView;

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
            _fragmentationView = new FragmentationViewModel();
            _settingsView = new SettingsViewModel();

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
                case "fragmentation":
                    RightSide = _fragmentationView;
                    break;
                case "settings":
                    RightSide = _settingsView;
                    break;
                default:
                    break;
            }

            ((BaseViewModel)RightSide).Refresh();
        }

    }
}
