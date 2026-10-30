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
using Zeitmanagement.Helpers;
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
        private readonly BaseViewModel _sapBookingView;
        private readonly BaseViewModel _settingsView;
        private readonly HelpViewModel _helpView;

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

        /// <summary>The "Hilfe &amp; Neuigkeiten" page, also bound by the navigation for its "Neu" badge.</summary>
        public HelpViewModel Help => _helpView;

        private string _clock;
        public string Clock
        {
            get => _clock;
            set => SetProperty(ref _clock, value);
        }

        private string _today;
        public string Today
        {
            get => _today;
            set => SetProperty(ref _today, value);
        }

        private string _currentView;
        /// <summary>Key of the page currently shown (as passed to <see cref="SelectViewCommand"/>, lower case).</summary>
        public string CurrentView
        {
            get => _currentView;
            private set => SetProperty(ref _currentView, value);
        }

        /// <summary>Switches the current theme between its light and dark variant.</summary>
        public DelegateCommand ToggleDarkModeCommand { get; }

        private readonly DispatcherTimer _timer;

        public MainViewModel()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version.ToString();

            WindowTitle = "TimeTrack v" + version;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += (s, e) => UpdateClock();
            _timer.Start();
            UpdateClock();


            _dashboardView = new DashboardViewModel(OpenDay);
            _projectView = new ProjectsViewModel();
            _entriesView = new EntriesViewModel();
            _quickSelectView = new QuickSelectViewModel();
            _fragmentationView = new FragmentationViewModel();
            _sapBookingView = new SapBookingViewModel();
            _settingsView = new SettingsViewModel();
            _helpView = new HelpViewModel(SelectViewCommandExecute);

            SelectViewCommandExecute("dashboard");

            SelectViewCommand = new DelegateCommand(SelectViewCommandExecute);
            ToggleDarkModeCommand = new DelegateCommand(_ => ToggleDarkMode());

            GetDbProjects();
        }

        private void UpdateClock()
        {
            var now = DateTime.Now;
            Clock = now.ToString("HH:mm:ss");
            Today = now.ToString("dddd, d. MMMM");
        }

        private static void ToggleDarkMode()
        {
            var settings = Properties.Settings.Default;
            settings.ThemeMode = (ThemeManager.IsDark ? ThemeMode.Light : ThemeMode.Dark).ToString();
            settings.Save();
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
            CurrentView = mode;

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
                case "sap":
                    RightSide = _sapBookingView;
                    break;
                case "settings":
                    RightSide = _settingsView;
                    break;
                case "help":
                    RightSide = _helpView;
                    break;
                default:
                    break;
            }

            ((BaseViewModel)RightSide).Refresh();
        }

        /// <summary>Öffnet „Hilfe &amp; Neuigkeiten“ direkt bei den Neuigkeiten (aus dem Update-Hinweis heraus).</summary>
        public void OpenChangelog()
        {
            _helpView.ShowChangelog();
            SelectViewCommandExecute("help");
        }

        /// <summary>
        /// Öffnet den angegebenen Tag in der Stundenbuchungs-Ansicht (aus der Dashboard-Heatmap heraus).
        /// </summary>
        private void OpenDay(DateTime date)
        {
            ((EntriesViewModel)_entriesView).SelectedDate = date.ToString("dd.MM.yyyy");
            SelectViewCommandExecute("hours");
        }

    }
}
