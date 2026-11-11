using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using Zeitmanagement.Helpers.Plugins;
using Zeitmanagement.MVVM;

namespace Zeitmanagement.ViewModel
{
    /// <summary>Der Abschnitt „Plugins“ der Einstellungen: gefundene Plugins, Aktivierung, Fehler, Einstellungsbereiche.</summary>
    internal sealed class PluginSettingsViewModel : BindableBase
    {
        private readonly PluginManager _manager;

        public PluginSettingsViewModel(PluginManager manager)
        {
            _manager = manager;
            Items = new ObservableCollection<PluginItemViewModel>(
                (manager?.Plugins ?? new PluginInfo[0]).Select(info => new PluginItemViewModel(manager, info, OnEnabledChanged)));
            OpenFolderCommand = new DelegateCommand(_ => OpenFolder());
            RestartCommand = new DelegateCommand(_ => Restart());
            RescanCommand = new DelegateCommand(_ => Rescan(showResult: true));
        }

        public ObservableCollection<PluginItemViewModel> Items { get; }

        public bool HasNoPlugins => Items.Count == 0;

        public DelegateCommand RescanCommand { get; }

        private string _rescanText;
        /// <summary>Ergebnis von „Nach Plugins suchen“, z. B. „1 neues Plugin gefunden.“</summary>
        public string RescanText
        {
            get => _rescanText;
            private set => SetProperty(ref _rescanText, value);
        }

        /// <summary>
        /// Durchsucht den Plugin-Ordner erneut (auch beim Öffnen der Einstellungen). Einträge unveränderter Plugins bleiben
        /// erhalten, damit ihre Einstellungsbereiche nicht neu aufgebaut werden.
        /// </summary>
        public void Rescan(bool showResult)
        {
            if (_manager == null)
                return;

            var result = _manager.Rescan();
            var previous = Items.ToList();
            Items.Clear();
            foreach (var info in _manager.Plugins)
            {
                var existing = previous.FirstOrDefault(item => ReferenceEquals(item.Info, info));
                if (existing != null)
                {
                    existing.Refresh();
                    Items.Add(existing);
                }
                else
                {
                    Items.Add(new PluginItemViewModel(_manager, info, OnEnabledChanged));
                }
            }

            RaisePropertyChanged(nameof(HasNoPlugins));
            RaisePropertyChanged(nameof(RestartRequired));

            if (showResult)
                RescanText = DescribeRescan(result, Items.Any(i => i.Info.UpdateOnDisk || i.Info.IsMissing));
        }

        private static string DescribeRescan(PluginRescanResult result, bool changedOnDisk)
        {
            var parts = new List<string>();
            if (result.Added == 1) parts.Add("1 neues Plugin gefunden");
            else if (result.Added > 1) parts.Add(result.Added + " neue Plugins gefunden");
            if (result.Removed > 0) parts.Add(result.Removed + " entfernt");
            if (changedOnDisk) parts.Add("geänderte Dateien bei laufenden Plugins");

            var text = parts.Count == 0 ? "Keine Änderungen im Plugin-Ordner" : string.Join(", ", parts);
            return text + $" ({result.Total} Plugin{(result.Total == 1 ? "" : "s")} insgesamt), Stand {DateTime.Now:HH:mm}.";
        }

        public string FolderPath => _manager?.PluginsRoot ?? PluginLoader.DefaultPluginsRoot;

        public bool RestartRequired => _manager != null && _manager.RestartRequired;

        public DelegateCommand OpenFolderCommand { get; }

        public DelegateCommand RestartCommand { get; }

        private void OnEnabledChanged()
        {
            RaisePropertyChanged(nameof(RestartRequired));
        }

