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
using Zeitmanagement.Helpers.Plugins;
using Zeitmanagement.MVVM;

namespace Zeitmanagement.ViewModel
{
    internal class MainViewModel:BindableBase
    {
        private readonly BaseViewModel _dashboardView;
        private readonly BaseViewModel _projectView;
        private readonly BaseViewModel _entriesView;
        private readonly QuickSelectViewModel _quickSelect;
        private readonly BaseViewModel _fragmentationView;
        private readonly BaseViewModel _sapBookingView;
        private readonly ProjectControllingViewModel _controllingView;
        private readonly ExportViewModel _exportView;
        private readonly TodoViewModel _todoView;
        private readonly NotesViewModel _notesView;
        private readonly BaseViewModel _settingsView;
        private readonly HelpViewModel _helpView;
        private readonly PluginManager _plugins;
        private readonly Dictionary<string, PluginPageViewModel> _pluginPages = new Dictionary<string, PluginPageViewModel>();

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
        /// The view model behind the floating Quick Switch window. It has no page of its own but
        /// has to outlive any single window, since it tracks the running bookings and the
        /// lock/sleep automation.
        /// </summary>
        public QuickSelectViewModel QuickSelectViewModel => _quickSelect;

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

        /// <summary>The plugins of this session (loaded at start, shut down by <see cref="App"/> on exit).</summary>
        public PluginManager Plugins => _plugins;

        /// <summary>Navigation entries of the plugin pages, shown in their own group below the built-in pages.</summary>
        public ObservableCollection<PluginNavigationItem> PluginNavigationItems { get; } = new ObservableCollection<PluginNavigationItem>();

        public bool HasPluginPages => PluginNavigationItems.Count > 0;

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
            // New/renamed/deleted/(de)activated projects must show up in the Quick Switch window
            // right away, not only after the next start.
            _controllingView = new ProjectControllingViewModel(OpenDay);
            _projectView = new ProjectsViewModel(() => QuickSelectViewModel.ReloadProjects(), OpenControlling, OpenExport);
            _entriesView = new EntriesViewModel();
            _quickSelect = new QuickSelectViewModel();
            _fragmentationView = new FragmentationViewModel();
            _sapBookingView = new SapBookingViewModel();
            _exportView = new ExportViewModel();
            _todoView = new TodoViewModel(() => QuickSelectViewModel.ReloadTickets());
            _notesView = new NotesViewModel();

            // Plugins nach den festen Seiten laden: Ein fehlerhaftes Plugin darf den Start nicht verhindern.
            _plugins = new PluginManager(DbInstance) { NavigateHandler = SelectViewCommandExecute };
            _plugins.LoadAll();
            foreach (var page in _plugins.Pages)
            {
                _pluginPages[page.NavigationKey] = new PluginPageViewModel(page);
                PluginNavigationItems.Add(new PluginNavigationItem(page));
            }

            _settingsView = new SettingsViewModel(_plugins);
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
                case "fragmentation":
                    RightSide = _fragmentationView;
                    break;
                case "sap":
                    RightSide = _sapBookingView;
                    break;
                case "controlling":
                    RightSide = _controllingView;
                    break;
                case "export":
                    RightSide = _exportView;
                    break;
                case "todo":
                    RightSide = _todoView;
                    break;
                case "notes":
                    RightSide = _notesView;
                    break;
                case "settings":
                    RightSide = _settingsView;
                    break;
                case "help":
                    RightSide = _helpView;
                    break;
                default:
                    if (_pluginPages.TryGetValue(mode, out var pluginPage))
                        RightSide = pluginPage;
                    break;
            }

            foreach (var item in PluginNavigationItems)
                item.IsSelected = item.Key == mode;

            ((BaseViewModel)RightSide).Refresh();
        }

        /// <summary>Speichert noch ausstehende Eingaben (die Notizen speichern kurz nach dem Tippen); beim Beenden.</summary>
        public void FlushPending()
        {
            _notesView.Flush();
        }

        /// <summary>Öffnet „Hilfe &amp; Neuigkeiten“ direkt bei den Neuigkeiten (aus dem Update-Hinweis heraus).</summary>
        public void OpenChangelog()
        {
            _helpView.ShowChangelog();
            SelectViewCommandExecute("help");
        }

        /// <summary>
        /// Öffnet „Projektcontrolling“ mit dem PSP-Element, das der Kostenträger enthält (aus der Projektseite heraus).
        /// </summary>
        private void OpenControlling(string kostentraeger)
        {
            SelectViewCommandExecute("controlling");
            _controllingView.FocusCarrier(kostentraeger);
        }

        /// <summary>
        /// Öffnet den „Tätigkeitsnachweis“ mit genau diesem Projekt gewählt (aus der Projektseite heraus).
        /// </summary>
        private void OpenExport(string projektname)
        {
            _exportView.SelectOnly(projektname);
            SelectViewCommandExecute("export");
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
