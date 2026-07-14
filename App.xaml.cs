using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
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
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var startupMode = ParseStartupMode(Zeitmanagement.Properties.Settings.Default.StartupMode);

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;

            if (startupMode == StartupMode.Minimized || startupMode == StartupMode.MinimizedWithFloating)
            {
                mainWindow.WindowState = WindowState.Minimized;
            }

            mainWindow.Show();

            if (startupMode == StartupMode.NormalWithFloating || startupMode == StartupMode.MinimizedWithFloating)
            {
                var mainViewModel = (MainViewModel)mainWindow.DataContext;
                QuickSelectView.ShowFloatingWindow(mainViewModel.QuickSelectViewModel, mainWindow);
            }
        }

        private static StartupMode ParseStartupMode(string value)
        {
            return Enum.TryParse(value, out StartupMode mode) ? mode : StartupMode.Normal;
        }
    }
}
