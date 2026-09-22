using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
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

        private bool _autoStopOnLockEnabled;
        public bool AutoStopOnLockEnabled
        {
            get => _autoStopOnLockEnabled;
            set => SetProperty(ref _autoStopOnLockEnabled, value);
        }

        private int _autoStopOnLockMinutes;
        public int AutoStopOnLockMinutes
        {
            get => _autoStopOnLockMinutes;
            set => SetProperty(ref _autoStopOnLockMinutes, Math.Max(1, value));
        }

        private bool _reminderNudgeEnabled;
        public bool ReminderNudgeEnabled
        {
            get => _reminderNudgeEnabled;
            set => SetProperty(ref _reminderNudgeEnabled, value);
        }

        private int _reminderNudgeIntervalMinutes;
        public int ReminderNudgeIntervalMinutes
        {
            get => _reminderNudgeIntervalMinutes;
            set => SetProperty(ref _reminderNudgeIntervalMinutes, Math.Max(1, value));
        }

        private bool _autoUpdateCheckEnabled;
        public bool AutoUpdateCheckEnabled
        {
            get => _autoUpdateCheckEnabled;
            set => SetProperty(ref _autoUpdateCheckEnabled, value);
        }

        public string CurrentVersionText { get; } =
            Assembly.GetExecutingAssembly().GetName().Version.ToString();

        private string _updateStatusText = "Noch nicht geprüft.";
        public string UpdateStatusText
        {
            get => _updateStatusText;
            set => SetProperty(ref _updateStatusText, value);
        }

        private bool _isUpdateAvailable;
        public bool IsUpdateAvailable
        {
            get => _isUpdateAvailable;
            set => SetProperty(ref _isUpdateAvailable, value);
        }

        private bool _isCheckingForUpdate;
        public bool IsCheckingForUpdate
        {
            get => _isCheckingForUpdate;
            set => SetProperty(ref _isCheckingForUpdate, value);
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
        public DelegateCommand CheckForUpdateCommand { get; }
        public DelegateCommand InstallUpdateCommand { get; }

        public SettingsViewModel()
        {
            SaveCommand = new DelegateCommand(_ => Save());
            CancelCommand = new DelegateCommand(_ => Refresh());
            CreateBackupCommand = new DelegateCommand(_ => CreateBackup());
            RestoreBackupCommand = new DelegateCommand(param => RestoreAndRestart((param as BackupHelper.BackupFile)?.FullPath));
            BrowseBackupCommand = new DelegateCommand(_ => BrowseBackup());
            CheckForUpdateCommand = new DelegateCommand(async _ => await CheckForUpdate());
            InstallUpdateCommand = new DelegateCommand(async _ => await InstallUpdate());

            UpdateService.UpdateAvailable += _ => ShowUpdate(UpdateService.LatestUpdate);

            Refresh();
        }

        public override void Refresh()
        {
            AutostartEnabled = AutostartHelper.IsEnabled();
            FloatingWindowAlwaysOnTop = Properties.Settings.Default.FloatingWindowAlwaysOnTop;
            SelectedStartupMode = ParseStartupMode(Properties.Settings.Default.StartupMode);
            AutoStopOnLockEnabled = Properties.Settings.Default.AutoStopOnLockEnabled;
            AutoStopOnLockMinutes = Properties.Settings.Default.AutoStopOnLockMinutes;
            ReminderNudgeEnabled = Properties.Settings.Default.ReminderNudgeEnabled;
            ReminderNudgeIntervalMinutes = Properties.Settings.Default.ReminderNudgeIntervalMinutes;
            AutoUpdateCheckEnabled = Properties.Settings.Default.AutoUpdateCheckEnabled;
            ShowUpdate(UpdateService.LatestUpdate);

            AvailableBackups.Clear();
            foreach (var backup in BackupHelper.GetAvailableBackups())
            {
                AvailableBackups.Add(backup);
            }
        }

        private void ShowUpdate(UpdateChecker.UpdateInfo update)
        {
            IsUpdateAvailable = update != null;
            UpdateStatusText = update != null
                ? $"Version {update.VersionText} ist verfügbar."
                : "Sie verwenden die aktuelle Version.";
        }

        private async Task CheckForUpdate()
        {
            IsCheckingForUpdate = true;
            UpdateStatusText = "Suche nach Updates...";

            var update = await UpdateService.CheckNowAsync();
            ShowUpdate(update);

            IsCheckingForUpdate = false;
        }

        private async Task InstallUpdate()
        {
            var update = UpdateService.LatestUpdate;
            if (update == null)
                return;

            var result = MessageBox.Show(
                $"TimeTrack {update.VersionText} wird heruntergeladen und installiert. Die Anwendung wird dazu beendet.\n\nFortfahren?",
                "Update installieren",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                UpdateStatusText = "Update wird heruntergeladen...";
                IsCheckingForUpdate = true;

                await UpdateService.DownloadAndInstallAsync(update);
            }
            catch (Exception ex)
            {
                IsCheckingForUpdate = false;
                UpdateStatusText = "Update fehlgeschlagen.";

                MessageBox.Show(
                    $"Update konnte nicht installiert werden:\n{ex.Message}",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
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
            Properties.Settings.Default.AutoStopOnLockEnabled = AutoStopOnLockEnabled;
            Properties.Settings.Default.AutoStopOnLockMinutes = AutoStopOnLockMinutes;
            Properties.Settings.Default.ReminderNudgeEnabled = ReminderNudgeEnabled;
            Properties.Settings.Default.ReminderNudgeIntervalMinutes = ReminderNudgeIntervalMinutes;
            Properties.Settings.Default.AutoUpdateCheckEnabled = AutoUpdateCheckEnabled;
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
