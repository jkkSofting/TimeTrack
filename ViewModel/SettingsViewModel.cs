using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
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

    /// <summary>A colour scheme as offered in the settings, with preview colours for its tile.</summary>
    internal class ThemeOption
    {
        public ThemeOption(AppTheme theme)
        {
            Theme = theme;
            Name = ThemeManager.DisplayName(theme);
            IsSeasonal = ThemeManager.IsSeasonal(theme);

            var palette = ThemeManager.LoadPalette(theme);
            Background = (Brush)palette["Theme.AppBackground"];
            Sidebar = (Brush)palette["Theme.Sidebar"];
            Card = (Brush)palette["Theme.Card"];
            CardBorder = (Brush)palette["Theme.CardBorder"];
            Accent = (Brush)palette["Theme.Accent"];
            Text = (Brush)palette["Theme.TextSubtle"];
        }

        public AppTheme Theme { get; }
        public string Name { get; }
        public bool IsSeasonal { get; }
        public Brush Background { get; }
        public Brush Sidebar { get; }
        public Brush Card { get; }
        public Brush CardBorder { get; }
        public Brush Accent { get; }
        public Brush Text { get; }
    }

    internal class SettingsViewModel : BaseViewModel
    {
        public IReadOnlyList<ThemeOption> ThemeOptions { get; } =
            ThemeManager.AllThemes.Select(theme => new ThemeOption(theme)).ToList();

        /// <summary>The chosen colour scheme. Takes effect (and is stored) right away, without "Speichern".</summary>
        public ThemeOption SelectedTheme
        {
            get => ThemeOptions.FirstOrDefault(option => option.Theme == ThemeManager.Selected);
            set
            {
                if (value == null || value.Theme == ThemeManager.Selected)
                    return;

                Properties.Settings.Default.Theme = value.Theme.ToString();
                Properties.Settings.Default.Save();
            }
        }

        /// <summary>Holiday themes switch on by themselves around the holidays. Takes effect right away.</summary>
        public bool SeasonalThemesEnabled
        {
            get => Properties.Settings.Default.SeasonalThemesEnabled;
            set
            {
                if (value == Properties.Settings.Default.SeasonalThemesEnabled)
                    return;

                Properties.Settings.Default.SeasonalThemesEnabled = value;
                Properties.Settings.Default.Save();
            }
        }

        public string SeasonalThemesHint { get; } =
            $"Halloween, Weihnachten und Ostern erscheinen automatisch {ThemeManager.SeasonDaysBefore} Tage vor bis {ThemeManager.SeasonDaysAfter} Tage nach dem Fest.";

        /// <summary>Explains which theme is shown right now if it isn't simply the chosen one.</summary>
        public string ThemeStatusText
        {
            get
            {
                var season = ThemeManager.ActiveSeason;
                if (season != null)
                {
                    var until = ThemeManager.SeasonRange(season.Value, DateTime.Today.Year).To;
                    return $"Gerade aktiv: {ThemeManager.DisplayName(season.Value)}, automatisch bis {until:dd.MM.}";
                }

                return ThemeManager.Selected == AppTheme.System
                    ? $"Gerade aktiv: {ThemeManager.DisplayName(ThemeManager.Effective)} (wie in Windows eingestellt)"
                    : null;
            }
        }

        public int PenguinCaughtCount => Properties.Settings.Default.PenguinCaughtCount;
        public int FishCaughtCount => Properties.Settings.Default.FishCaughtCount;

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

        private bool _penguinEnabled;
        public bool PenguinEnabled
        {
            get => _penguinEnabled;
            set => SetProperty(ref _penguinEnabled, value);
        }

        // Untergrenze erst beim Speichern, sonst würde schon die erste getippte Ziffer hochgesetzt.
        private int _penguinMinDelayMinutes;
        public int PenguinMinDelayMinutes
        {
            get => _penguinMinDelayMinutes;
            set => SetProperty(ref _penguinMinDelayMinutes, value);
        }

        private int _penguinMaxDelayMinutes;
        public int PenguinMaxDelayMinutes
        {
            get => _penguinMaxDelayMinutes;
            set => SetProperty(ref _penguinMaxDelayMinutes, value);
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
            ThemeManager.ThemeChanged += (s, e) => RaisePropertyChanged(nameof(ThemeStatusText));
            Properties.Settings.Default.PropertyChanged += OnSettingsChanged;

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
            PenguinEnabled = Properties.Settings.Default.PenguinEnabled;
            PenguinMinDelayMinutes = Properties.Settings.Default.PenguinMinDelayMinutes;
            PenguinMaxDelayMinutes = Properties.Settings.Default.PenguinMaxDelayMinutes;
            ShowUpdate(UpdateService.LatestUpdate);

            AvailableBackups.Clear();
            foreach (var backup in BackupHelper.GetAvailableBackups())
            {
                AvailableBackups.Add(backup);
            }
        }

        /// <summary>
        /// Keeps values that change outside this page live: the catch counters (the penguin may
        /// show up while the settings are open) and the theme (also switchable in the sidebar).
        /// </summary>
        private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(Properties.Settings.PenguinCaughtCount):
                    RaisePropertyChanged(nameof(PenguinCaughtCount));
                    break;
                case nameof(Properties.Settings.FishCaughtCount):
                    RaisePropertyChanged(nameof(FishCaughtCount));
                    break;
                case nameof(Properties.Settings.Theme):
                    RaisePropertyChanged(nameof(SelectedTheme));
                    break;
                case nameof(Properties.Settings.SeasonalThemesEnabled):
                    RaisePropertyChanged(nameof(SeasonalThemesEnabled));
                    break;
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
                $"TimeTrack {update.VersionText} wird heruntergeladen und im Hintergrund installiert. Die Anwendung wird dazu kurz beendet und danach automatisch neu gestartet. Windows fragt dabei einmal nach der Administratorfreigabe (UAC).\n\nFortfahren?",
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
            Properties.Settings.Default.PenguinEnabled = PenguinEnabled;
            // Mindestens 10 Minuten, und "bis" darf nicht kleiner als "von" sein - dann gilt einfach eine feste Pause.
            if (PenguinMinDelayMinutes < PenguinEasterEgg.MinDelayLimitMinutes)
                PenguinMinDelayMinutes = PenguinEasterEgg.MinDelayLimitMinutes;
            if (PenguinMaxDelayMinutes < PenguinMinDelayMinutes)
                PenguinMaxDelayMinutes = PenguinMinDelayMinutes;
            Properties.Settings.Default.PenguinMinDelayMinutes = PenguinMinDelayMinutes;
            Properties.Settings.Default.PenguinMaxDelayMinutes = PenguinMaxDelayMinutes;
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
