using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Zeitmanagement.Helpers;
using Zeitmanagement.ViewModel;
using Zeitmanagement.Views;

namespace Zeitmanagement
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private TrayNotificationService _trayService;
        private PenguinEasterEgg _penguin;
        private DaubnerEasterEgg _daubner;

        // Kept on its own: Application.MainWindow can already be null by the time OnExit runs
        // (e.g. after the main window was closed), which used to skip saving the running bookings.
        private static MainViewModel _mainViewModel;

        // Only release builds are limited to one running instance; debug builds never create it.
        private SingleInstanceGuard _singleInstance;
        private bool _isDuplicateInstance;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (!TryBecomeRunningInstance(e.Args))
            {
                // Another instance is running and was asked to come to the front. Nothing of this
                // process may touch settings, database or windows.
                _isDuplicateInstance = true;
                Shutdown();
                return;
            }

            UpgradeSettingsIfNeeded();

            ThemeManager.Initialize();

            OfferBackupRestoreIfDbMissing();

            var startupMode = ParseStartupMode(Zeitmanagement.Properties.Settings.Default.StartupMode);

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;

            if (startupMode == StartupMode.Minimized || startupMode == StartupMode.MinimizedWithFloating)
            {
                mainWindow.WindowState = WindowState.Minimized;
            }

            mainWindow.Show();

            // A second start brings this window to the front instead of opening another instance.
            _singleInstance?.StartListening(() => Dispatcher.BeginInvoke(new Action(BringMainWindowToFront)));

            var mainViewModel = (MainViewModel)mainWindow.DataContext;
            _mainViewModel = mainViewModel;

            // Windows logoff/shutdown/restart and the installer's Restart Manager ask the app to
            // close via WM_QUERYENDSESSION. The process can be ended right after WM_ENDSESSION
            // returns - possibly before OnExit ever runs - so persist here, synchronously.
            SessionEnding += (s, args) => SaveBeforeExit();

            if (startupMode == StartupMode.NormalWithFloating || startupMode == StartupMode.MinimizedWithFloating)
            {
                FloatingQuickSelectWindow.ShowOrActivate(mainViewModel.QuickSelectViewModel, mainWindow);
            }

            _trayService = new TrayNotificationService(mainWindow);
            mainViewModel.QuickSelectViewModel.NotificationRequested += (title, message) => _trayService.ShowBalloon(title, message);
            mainViewModel.Plugins.NotificationRequested += (title, message, onClick) => _trayService.ShowBalloon(
                title,
                message,
                onClick: () =>
                {
                    mainWindow.Show();
                    if (mainWindow.WindowState == WindowState.Minimized)
                        mainWindow.WindowState = WindowState.Normal;
                    mainWindow.Activate();
                    onClick?.Invoke();
                });

            UpdateService.UpdateAvailable += update => _trayService.ShowBalloon(
                "Update verfügbar",
                $"TimeTrack {update.VersionText} ist verfügbar. Klicken, um es in den Einstellungen zu installieren.",
                onClick: () =>
                {
                    mainWindow.Show();
                    if (mainWindow.WindowState == WindowState.Minimized)
                        mainWindow.WindowState = WindowState.Normal;
                    mainWindow.Activate();
                    mainViewModel.SelectViewCommand.Execute("settings");
                });
            UpdateService.Start();

            if (mainViewModel.Help.WasUpdated)
            {
                _trayService.ShowBalloon(
                    "TimeTrack wurde aktualisiert",
                    $"Version {mainViewModel.Help.InstalledVersionText} ist installiert. Klicken, um zu sehen, was neu ist.",
                    onClick: () =>
                    {
                        mainWindow.Show();
                        if (mainWindow.WindowState == WindowState.Minimized)
                            mainWindow.WindowState = WindowState.Normal;
                        mainWindow.Activate();
                        mainViewModel.OpenChangelog();
                    });
            }

            _penguin = new PenguinEasterEgg();
            _penguin.Start();

            _daubner = new DaubnerEasterEgg();
            _daubner.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_isDuplicateInstance)
            {
                base.OnExit(e);
                return;
            }

            // First: running Quick Switch bookings are written now, so the flush below includes them.
            SaveBeforeExit();

            _penguin?.Dispose();
            _daubner?.Dispose();
            _trayService?.Dispose();
            ThemeManager.Shutdown();
            _mainViewModel?.QuickSelectViewModel?.Shutdown();
            _mainViewModel?.Plugins?.ShutdownAll();

            base.OnExit(e);

            // Last: a restarted process is waiting for this, and it must find everything saved.
            _singleInstance?.Dispose();
        }

        /// <summary>
        /// Release builds: takes the single-instance lock. Returns false if another instance runs
        /// (it is then told to show its window). A restart started by the app waits for the old
        /// process to exit instead of giving up. Debug builds always return true.
        /// </summary>
        private bool TryBecomeRunningInstance(string[] args)
        {
            if (!SingleInstanceGuard.IsRestrictedBuild)
                return true;

            _singleInstance = new SingleInstanceGuard();
            var wait = SingleInstanceGuard.IsRestart(args) ? SingleInstanceGuard.RestartWait : TimeSpan.Zero;
            if (_singleInstance.TryAcquire(wait))
                return true;

            var informed = _singleInstance.SignalRunningInstance();
            _singleInstance.Dispose();
            _singleInstance = null;

            if (!informed)
            {
                // Nobody answered (hung instance, or one running with other rights): without this
                // the start would end without any sign of life.
                MessageBox.Show(
                    "TimeTrack läuft bereits, das Fenster lässt sich aber nicht in den Vordergrund holen.\n\nFalls es nicht reagiert, beende es im Task-Manager und starte TimeTrack danach erneut.",
                    "TimeTrack",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            return false;
        }

        private void BringMainWindowToFront()
        {
            var window = MainWindow;
            if (window == null)
                return;

            window.Show();
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;
            window.Activate();

            // Windows often only flashes the taskbar button for a window activated from the
            // background; briefly toggling Topmost reliably raises it.
            window.Topmost = true;
            window.Topmost = false;

            // Say why no second window appeared, also when the window was already in front.
            _trayService?.ShowBalloon(
                "TimeTrack läuft bereits",
                "Es kann nur eine Instanz gleichzeitig laufen. Das vorhandene Fenster wurde in den Vordergrund geholt.");
        }

        /// <summary>
        /// Writes everything that must not be lost when the process ends: the running Quick
        /// Switch segments (they become paused) and pending note edits. Safe to call repeatedly
        /// and from several exit paths (session end, update install, normal exit) - once the
        /// slots are saved, further calls write nothing. Never throws.
        /// </summary>
        public static void SaveBeforeExit()
        {
            try
            {
                _mainViewModel?.QuickSelectViewModel?.SaveRunningBookings();
            }
            catch (Exception)
            {
                // Exit path: the flush below must still run.
            }

            try
            {
                _mainViewModel?.FlushPending();
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Each release bumps the assembly version, and .NET's user-scoped settings are stored
        /// per version - so without this, every update would silently reset all user settings
        /// (quick-select projects, startup mode, ...) back to their defaults. Runs once per
        /// installed version and copies the previous version's settings forward.
        /// </summary>
        private static void UpgradeSettingsIfNeeded()
        {
            if (Zeitmanagement.Properties.Settings.Default.SettingsUpgraded)
                return;

            Zeitmanagement.Properties.Settings.Default.Upgrade();

            // SettingsUpgraded is only true after Upgrade() if a previous install had it set. Coming
            // from a version before the changelog existed counts as having seen everything up to
            // then, so the update hint and the "Neu für dich" marks show for what came after.
            if (Zeitmanagement.Properties.Settings.Default.SettingsUpgraded &&
                string.IsNullOrEmpty(Zeitmanagement.Properties.Settings.Default.LastSeenChangelogVersion))
            {
                Zeitmanagement.Properties.Settings.Default.LastSeenChangelogVersion = "2026.43.0";
            }

            Zeitmanagement.Properties.Settings.Default.SettingsUpgraded = true;
            Zeitmanagement.Properties.Settings.Default.Save();
        }

        private static StartupMode ParseStartupMode(string value)
        {
            return Enum.TryParse(value, out StartupMode mode) ? mode : StartupMode.Normal;
        }

        private static void OfferBackupRestoreIfDbMissing()
        {
            if (File.Exists(BackupHelper.DbFilePath))
                return;

            var latest = BackupHelper.GetAvailableBackups().FirstOrDefault();
            if (latest == null)
                return;

            var result = MessageBox.Show(
                $"Es wurde keine Datenbank gefunden. Eine Sicherung vom {latest.CreatedAt:g} wurde gefunden.\n\nMöchten Sie diese Sicherung laden?\n\nJa = Sicherung laden\nNein = neue, leere Datenbank erstellen",
                "Datenbank wiederherstellen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                BackupHelper.RestoreBackup(latest.FullPath);
            }
        }
    }
}
