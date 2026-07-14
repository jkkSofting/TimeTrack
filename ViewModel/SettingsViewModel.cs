using System;
using System.Collections.Generic;
using System.Windows;
using Zeitmanagement.Helpers;
using Zeitmanagement.MVVM;

namespace Zeitmanagement.ViewModel
{
    internal class StartupModeOption
    {
        public StartupMode Value { get; }
        public string Label { get; }

        public StartupModeOption(StartupMode value, string label)
        {
            Value = value;
            Label = label;
        }
    }

    internal class SettingsViewModel : BaseViewModel
    {
        private bool _autostartEnabled;
        public bool AutostartEnabled
        {
            get => _autostartEnabled;
            set => SetProperty(ref _autostartEnabled, value);
        }

        private StartupMode _selectedStartupMode;
        public StartupMode SelectedStartupMode
        {
            get => _selectedStartupMode;
            set => SetProperty(ref _selectedStartupMode, value);
        }

        private bool _floatingWindowAlwaysOnTop;
        public bool FloatingWindowAlwaysOnTop
        {
            get => _floatingWindowAlwaysOnTop;
            set => SetProperty(ref _floatingWindowAlwaysOnTop, value);
        }

        public IReadOnlyList<StartupModeOption> StartupModeOptions { get; } = new List<StartupModeOption>
        {
            new StartupModeOption(StartupMode.Normal, "Normal starten"),
            new StartupModeOption(StartupMode.NormalWithFloating, "Normal starten, mit Schnellwechsel-Fenster"),
            new StartupModeOption(StartupMode.Minimized, "Minimiert starten"),
            new StartupModeOption(StartupMode.MinimizedWithFloating, "Minimiert starten, mit Schnellwechsel-Fenster"),
        };

        public DelegateCommand SaveCommand { get; }
        public DelegateCommand CancelCommand { get; }

        public SettingsViewModel()
        {
            SaveCommand = new DelegateCommand(_ => Save());
            CancelCommand = new DelegateCommand(_ => Refresh());

            Refresh();
        }

        public override void Refresh()
        {
            AutostartEnabled = AutostartHelper.IsEnabled();
            FloatingWindowAlwaysOnTop = Properties.Settings.Default.FloatingWindowAlwaysOnTop;
            SelectedStartupMode = ParseStartupMode(Properties.Settings.Default.StartupMode);
        }

        private static StartupMode ParseStartupMode(string value)
        {
            return Enum.TryParse(value, out StartupMode mode) ? mode : StartupMode.Normal;
        }

        private void Save()
        {
            Properties.Settings.Default.StartupMode = SelectedStartupMode.ToString();
            Properties.Settings.Default.FloatingWindowAlwaysOnTop = FloatingWindowAlwaysOnTop;
            Properties.Settings.Default.Save();

            try
            {
                AutostartHelper.SetEnabled(AutostartEnabled);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Autostart konnte nicht {(AutostartEnabled ? "aktiviert" : "deaktiviert")} werden:\n{ex.Message}",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
}
