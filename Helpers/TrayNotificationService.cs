using System;
using System.Reflection;
using System.Windows;
using Zeitmanagement.ViewModel;
using Zeitmanagement.Views;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Zeitmanagement.Helpers
{
    /// <summary>
    /// Owns the app's persistent tray icon and turns auto-stop/reminder events from
    /// <see cref="QuickSelectViewModel"/> into Windows tray balloon notifications.
    /// </summary>
    internal sealed class TrayNotificationService : IDisposable
    {
        private readonly Window _mainWindow;
        private readonly Forms.NotifyIcon _notifyIcon;
        private Action _pendingBalloonClickAction;

        public TrayNotificationService(Window mainWindow)
        {
            _mainWindow = mainWindow;

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Öffnen", null, (s, e) => RestoreMainWindow());
            menu.Items.Add("Beenden", null, (s, e) => Application.Current.Shutdown());

            _notifyIcon = new Forms.NotifyIcon
            {
                Icon = Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location),
                Text = "TimeTrack",
                ContextMenuStrip = menu,
                Visible = true
            };

            _notifyIcon.DoubleClick += (s, e) => RestoreMainWindow();
            _notifyIcon.BalloonTipClicked += (s, e) => (_pendingBalloonClickAction ?? OpenQuickSwitch)();
        }

        public void ShowBalloon(string title, string message, Action onClick = null)
        {
            _pendingBalloonClickAction = onClick;
            _notifyIcon.ShowBalloonTip(6000, title, message, Forms.ToolTipIcon.Info);
        }

        private void RestoreMainWindow()
        {
            _mainWindow.Show();
            if (_mainWindow.WindowState == WindowState.Minimized)
                _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }

        private void OpenQuickSwitch()
        {
            if (_mainWindow.DataContext is MainViewModel mainViewModel)
                QuickSelectView.ShowFloatingWindow(mainViewModel.QuickSelectViewModel, _mainWindow);
        }

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }
}