        private void OpenFolder()
        {
            try
            {
                _manager?.OpenPluginsFolder();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Der Plugin-Ordner konnte nicht geöffnet werden:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static void Restart()
        {
            Process.Start(Process.GetCurrentProcess().MainModule.FileName);
            Application.Current.Shutdown();
        }
    }

    /// <summary>Ein Plugin in den Einstellungen.</summary>
    internal sealed class PluginItemViewModel : BindableBase
    {
        private readonly PluginManager _manager;
        private readonly PluginInfo _info;
        private readonly Action _enabledChanged;

        public PluginItemViewModel(PluginManager manager, PluginInfo info, Action enabledChanged)
        {
            _manager = manager;
            _info = info;
            _enabledChanged = enabledChanged;
            Sections = manager.SettingsSectionsOf(info)
                .Select(section => new PluginSettingsSectionViewModel(section, info.DisplayName))
                .ToList();
        }

        public PluginInfo Info => _info;

        /// <summary>Nach erneutem Suchen: Anzeige aus dem (aktualisierten) <see cref="PluginInfo"/> neu lesen.</summary>
        public void Refresh()
        {
            RaisePropertyChanged(nameof(Name));
            RaisePropertyChanged(nameof(VersionText));
            RaisePropertyChanged(nameof(Description));
            RaisePropertyChanged(nameof(HasDescription));
            RaisePropertyChanged(nameof(StatusText));
            RaisePropertyChanged(nameof(IsEnabled));
        }

        public string Name => _info.DisplayName;

        public string VersionText => string.IsNullOrEmpty(_info.VersionText) ? null : "Version " + _info.VersionText;

        public string Description => _info.Description;

        public bool HasDescription => !string.IsNullOrWhiteSpace(_info.Description);

        public string Error => _info.Error;

        public bool HasError => _info.State == PluginState.Failed && !string.IsNullOrEmpty(_info.Error);

        public string StatusText
        {
            get
            {
                var running = _info.State == PluginState.Loaded;
                if (running && _info.IsMissing)
                    return "Aktiv bis zum nächsten Start; der Plugin-Ordner wurde entfernt";
                if (running && _info.IsEnabled && _info.UpdateOnDisk)
                    return "Aktiv; im Ordner liegt eine neue Fassung" +
                           (string.IsNullOrEmpty(_info.VersionOnDisk) ? "" : " (Version " + _info.VersionOnDisk + ")") +
                           ", sie wird beim nächsten Start geladen";
                if (_info.IsEnabled && !running && _info.State == PluginState.Disabled)
                    return "Wird beim nächsten Start geladen";
                if (!_info.IsEnabled && running)
                    return "Aktiv bis zum nächsten Start, danach deaktiviert";
                switch (_info.State)
                {
                    case PluginState.Loaded: return "Aktiv";
                    case PluginState.Failed: return _info.IsEnabled ? "Fehler beim Laden" : "Deaktiviert";
                    default: return "Deaktiviert";
                }
            }
        }

        public IReadOnlyList<PluginSettingsSectionViewModel> Sections { get; }

        public bool HasSections => Sections.Count > 0;

        /// <summary>
        /// Aktivieren fragt vorher nach, weil ein Plugin mit allen Rechten von TimeTrack läuft. Die Wahl wird sofort
        /// gespeichert und wirkt nach einem Neustart.
        /// </summary>
        public bool IsEnabled
        {
            get => _info.IsEnabled;
            set
            {
                if (value == _info.IsEnabled)
                    return;

                if (value)
                {
                    var answer = MessageBox.Show(
                        $"„{Name}“ aktivieren?\n\nEin Plugin läuft mit allen Rechten von TimeTrack und kann auf deine Buchungen zugreifen. Aktiviere nur Plugins aus einer Quelle, der du vertraust.\n\nDas Plugin wird beim nächsten Start von TimeTrack geladen.",
                        "Plugin aktivieren",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    if (answer != MessageBoxResult.Yes)
                    {
                        // Der Schalter ist schon umgelegt; nach dem laufenden Binding-Update zurücksetzen.
                        Application.Current?.Dispatcher.BeginInvoke(new Action(() => RaisePropertyChanged(nameof(IsEnabled))));
                        return;
                    }
                }

                _manager.SetEnabled(_info, value);
                RaisePropertyChanged(nameof(IsEnabled));
                RaisePropertyChanged(nameof(StatusText));
                _enabledChanged?.Invoke();
            }
        }
    }

    /// <summary>Ein Einstellungsbereich eines Plugins; seine Ansicht entsteht beim ersten Anzeigen.</summary>
    internal sealed class PluginSettingsSectionViewModel
    {
        private readonly TimeTrack.Plugins.PluginSettingsSection _section;
        private readonly string _pluginName;
        private FrameworkElement _view;

        public PluginSettingsSectionViewModel(TimeTrack.Plugins.PluginSettingsSection section, string pluginName)
        {
            _section = section;
            _pluginName = pluginName;
        }

        public string Title => _section.Title;

        public FrameworkElement View => _view ?? (_view = PluginViewFactory.Create(_section.CreateView, _pluginName));
    }
}
