using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
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

        public override string ToString() => Label;
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

        public ObservableCollection<BackupHelper.BackupFile> AvailableBackups { get; } = new ObservableCollection<BackupHelper.BackupFile>();

        public DelegateCommand SaveCommand { get; }
        public DelegateCommand CancelCommand { get; }
        public DelegateCommand CreateBackupCommand { get; }
        public DelegateCommand RestoreBackupCommand { get; }
        public DelegateCommand BrowseBackupCommand { get; }

        public SettingsViewModel()
        {
            SaveCommand = new DelegateCommand(_ => Save());
            CancelCommand = new DelegateCommand(_ => Refresh());
            CreateBackupCommand = new DelegateCommand(_ => CreateBackup());
            RestoreBackupCommand = new DelegateCommand(param => RestoreAndRestart((param as BackupHelper.BackupFile)?.FullPath));
            BrowseBackupCommand = new DelegateCommand(_ => BrowseBackup());

            Refresh();
        }

        public override void Refresh()
        {
            AutostartEnabled = AutostartHelper.IsEnabled();
            FloatingWindowAlwaysOnTop = Properties.Settings.Default.FloatingWindowAlwaysOnTop;
            SelectedStartupMode = ParseStartupMode(Properties.Settings.Default.StartupMode);

            AvailableBackups.Clear();
            foreach (var backup in BackupHelper.GetAvailableBackups())
            {
                AvailableBackups.Add(backup);
            }
        }

        private void CreateBackup()
        {
            try
            {
                MainViewModel.DbInstance.Checkpoint();
                var path = BackupHelper.CreateBackup();
                Refresh();

                MessageBox.Show(
                    $"Sicherung wurde erstellt:\n{path}",
                    "Sicherung erstellt",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Sicherung konnte nicht erstellt werden:\n{ex.Message}",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void BrowseBackup()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "TimeTrack-Sicherung (*.timetrack-backup)|*.timetrack-backup",
                InitialDirectory = System.IO.Directory.Exists(BackupHelper.BackupFolderPath)
                    ? BackupHelper.BackupFolderPath
                    : null
            };

            if (dialog.ShowDialog() == true)
            {
                RestoreAndRestart(dialog.FileName);
            }
        }

        private void RestoreAndRestart(string backupFilePath)
        {
            if (string.IsNullOrWhiteSpace(backupFilePath))
                return;

            var result = MessageBox.Show(
                "Die aktuelle Datenbank wird durch die ausgewählte Sicherung ersetzt.\nDie Anwendung wird danach neu gestartet.\n\nFortfahren?",
                "Sicherung wiederherstellen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                MainViewModel.DbInstance.Dispose();
                BackupHelper.RestoreBackup(backupFilePath);

                Process.Start(Process.GetCurrentProcess().MainModule.FileName);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Sicherung konnte nicht wiederhergestellt werden:\n{ex.Message}",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
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
