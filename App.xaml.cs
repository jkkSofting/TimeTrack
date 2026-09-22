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

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            OfferBackupRestoreIfDbMissing();

            var startupMode = ParseStartupMode(Zeitmanagement.Properties.Settings.Default.StartupMode);

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;

            if (startupMode == StartupMode.Minimized || startupMode == StartupMode.MinimizedWithFloating)
            {
                mainWindow.WindowState = WindowState.Minimized;
            }

            mainWindow.Show();

            var mainViewModel = (MainViewModel)mainWindow.DataContext;

            if (startupMode == StartupMode.NormalWithFloating || startupMode == StartupMode.MinimizedWithFloating)
            {
                QuickSelectView.ShowFloatingWindow(mainViewModel.QuickSelectViewModel, mainWindow);
            }

            _trayService = new TrayNotificationService(mainWindow);
            mainViewModel.QuickSelectViewModel.NotificationRequested += (title, message) => _trayService.ShowBalloon(title, message);

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
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayService?.Dispose();
            (MainWindow?.DataContext as MainViewModel)?.QuickSelectViewModel?.Shutdown();

            base.OnExit(e);
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
